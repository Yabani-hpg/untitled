using System;
using System.Collections.Generic;
using Godot;
using Untitled.Data;

namespace Untitled.Map;

public enum LabelKind
{
	Province = 0,
	Lake = 1,
	Country = 2,
}

/// <summary>
/// Where a map label goes: text along a gentle curve through the middle of a region, Paradox style.
/// The curve is s = A t^2 + B t in a frame centred on <see cref="Center"/>, with t along <see cref="Axis"/>
/// and s along the "up" side of it (all in map pixels).
/// </summary>
public sealed class LabelPlacement
{
	public string Text;
	public LabelKind Kind;
	public Vector2 Center;
	public Vector2 Axis;          // unit, reading direction (never pointing west)
	public float A, B;
	public float MaxLength;       // room along the curve
	public float MaxHeight;       // tallest letters that fit across the region

	/// <summary>"Up" for letters: the axis turned toward north (-y on the map).</summary>
	public Vector2 Up => new(Axis.Y, -Axis.X);

	public Vector2 PointAt(float t) => Center + Axis * t + Up * (A * t * t + B * t);

	public Vector2 TangentAt(float t) => (Axis + Up * (2f * A * t + B)).Normalized();
}

/// <summary>
/// Computes label placements from the province map. Every pixel is visited a few times, so run it off the
/// main thread. Country labels cover each country's largest connected territory.
/// </summary>
public static class LabelLayout
{
	const int Bins = 24;
	const float MaxProvinceHeight = 14f;
	const float MaxCountryHeight = 90f;
	const float MaxBendShare = 0.12f;      // curve sagitta over label length
	const int MinPixels = 12;

	/// <summary>Label per land/lake province (not the ocean) and per country's main territory.</summary>
	public static List<LabelPlacement> Compute(ProvinceMap map, IReadOnlyList<Province> provinces,
		IReadOnlyDictionary<string, Country> countries, bool includeProvinces = true, bool includeCountries = true)
	{
		int n = provinces.Count;
		ReadOnlySpan<int> ids = map.Ids;
		int w = map.Width, h = map.Height;

		var area = new long[n];
		foreach (int id in ids)
			area[id]++;

		// label groups: provinces 0..n-1, then one per country's largest territory
		var groupOfProvince = new int[n];
		var countryGroup = new int[n];
		var texts = new List<string>();
		var kinds = new List<LabelKind>();
		for (int i = 0; i < n; i++)
		{
			groupOfProvince[i] = -1;
			countryGroup[i] = -1;
			Province p = provinces[i];
			if (!includeProvinces || p == null || p.IsSea)
				continue;
			groupOfProvince[i] = texts.Count;
			texts.Add(ShortName(p.Name));
			kinds.Add(p.IsLake ? LabelKind.Lake : LabelKind.Province);
		}
		if (includeCountries)
		{
			foreach (var (tag, provinceIds) in LargestTerritories(provinces, area))
			{
				int g = texts.Count;
				texts.Add(countries.TryGetValue(tag, out Country c) ? c.Name.ToUpperInvariant() : tag);
				kinds.Add(LabelKind.Country);
				foreach (int pid in provinceIds)
					countryGroup[pid] = g;
			}
		}

		int groups = texts.Count;
		var g1 = new Accumulator[groups];
		for (int g = 0; g < groups; g++)
			g1[g].RefX = float.NaN;

		// pass 1: moments (x unwrapped around each group's first pixel, so date-line regions stay whole)
		ForEachPixel(ids, w, h, groupOfProvince, countryGroup, (g, x, y) => g1[g].Add(x, y, w));

		var frames = new Frame[groups];
		for (int g = 0; g < groups; g++)
			frames[g] = g1[g].Count >= MinPixels ? g1[g].Frame() : default;

		// pass 2: extent along the axis
		var tMin = new float[groups];
		var tMax = new float[groups];
		Array.Fill(tMin, float.MaxValue);
		Array.Fill(tMax, float.MinValue);
		ForEachPixel(ids, w, h, groupOfProvince, countryGroup, (g, x, y) =>
		{
			if (!frames[g].Valid) return;
			float t = frames[g].T(Unwrap(x, g1[g].RefX, w), y);
			if (t < tMin[g]) tMin[g] = t;
			if (t > tMax[g]) tMax[g] = t;
		});

		// pass 3: per bin along the axis, how far the region reaches on either side
		var sLo = new float[groups * Bins];
		var sHi = new float[groups * Bins];
		var count = new int[groups * Bins];
		Array.Fill(sLo, float.MaxValue);
		Array.Fill(sHi, float.MinValue);
		ForEachPixel(ids, w, h, groupOfProvince, countryGroup, (g, x, y) =>
		{
			if (!frames[g].Valid) return;
			float ux = Unwrap(x, g1[g].RefX, w);
			float t = frames[g].T(ux, y);
			float s = frames[g].S(ux, y);
			int b = Math.Clamp((int)((t - tMin[g]) / Math.Max(tMax[g] - tMin[g], 1e-3f) * Bins), 0, Bins - 1);
			int k = g * Bins + b;
			if (s < sLo[k]) sLo[k] = s;
			if (s > sHi[k]) sHi[k] = s;
			count[k]++;
		});

		var result = new List<LabelPlacement>();
		for (int g = 0; g < groups; g++)
		{
			if (!frames[g].Valid)
				continue;
			var placement = Fit(frames[g], tMin[g], tMax[g], sLo, sHi, count, g * Bins, kinds[g]);
			if (placement == null)
				continue;
			placement.Text = texts[g];
			placement.Kind = kinds[g];
			result.Add(placement);
		}
		return result;
	}

	/// <summary>"Waset (Thebes)" -> "Waset": the map shows the short name.</summary>
	static string ShortName(string name)
	{
		int i = name.IndexOf(" (", StringComparison.Ordinal);
		return i > 0 ? name[..i] : name;
	}

	/// <summary>Per country, the provinces of its largest connected (shared border) territory.</summary>
	static IEnumerable<(string, List<int>)> LargestTerritories(IReadOnlyList<Province> provinces, long[] area)
	{
		var seen = new bool[provinces.Count];
		var best = new Dictionary<string, (long Area, List<int> Ids)>();
		foreach (Province start in provinces)
		{
			if (start?.OwnerTag == null || start.IsWater || seen[start.Id])
				continue;
			var component = new List<int>();
			long total = 0;
			var stack = new Stack<Province>();
			stack.Push(start);
			seen[start.Id] = true;
			while (stack.Count > 0)
			{
				Province p = stack.Pop();
				component.Add(p.Id);
				total += area[p.Id];
				foreach (Adjacency link in p.Neighbors)
				{
					Province q = link.To;
					if (link.SharesBorder && !seen[q.Id] && q.OwnerTag == start.OwnerTag && !q.IsWater)
					{
						seen[q.Id] = true;
						stack.Push(q);
					}
				}
			}
			if (!best.TryGetValue(start.OwnerTag, out var cur) || total > cur.Area)
				best[start.OwnerTag] = (total, component);
		}
		foreach (var (tag, entry) in best)
			yield return (tag, entry.Ids);
	}

	static LabelPlacement Fit(Frame f, float tMin, float tMax, float[] sLo, float[] sHi, int[] count, int off, LabelKind kind)
	{
		float binLen = (tMax - tMin) / Bins;
		// the text spans the bins holding the central ~80% of the region
		long total = 0;
		for (int b = 0; b < Bins; b++)
			total += count[off + b];
		if (total < MinPixels)
			return null;
		int lo = 0, hi = Bins - 1;
		long acc = 0;
		for (int b = 0; b < Bins; b++)
		{
			acc += count[off + b];
			if (acc < 0.1 * total) lo = b + 1;
			if (acc <= 0.9 * total) hi = b;
		}
		hi = Math.Max(hi, lo);

		// centre line of the region: weighted least-squares parabola through the bins' midpoints
		double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, y0 = 0, y1 = 0, y2 = 0;
		var widths = new List<float>();
		for (int b = lo; b <= hi; b++)
		{
			int k = off + b;
			if (count[k] == 0)
				continue;
			double t = tMin + (b + 0.5) * binLen;
			double mid = 0.5 * (sLo[k] + sHi[k]);
			double wgt = count[k];
			s0 += wgt; s1 += wgt * t; s2 += wgt * t * t; s3 += wgt * t * t * t; s4 += wgt * t * t * t * t;
			y0 += wgt * mid; y1 += wgt * mid * t; y2 += wgt * mid * t * t;
			widths.Add(sHi[k] - sLo[k] + 1f);
		}
		if (widths.Count == 0)
			return null;
		var (a, bCoef, c) = SolveQuadratic(s0, s1, s2, s3, s4, y0, y1, y2, widths.Count >= 3);

		float tLo = tMin + lo * binLen, tHi = tMin + (hi + 1) * binLen;
		float tMid = 0.5f * (tLo + tHi);
		float length = tHi - tLo;
		widths.Sort();
		float narrow = widths[widths.Count / 4];       // lower quartile: letters must fit most of the way

		// re-centre the curve at tMid and keep the bend gentle
		float sMid = (float)(a * tMid * tMid + bCoef * tMid + c);
		float slope = (float)(2 * a * tMid + bCoef);
		float bend = (float)a;
		float maxBend = MaxBendShare * length / Math.Max(0.25f * length * length, 1e-3f);
		bend = Math.Clamp(bend, -maxBend, maxBend);
		slope = Math.Clamp(slope, -0.6f, 0.6f);

		Vector2 center = f.Origin + f.Axis * tMid + f.Up * sMid;
		var p = new LabelPlacement
		{
			Center = center,
			Axis = f.Axis,
			A = bend,
			B = slope,
			MaxLength = 0.92f * length,
			MaxHeight = Math.Min(0.55f * narrow, kind == LabelKind.Country ? MaxCountryHeight : MaxProvinceHeight),
		};
		return p;
	}

	static (double, double, double) SolveQuadratic(double s0, double s1, double s2, double s3, double s4,
		double y0, double y1, double y2, bool quadratic)
	{
		if (!quadratic)
			return (0, 0, y0 / s0);
		// normal equations [s4 s3 s2; s3 s2 s1; s2 s1 s0] [a b c] = [y2 y1 y0]
		double det = s4 * (s2 * s0 - s1 * s1) - s3 * (s3 * s0 - s1 * s2) + s2 * (s3 * s1 - s2 * s2);
		if (Math.Abs(det) < 1e-9)
			return (0, 0, y0 / s0);
		double da = y2 * (s2 * s0 - s1 * s1) - s3 * (y1 * s0 - s1 * y0) + s2 * (y1 * s1 - s2 * y0);
		double db = s4 * (y1 * s0 - s1 * y0) - y2 * (s3 * s0 - s1 * s2) + s2 * (s3 * y0 - y1 * s2);
		double dc = s4 * (s2 * y0 - y1 * s1) - s3 * (s3 * y0 - y1 * s2) + y2 * (s3 * s1 - s2 * s2);
		return (da / det, db / det, dc / det);
	}

	delegate void PixelAction(int group, int x, int y);

	static void ForEachPixel(ReadOnlySpan<int> ids, int w, int h, int[] groupOfProvince, int[] countryGroup, PixelAction act)
	{
		for (int y = 0; y < h; y++)
		{
			int row = y * w;
			for (int x = 0; x < w; x++)
			{
				int id = ids[row + x];
				int g = groupOfProvince[id];
				if (g >= 0)
					act(g, x, y);
				int c = countryGroup[id];
				if (c >= 0)
					act(c, x, y);
			}
		}
	}

	static float Unwrap(float x, float refX, int w)
	{
		float d = x - refX;
		if (d > 0.5f * w) return x - w;
		if (d < -0.5f * w) return x + w;
		return x;
	}

	struct Accumulator
	{
		public double Count, Sx, Sy, Sxx, Syy, Sxy;
		public float RefX;

		public void Add(int x, int y, int w)
		{
			if (float.IsNaN(RefX))
				RefX = x;
			double ux = Unwrap(x + 0.5f, RefX + 0.5f, w), uy = y + 0.5;
			Count++; Sx += ux; Sy += uy; Sxx += ux * ux; Syy += uy * uy; Sxy += ux * uy;
		}

		public Frame Frame()
		{
			double mx = Sx / Count, my = Sy / Count;
			double cxx = Sxx / Count - mx * mx, cyy = Syy / Count - my * my, cxy = Sxy / Count - mx * my;
			double angle = 0.5 * Math.Atan2(2 * cxy, cxx - cyy);   // major axis
			var axis = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
			if (axis.X < -1e-4f || (Math.Abs(axis.X) <= 1e-4f && axis.Y > 0f))
				axis = -axis;                                       // read west to east (or upward)
			return new Frame { Valid = true, Origin = new Vector2((float)mx, (float)my), Axis = axis };
		}
	}

	struct Frame
	{
		public bool Valid;
		public Vector2 Origin, Axis;
		public Vector2 Up => new(Axis.Y, -Axis.X);
		public float T(float x, float y) => (x + 0.5f - Origin.X) * Axis.X + (y + 0.5f - Origin.Y) * Axis.Y;
		public float S(float x, float y) => (x + 0.5f - Origin.X) * Up.X + (y + 0.5f - Origin.Y) * Up.Y;
	}
}
