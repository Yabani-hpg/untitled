using System;
using System.Collections.Generic;
using Godot;

namespace Untitled.Map;

/// <summary>
/// Traces the borders between land provinces in the province map and smooths them into polylines,
/// so they can be drawn as crisp lines at any zoom instead of pixel staircases.
/// </summary>
public static class ProvinceBorderBuilder
{
	/// <summary>A border between provinces A and B, in map pixel coordinates (0..width, 0..height).</summary>
	public sealed class Polyline
	{
		public int A, B;
		public Vector2[] Points;
	}

	/// <summary>Chaikin corner-cutting passes after the staircase is reduced to edge midpoints.</summary>
	const int SmoothPasses = 2;
	/// <summary>Max deviation (px) when dropping nearly collinear points after smoothing.</summary>
	const float SimplifyTolerance = 0.06f;

	/// <param name="isLand">indexed by province id; borders are traced only between two land provinces</param>
	public static List<Polyline> Build(ProvinceMap map, bool[] isLand)
	{
		int w = map.Width, h = map.Height;
		ReadOnlySpan<int> ids = map.Ids;
		int stride = w + 1; // lattice points are pixel corners

		// 1) unit edges between two different land provinces, keyed by province pair
		var edges = new List<(long Pair, int P0, int P1)>();
		for (int y = 0; y < h; y++)
		{
			int row = y * w;
			for (int x = 0; x < w; x++)
			{
				int a = ids[row + x];
				if (x + 1 < w)
				{
					int b = ids[row + x + 1];
					if (a != b && isLand[a] && isLand[b])
						edges.Add((PairKey(a, b), y * stride + x + 1, (y + 1) * stride + x + 1));
				}
				if (y + 1 < h)
				{
					int b = ids[row + w + x];
					if (a != b && isLand[a] && isLand[b])
						edges.Add((PairKey(a, b), (y + 1) * stride + x, (y + 1) * stride + x + 1));
				}
			}
		}
		edges.Sort((l, r) => l.Pair.CompareTo(r.Pair));

		// 2) chain each pair's edges into polylines between junctions (or closed loops)
		var result = new List<Polyline>();
		var adjacency = new Dictionary<int, List<int>>();
		var used = new bool[edges.Count];
		for (int start = 0; start < edges.Count;)
		{
			int end = start;
			while (end < edges.Count && edges[end].Pair == edges[start].Pair)
				end++;

			adjacency.Clear();
			for (int i = start; i < end; i++)
			{
				AddAdjacent(adjacency, edges[i].P0, i);
				AddAdjacent(adjacency, edges[i].P1, i);
			}
			var (a, b) = UnpackPair(edges[start].Pair);

			// open chains start at points that aren't simple pass-throughs; then whatever is left is loops
			foreach (var (point, list) in adjacency)
			{
				if (list.Count == 2)
					continue;
				foreach (int e in list)
				{
					if (!used[e])
						result.Add(MakePolyline(a, b, Walk(point, e, edges, adjacency, used), false, stride));
				}
			}
			for (int i = start; i < end; i++)
			{
				if (!used[i])
					result.Add(MakePolyline(a, b, Walk(edges[i].P0, i, edges, adjacency, used), true, stride));
			}
			start = end;
		}
		return result;
	}

	static List<int> Walk(int point, int edge, List<(long Pair, int P0, int P1)> edges,
		Dictionary<int, List<int>> adjacency, bool[] used)
	{
		var points = new List<int> { point };
		while (edge >= 0 && !used[edge])
		{
			used[edge] = true;
			point = edges[edge].P0 == point ? edges[edge].P1 : edges[edge].P0;
			points.Add(point);
			var next = adjacency[point];
			edge = -1;
			if (next.Count == 2)
			{
				foreach (int e in next)
				{
					if (!used[e])
						edge = e;
				}
			}
		}
		return points;
	}

	static Polyline MakePolyline(int a, int b, List<int> lattice, bool closed, int stride)
	{
		var pts = new List<Vector2>(lattice.Count);
		foreach (int p in lattice)
			pts.Add(new Vector2(p % stride, p / stride));
		if (closed && pts.Count > 1 && pts[0] == pts[^1])
			pts.RemoveAt(pts.Count - 1);

		pts = Midpoints(pts, closed);
		for (int i = 0; i < SmoothPasses; i++)
			pts = Chaikin(pts, closed);
		pts = Simplify(pts, SimplifyTolerance);
		if (closed)
			pts.Add(pts[0]);
		return new Polyline { A = a, B = b, Points = pts.ToArray() };
	}

	/// <summary>Replaces the staircase corners with edge midpoints, which turns 1-px steps into straight diagonals.</summary>
	static List<Vector2> Midpoints(List<Vector2> pts, bool closed)
	{
		var result = new List<Vector2>(pts.Count + 1);
		if (!closed)
			result.Add(pts[0]);
		for (int i = 0; i + 1 < pts.Count; i++)
			result.Add((pts[i] + pts[i + 1]) * 0.5f);
		if (closed)
			result.Add((pts[^1] + pts[0]) * 0.5f);
		else
			result.Add(pts[^1]);
		return result;
	}

	static List<Vector2> Chaikin(List<Vector2> pts, bool closed)
	{
		if (pts.Count < 3)
			return pts;
		var result = new List<Vector2>(pts.Count * 2);
		int n = closed ? pts.Count : pts.Count - 1;
		if (!closed)
			result.Add(pts[0]);
		for (int i = 0; i < n; i++)
		{
			Vector2 p = pts[i], q = pts[(i + 1) % pts.Count];
			result.Add(p.Lerp(q, 0.25f));
			result.Add(p.Lerp(q, 0.75f));
		}
		if (!closed)
			result.Add(pts[^1]); // junction endpoints stay put so neighbouring borders still meet
		return result;
	}

	/// <summary>Iterative Douglas-Peucker.</summary>
	static List<Vector2> Simplify(List<Vector2> pts, float tolerance)
	{
		if (pts.Count < 3)
			return pts;
		var keep = new bool[pts.Count];
		keep[0] = keep[^1] = true;
		var stack = new Stack<(int, int)>();
		stack.Push((0, pts.Count - 1));
		while (stack.Count > 0)
		{
			var (i0, i1) = stack.Pop();
			float maxDist = 0f;
			int index = -1;
			Vector2 a = pts[i0], ab = pts[i1] - a;
			float len2 = ab.LengthSquared();
			for (int i = i0 + 1; i < i1; i++)
			{
				Vector2 ap = pts[i] - a;
				float d = len2 > 1e-12f ? Mathf.Abs(ab.Cross(ap)) / Mathf.Sqrt(len2) : ap.Length();
				if (d > maxDist)
				{
					maxDist = d;
					index = i;
				}
			}
			if (index >= 0 && maxDist > tolerance)
			{
				keep[index] = true;
				stack.Push((i0, index));
				stack.Push((index, i1));
			}
		}
		var result = new List<Vector2>();
		for (int i = 0; i < pts.Count; i++)
		{
			if (keep[i])
				result.Add(pts[i]);
		}
		return result;
	}

	static void AddAdjacent(Dictionary<int, List<int>> adjacency, int point, int edge)
	{
		if (!adjacency.TryGetValue(point, out var list))
			adjacency[point] = list = new List<int>(2);
		list.Add(edge);
	}

	static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
	static (int, int) UnpackPair(long key) => ((int)(key >> 32), (int)(key & 0xffffffff));
}
