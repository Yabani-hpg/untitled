using System;
using System.Collections.Generic;
using Godot;
using Untitled.Data;

namespace Untitled.Map;

/// <summary>
/// Per-pixel province ids decoded once from map/provinces.png, so lookups are an array read.
/// The map wraps horizontally (east-west) and clamps vertically.
/// </summary>
public sealed class ProvinceMap
{
	public const int NoProvince = 0;

	public int Width { get; }
	public int Height { get; }

	readonly int[] _ids;

	ProvinceMap(int width, int height, int[] ids)
	{
		Width = width;
		Height = height;
		_ids = ids;
	}

	/// <summary>
	/// Decodes <paramref name="image"/>. Pixels whose color no province uses get <see cref="NoProvince"/>;
	/// each such color is reported once in <paramref name="unknownColors"/> with the first pixel it was seen at.
	/// </summary>
	public static ProvinceMap Build(Image image, IReadOnlyList<Province> provinces, out Dictionary<Color, Vector2I> unknownColors)
	{
		var idByRgb = new Dictionary<uint, int>();
		foreach (Province p in provinces)
		{
			if (p != null)
				idByRgb[Pack((byte)p.MapColor.R8, (byte)p.MapColor.G8, (byte)p.MapColor.B8)] = p.Id;
		}

		if (image.GetFormat() != Image.Format.Rgb8)
		{
			image = (Image)image.Duplicate();
			image.Convert(Image.Format.Rgb8);
		}

		int width = image.GetWidth();
		int height = image.GetHeight();
		byte[] rgb = image.GetData();
		var ids = new int[width * height];
		unknownColors = new Dictionary<Color, Vector2I>();

		// Provinces are large runs of one color, so caching the last lookup skips most dictionary hits.
		uint lastKey = uint.MaxValue;
		int lastId = NoProvince;
		for (int i = 0; i < ids.Length; i++)
		{
			int o = i * 3;
			uint key = Pack(rgb[o], rgb[o + 1], rgb[o + 2]);
			if (key != lastKey)
			{
				lastKey = key;
				if (!idByRgb.TryGetValue(key, out lastId))
				{
					lastId = NoProvince;
					unknownColors.TryAdd(Color.Color8(rgb[o], rgb[o + 1], rgb[o + 2]), new Vector2I(i % width, i / width));
				}
			}
			ids[i] = lastId;
		}

		return new ProvinceMap(width, height, ids);
	}

	/// <summary>Row-major province id per pixel (width * height).</summary>
	public ReadOnlySpan<int> Ids => _ids;

	/// <summary>Province at pixel (x, y). x wraps around the map; y outside the map returns <see cref="NoProvince"/>.</summary>
	public int GetIdAtPixel(int x, int y)
	{
		if (y < 0 || y >= Height)
			return NoProvince;
		x = ((x % Width) + Width) % Width;
		return _ids[y * Width + x];
	}

	/// <summary>Province at normalized map coordinates (0..1, origin at the north-west corner).</summary>
	public int GetIdAtUv(Vector2 uv)
	{
		if (uv.Y < 0f || uv.Y >= 1f)
			return NoProvince;
		return GetIdAtPixel((int)MathF.Floor(uv.X * Width), (int)MathF.Floor(uv.Y * Height));
	}

	Vector2[] _centroids;

	/// <summary>
	/// Centre of a province's pixels, in pixels, or null if it has none. Computed for all provinces on first
	/// use. Provinces across the date line average around their first pixel's side of the map.
	/// </summary>
	public Vector2? GetCentroid(int id)
	{
		if (_centroids == null)
		{
			int max = 0;
			foreach (int i in _ids)
				max = Math.Max(max, i);
			var sx = new double[max + 1];
			var sy = new double[max + 1];
			var n = new int[max + 1];
			var firstX = new int[max + 1];
			for (int y = 0; y < Height; y++)
			{
				for (int x = 0; x < Width; x++)
				{
					int i = _ids[y * Width + x];
					if (n[i] == 0)
						firstX[i] = x;
					int dx = x - firstX[i];
					int ux = dx > Width / 2 ? x - Width : dx < -Width / 2 ? x + Width : x;
					sx[i] += ux + 0.5;
					sy[i] += y + 0.5;
					n[i]++;
				}
			}
			var centroids = new Vector2[max + 1];
			for (int i = 0; i <= max; i++)
				centroids[i] = n[i] > 0 ? new Vector2((float)(sx[i] / n[i]), (float)(sy[i] / n[i])) : new Vector2(float.NaN, float.NaN);
			_centroids = centroids;
		}
		if (id <= 0 || id >= _centroids.Length || float.IsNaN(_centroids[id].X))
			return null;
		return _centroids[id];
	}

	static uint Pack(byte r, byte g, byte b) => ((uint)r << 16) | ((uint)g << 8) | b;
}
