using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Untitled.Core;

namespace Untitled.Map;

/// <summary>
/// Paradox-style names painted on the map: province (and lake) names along a curve through each province,
/// country names across each country's main territory. The shader shows province names when zoomed in and
/// country names when zoomed out. Call <see cref="RefreshCountryLabels"/> after provinces change hands.
/// </summary>
public partial class MapLabels : Node3D
{
	[Export] public NodePath FlatWorldPath { get; set; }
	[Export] public Shader LabelShader { get; set; }
	/// <summary>Main label font; missing characters fall back to <see cref="FallbackFont"/>.</summary>
	[Export] public FontFile LabelFont { get; set; }
	[Export] public FontFile FallbackFont { get; set; }

	[Export] public float MinLetterSize { get; set; } = 0.9f;     // map px; smaller labels are skipped
	[Export] public float CountryTracking { get; set; } = 0.18f;  // extra letter spacing for country names, in em
	[Export] public int ChunksX { get; set; } = 8;
	[Export] public int ChunksY { get; set; } = 4;

	const int MsdfSize = 48;         // font size the distance field atlas is rendered at
	const int MsdfPixelRange = 16;
	const float AtlasInset = 1.5f;   // atlas texels trimmed from each glyph cell
	static readonly StringName[] SharedTerrainParams = { "heightmap", "sea_level_norm", "height_scale", "terrain_base_y" };

	MeshInstance3D _flatWorld;
	Vector2 _mapSize;
	FontFile[] _fonts;
	Godot.Collections.Array<Rid> _fontRids;
	readonly Dictionary<(Rid, long), ShaderMaterial> _pageMaterials = new();
	Node3D _provinceLayer, _countryLayer;
	Task<GlyphBatch> _provinceTask, _countryTask;

	/// <summary>Glyph geometry for one label layer, built off the main thread.</summary>
	sealed class GlyphBatch
	{
		public readonly Dictionary<(int Chunk, Rid Font, long Page), GlyphMeshBuilder> Chunks = new();
		public int Placed, Total;
	}

	public override void _Ready()
	{
		GameState state = GameState.Instance;
		_flatWorld = GetNodeOrNull<MeshInstance3D>(FlatWorldPath);
		if (state?.ProvinceMap == null || _flatWorld?.Mesh is not PlaneMesh plane || LabelShader == null || LabelFont == null)
		{
			GD.PushError($"{nameof(MapLabels)}: needs GameState, FlatWorldPath with a PlaneMesh, LabelShader and LabelFont");
			return;
		}
		_mapSize = plane.Size;
		_fonts = new[] { LabelFont, FallbackFont }.Where(f => f != null).Select(MakeMsdf).ToArray();
		for (int i = 0; i + 1 < _fonts.Length; i++)
			_fonts[i].Fallbacks = new Godot.Collections.Array<Font> { _fonts[i + 1] };
		_fontRids = _fonts[0].GetRids();

		_provinceLayer = new Node3D { Name = "Provinces" };
		_countryLayer = new Node3D { Name = "Countries" };
		AddChild(_provinceLayer);
		AddChild(_countryLayer);

		ProvinceMap map = state.ProvinceMap;
		var provinces = state.Provinces;
		var countries = state.Countries;
		// layout, text shaping and glyph generation all run off the main thread (the text server locks
		// internally); only the mesh and atlas upload happens in _Process
		_provinceTask = Task.Run(() => LayOut(LabelLayout.Compute(map, provinces, countries, includeCountries: false)));
		_countryTask = Task.Run(() => LayOut(LabelLayout.Compute(map, provinces, countries, includeProvinces: false)));
	}

	GlyphBatch LayOut(List<LabelPlacement> labels)
	{
		var batch = new GlyphBatch { Total = labels.Count };
		foreach (LabelPlacement label in labels)
		{
			if (AddLabel(label, batch.Chunks))
				batch.Placed++;
		}
		return batch;
	}

	public override void _Process(double delta)
	{
		if (_provinceTask is { IsCompleted: true })
		{
			Build(_provinceTask, _provinceLayer, "province");
			_provinceTask = null;
		}
		if (_countryTask is { IsCompleted: true })
		{
			Build(_countryTask, _countryLayer, "country");
			_countryTask = null;
		}
		if (_flatWorld != null)
			Visible = _flatWorld.IsVisibleInTree();
	}

	public override void _EnterTree()
	{
		if (GameState.Instance != null)
			GameState.Instance.OwnershipChanged += RefreshCountryLabels;
	}

	public override void _ExitTree()
	{
		if (GameState.Instance != null)
			GameState.Instance.OwnershipChanged -= RefreshCountryLabels;
	}

	/// <summary>
	/// Recompute country names from current ownership (after conquests, unions, ...), and province names
	/// too where owners have names of their own for provinces.
	/// </summary>
	public void RefreshCountryLabels()
	{
		GameState state = GameState.Instance;
		if (state?.ProvinceMap == null || _countryLayer == null)
			return;
		ProvinceMap map = state.ProvinceMap;
		var provinces = state.Provinces;
		var countries = state.Countries;
		_countryTask = Task.Run(() => LayOut(LabelLayout.Compute(map, provinces, countries, includeProvinces: false)));
		if (provinces.Any(p => p != null && p.AlternateNames.Count > 0))
			_provinceTask = Task.Run(() => LayOut(LabelLayout.Compute(map, provinces, countries, includeCountries: false)));
	}

	void Build(Task<GlyphBatch> task, Node3D layer, string what)
	{
		if (task.IsFaulted)
		{
			GD.PushError($"{nameof(MapLabels)}: {what} layout failed: {task.Exception?.InnerException}");
			return;
		}
		foreach (Node child in layer.GetChildren())
			child.QueueFree();

		ulong start = Time.GetTicksMsec();
		GlyphBatch batch = task.Result;
		var meshes = new List<ArrayMesh>();
		foreach (var byChunk in batch.Chunks.GroupBy(kv => kv.Key.Chunk))
		{
			var mesh = new ArrayMesh();
			foreach (var ((_, font, page), builder) in byChunk)
				builder.AddSurface(mesh, PageMaterial(font, page));
			meshes.Add(mesh);
		}
		// the map wraps east-west: one copy per side, like the borders
		foreach (int offset in new[] { -1, 0, 1 })
		{
			var copy = new Node3D { Position = new Vector3(offset * _mapSize.X, 0f, 0f) };
			layer.AddChild(copy);
			foreach (ArrayMesh mesh in meshes)
			{
				copy.AddChild(new MeshInstance3D
				{
					Mesh = mesh,
					CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
					ExtraCullMargin = 40f,
				});
			}
		}
		GD.Print($"MapLabels: {batch.Placed} {what} labels ({batch.Total - batch.Placed} too small), {Time.GetTicksMsec() - start} ms on the main thread");
	}

	/// <summary>Shapes the text and lays its glyphs along the placement's curve. False if it doesn't fit.</summary>
	bool AddLabel(LabelPlacement label, Dictionary<(int, Rid, long), GlyphMeshBuilder> chunks)
	{
		TextServer ts = TextServerManager.GetPrimaryInterface();
		Rid shaped = ts.CreateShapedText();
		try
		{
			ts.ShapedTextAddString(shaped, SeparateAccents(label.Text), _fontRids, MsdfSize);
			if (!ts.ShapedTextShape(shaped))
				return false;
			var glyphs = ts.ShapedTextGetGlyphs(shaped);
			float tracking = label.Kind == LabelKind.Country ? CountryTracking * MsdfSize : 0f;
			float textWidth = (float)ts.ShapedTextGetWidth(shaped) + tracking * Math.Max(glyphs.Count - 1, 0);
			if (textWidth <= 0f)
				return false;

			// letter size: as big as the region allows along and across the curve
			float size = Math.Min(label.MaxHeight, label.MaxLength / (textWidth / MsdfSize));
			if (size < MinLetterSize)
				return false;
			float k = size / MsdfSize;   // world units per font pixel

			var arc = new ArcTable(label, 0.6f * (textWidth * k) + 2f);
			float pen = -0.5f * textWidth * k;
			float kind = (float)label.Kind;
			int chunk = ChunkOf(label.Center);

			foreach (Godot.Collections.Dictionary g in glyphs)
			{
				float advance = g["advance"].AsSingle() * k;
				// shaping offset: puts combining accents (U + ´) over their letter
				Vector2 shift = g.TryGetValue("offset", out Variant o) ? o.AsVector2() * k : Vector2.Zero;
				long index = g["index"].AsInt64();
				Rid font = g["font_rid"].AsRid();
				var fsize = new Vector2I(MsdfSize, 0);
				if (index != 0 && font.IsValid)
				{
					ts.FontRenderGlyph(font, fsize, index);
					Vector2 gsize = ts.FontGetGlyphSize(font, fsize, index) * k;
					if (gsize.X > 0f && gsize.Y > 0f)
					{
						Vector2 goff = ts.FontGetGlyphOffset(font, fsize, index) * k;     // top-left from the pen, y down
						Rect2 uv = ts.FontGetGlyphUVRect(font, fsize, index);
						long page = ts.FontGetGlyphTextureIdx(font, fsize, index);
						Vector2 texSize = PageSize(font, page);

						// glyph centre along the curve; letters sit centred on it (baseline 0.33 em below)
						float along = pen + shift.X + goff.X + 0.5f * gsize.X;
						float t = arc.TAt(along);
						Vector2 basePt = label.PointAt(t);
						Vector2 tan = label.TangentAt(t);
						var up = new Vector2(tan.Y, -tan.X);
						// trim the atlas cell's outer texels: linear filtering there picks up neighbouring glyphs
						uv = uv.Grow(-AtlasInset);
						float inset = AtlasInset * k;
						float top = -(goff.Y + shift.Y) - inset - 0.33f * size, bottom = top - gsize.Y + 2f * inset;
						float half = 0.5f * gsize.X - inset;

						var key = (chunk, font, page);
						if (!chunks.TryGetValue(key, out var builder))
							chunks[key] = builder = new GlyphMeshBuilder();
						builder.AddQuad(
							ToWorld(basePt - tan * half + up * top), ToWorld(basePt + tan * half + up * top),
							ToWorld(basePt + tan * half + up * bottom), ToWorld(basePt - tan * half + up * bottom),
							uv.Position / texSize, uv.End / texSize, new Vector2(size, kind));
					}
				}
				pen += advance + tracking * k;
			}
			return true;
		}
		finally
		{
			ts.FreeRid(shaped);
		}
	}

	readonly System.Collections.Concurrent.ConcurrentDictionary<(Rid, long), Vector2> _pageSizes = new();

	/// <summary>Atlas pages keep their size once created, so it is read once per page.</summary>
	Vector2 PageSize(Rid font, long page)
	{
		if (!_pageSizes.TryGetValue((font, page), out Vector2 size))
		{
			var image = TextServerManager.GetPrimaryInterface().FontGetTextureImage(font, new Vector2I(MsdfSize, 0), page);
			_pageSizes[(font, page)] = size = image.GetSize();
		}
		return size;
	}

	/// <summary>
	/// Draw accents as separate mark glyphs (Č -> C + combining caron). The distance-field generator
	/// mangles precomposed glyphs that fonts assemble from flipped components (Cinzel's Č Ř Š Ž Ê Â
	/// come out with a solid blob for the accent), while the standalone marks render cleanly. A combining
	/// grapheme joiner stops the shaper from recomposing the pair; mark positioning still places the accent.
	/// </summary>
	static string SeparateAccents(string text)
	{
		string nfd = text.Normalize(System.Text.NormalizationForm.FormD);
		if (nfd.Length == text.Length)
			return text;
		var sb = new System.Text.StringBuilder(nfd.Length * 2);
		foreach (char ch in nfd)
		{
			if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
				sb.Append('\u034F');
			sb.Append(ch);
		}
		return sb.ToString();
	}

	Vector3 ToWorld(Vector2 mapPx) => new(mapPx.X - 0.5f * _mapSize.X, 0f, mapPx.Y - 0.5f * _mapSize.Y);

	int ChunkOf(Vector2 mapPx)
	{
		int cx = Math.Clamp((int)(Mathf.PosMod(mapPx.X, _mapSize.X) / _mapSize.X * ChunksX), 0, ChunksX - 1);
		int cy = Math.Clamp((int)(mapPx.Y / _mapSize.Y * ChunksY), 0, ChunksY - 1);
		return cy * ChunksX + cx;
	}

	ShaderMaterial PageMaterial(Rid font, long page)
	{
		if (_pageMaterials.TryGetValue((font, page), out var mat))
		{
			// the atlas page may have grown since; refresh its texture
			mat.SetShaderParameter("atlas", PageTexture(font, page, out Vector2 size));
			mat.SetShaderParameter("atlas_size", size);
			return mat;
		}
		mat = new ShaderMaterial { Shader = LabelShader };
		mat.SetShaderParameter("atlas", PageTexture(font, page, out Vector2 texSize));
		mat.SetShaderParameter("atlas_size", texSize);
		mat.SetShaderParameter("msdf_pixel_range", (float)MsdfPixelRange);
		mat.SetShaderParameter("map_size", _mapSize);
		if (_flatWorld.Mesh.SurfaceGetMaterial(0) is ShaderMaterial terrain)
		{
			foreach (StringName p in SharedTerrainParams)
				mat.SetShaderParameter(p, terrain.GetShaderParameter(p));
		}
		_pageMaterials[(font, page)] = mat;
		return mat;
	}

	static ImageTexture PageTexture(Rid font, long page, out Vector2 size)
	{
		TextServer ts = TextServerManager.GetPrimaryInterface();
		Image image = ts.FontGetTextureImage(font, new Vector2I(MsdfSize, 0), page);
		size = image.GetSize();
		return ImageTexture.CreateFromImage(image);
	}

	/// <summary>A copy of the font that renders a multichannel signed distance field atlas (UI fonts stay as they are).</summary>
	static FontFile MakeMsdf(FontFile source) => new()
	{
		Data = source.Data,
		MultichannelSignedDistanceField = true,
		MsdfPixelRange = MsdfPixelRange,
		MsdfSize = MsdfSize,
		GenerateMipmaps = false,
	};

	/// <summary>Arc length along a label's curve, so letters are spaced evenly even where it bends.</summary>
	sealed class ArcTable
	{
		const int Steps = 64;
		readonly float[] _t = new float[Steps + 1];
		readonly float[] _len = new float[Steps + 1];

		public ArcTable(LabelPlacement label, float halfSpan)
		{
			float total = 0f;
			Vector2 prev = label.PointAt(-halfSpan);
			for (int i = 0; i <= Steps; i++)
			{
				float t = -halfSpan + 2f * halfSpan * i / Steps;
				Vector2 p = label.PointAt(t);
				total += p.DistanceTo(prev);
				prev = p;
				_t[i] = t;
				_len[i] = total;
			}
			float mid = _len[Steps / 2];
			for (int i = 0; i <= Steps; i++)
				_len[i] -= mid;       // arc position 0 = label centre
		}

		public float TAt(float arc)
		{
			if (arc <= _len[0]) return _t[0];
			for (int i = 1; i <= Steps; i++)
			{
				if (arc <= _len[i])
				{
					float f = (arc - _len[i - 1]) / Math.Max(_len[i] - _len[i - 1], 1e-6f);
					return Mathf.Lerp(_t[i - 1], _t[i], f);
				}
			}
			return _t[Steps];
		}
	}

	sealed class GlyphMeshBuilder
	{
		readonly List<Vector3> _v = new();
		readonly List<Vector2> _uv = new();
		readonly List<Vector2> _uv2 = new();
		readonly List<int> _i = new();

		/// <summary>Corners top-left, top-right, bottom-right, bottom-left.</summary>
		public void AddQuad(Vector3 tl, Vector3 tr, Vector3 br, Vector3 bl, Vector2 uvMin, Vector2 uvMax, Vector2 data)
		{
			int b = _v.Count;
			_v.Add(tl); _v.Add(tr); _v.Add(br); _v.Add(bl);
			_uv.Add(uvMin); _uv.Add(new Vector2(uvMax.X, uvMin.Y)); _uv.Add(uvMax); _uv.Add(new Vector2(uvMin.X, uvMax.Y));
			for (int n = 0; n < 4; n++)
				_uv2.Add(data);
			_i.Add(b); _i.Add(b + 1); _i.Add(b + 2);
			_i.Add(b); _i.Add(b + 2); _i.Add(b + 3);
		}

		public void AddSurface(ArrayMesh mesh, Material material)
		{
			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);
			arrays[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
			arrays[(int)Mesh.ArrayType.TexUV] = _uv.ToArray();
			arrays[(int)Mesh.ArrayType.TexUV2] = _uv2.ToArray();
			arrays[(int)Mesh.ArrayType.Index] = _i.ToArray();
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
			mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
		}
	}
}
