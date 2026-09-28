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

	static uint Pack(byte r, byte g, byte b) => ((uint)r << 16) | ((uint)g << 8) | b;
}
