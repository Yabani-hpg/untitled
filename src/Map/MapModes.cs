using System;
using System.Threading.Tasks;
using Godot;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Map;

/// <summary>
/// Colours the map by the current map mode. Every province gets a colour in a small palette texture
/// (indexed by province id); the terrain shader looks up the id under each pixel in a second texture and
/// lays the palette colour over the land, keeping the relief shading underneath.
/// </summary>
public partial class MapModes : Node
{
	[Export] public NodePath FlatWorldPath { get; set; }

	const int PaletteSide = 256;          // up to 65536 provinces
	static readonly StringName IdsParam = "province_ids";
	static readonly StringName PaletteParam = "province_palette";
	static readonly StringName EnabledParam = "map_mode_enabled";
	static readonly StringName StripesParam = "province_stripes";

	/// <summary>Land no organized country controls: settled tribes, and nomads.</summary>
	static readonly Color TribalLand = new(0.64f, 0.60f, 0.50f);
	static readonly Color NomadLand = new(0.78f, 0.68f, 0.46f);

	static readonly Color Unowned = new(0.55f, 0.53f, 0.50f);
	const float Opacity = 0.72f;

	ShaderMaterial _material;
	Image _palette;
	ImageTexture _paletteTexture;
	/// <summary>Second colour per province, laid in diagonal stripes: held but not yet a core, or allied tribes.</summary>
	Image _stripes;
	ImageTexture _stripesTexture;

	public override void _Ready()
	{
		var flatWorld = GetNodeOrNull<MeshInstance3D>(FlatWorldPath);
		_material = (flatWorld?.MaterialOverride ?? flatWorld?.Mesh?.SurfaceGetMaterial(0)) as ShaderMaterial;
		GameState gs = GameState.Instance;
		if (_material == null || gs?.ProvinceMap == null)
		{
			GD.PushError($"{nameof(MapModes)}: needs FlatWorldPath with a ShaderMaterial and the GameState autoload");
			return;
		}

		_palette = Image.CreateEmpty(PaletteSide, PaletteSide, false, Image.Format.Rgba8);
		_paletteTexture = ImageTexture.CreateFromImage(_palette);
		_stripes = Image.CreateEmpty(PaletteSide, PaletteSide, false, Image.Format.Rgba8);
		_stripesTexture = ImageTexture.CreateFromImage(_stripes);
		_material.SetShaderParameter(StripesParam, _stripesTexture);
		_material.SetShaderParameter(PaletteParam, _paletteTexture);
		BuildIdTexture(gs.ProvinceMap);

		gs.MapModeChanged += Refresh;
		gs.FocusedCountryChanged += OnFocusChanged;
		gs.PhaseChanged += Refresh;
		gs.MonthAdvanced += Refresh;
		gs.ProvinceChanged += OnProvinceChanged;
		gs.OwnershipChanged += Refresh;
		Refresh();
	}

	public override void _ExitTree()
	{
		GameState gs = GameState.Instance;
		if (gs == null || _material == null)
			return;
		gs.MapModeChanged -= Refresh;
		gs.FocusedCountryChanged -= OnFocusChanged;
		gs.PhaseChanged -= Refresh;
		gs.MonthAdvanced -= Refresh;
		gs.ProvinceChanged -= OnProvinceChanged;
		gs.OwnershipChanged -= Refresh;
	}

	void OnFocusChanged(string tag) => Refresh();
	void OnProvinceChanged(int id) => Refresh();

	/// <summary>Province ids packed in two bytes per pixel (RG8), built off the main thread.</summary>
	async void BuildIdTexture(ProvinceMap map)
	{
		int w = map.Width, h = map.Height;
		byte[] bytes = await Task.Run(() =>
		{
			var data = new byte[w * h * 2];
			ReadOnlySpan<int> ids = map.Ids;
			for (int i = 0; i < ids.Length; i++)
			{
				data[2 * i] = (byte)(ids[i] & 0xff);
				data[2 * i + 1] = (byte)(ids[i] >> 8);
			}
			return data;
		});
		if (!IsInsideTree())
			return;
		Image image = Image.CreateFromData(w, h, false, Image.Format.Rg8, bytes);
		_material.SetShaderParameter(IdsParam, ImageTexture.CreateFromImage(image));
		Refresh();
	}

	/// <summary>Recolours every province for the current mode.</summary>
	public void Refresh()
	{
		GameState gs = GameState.Instance;
		if (gs == null || _palette == null)
			return;
		MapMode mode = gs.MapMode;
		string focus = gs.Phase == GamePhase.CountrySelection ? gs.FocusedCountryTag : null;
		_palette.Fill(new Color(0, 0, 0, 0));
		_stripes.Fill(new Color(0, 0, 0, 0));
		foreach (Province p in gs.Provinces)
		{
			if (p == null || p.IsWater || p.Id >= PaletteSide * PaletteSide)
				continue;
			Color c = ColorFor(p, mode, gs);
			if (focus != null)
			{
				// the country being chosen stands out; the rest of the world steps back
				if (p.OwnerTag == focus)
					c = mode == MapMode.Natural ? new Color(1f, 0.92f, 0.6f, 0.45f) : new Color(c.Lightened(0.25f), 0.9f);
				else if (mode == MapMode.Political)
					c = new Color(c.Darkened(0.15f), c.A);
			}
			// premultiplied, so the shader's blend across province edges doesn't darken toward uncoloured neighbours
			_palette.SetPixel(p.Id % PaletteSide, p.Id / PaletteSide, new Color(c.R * c.A, c.G * c.A, c.B * c.A, c.A));
			if (mode == MapMode.Political && StripeFor(p, gs) is Color s)
				_stripes.SetPixel(p.Id % PaletteSide, p.Id / PaletteSide, new Color(s.R * s.A, s.G * s.A, s.B * s.A, s.A));
		}
		_paletteTexture.Update(_palette);
		_stripesTexture.Update(_stripes);
		_material.SetShaderParameter(EnabledParam, mode != MapMode.Natural || focus != null);
	}

	/// <summary>
	/// Political stripes: a province held but not yet a core shows its tribes' or nomads' colour in stripes
	/// that fade as its separatism does; tribes allied with a country show that country's colour.
	/// </summary>
	static Color? StripeFor(Province p, GameState gs)
	{
		if (p.Control != null && !p.IsCore)
		{
			double separatism = Untitled.Rules.ControlRules.Separatism(p, gs.Date);
			Color land = p.Control.Kind == ControlKind.Subjugated ? NomadLand : TribalLand;
			return new Color(land.Lightened(0.15f), 0.25f + 0.6f * (float)separatism);
		}
		if (p.OwnerTag == null && gs.GetCountry(p.AlliedTag) is Country ally)
			return new Color(ally.MapColor, 0.7f);
		return null;
	}

	static Color ColorFor(Province p, MapMode mode, GameState gs)
	{
		switch (mode)
		{
			case MapMode.Political:
				Country owner = gs.GetCountry(p.OwnerTag);
				if (owner != null)
					return new Color(owner.MapColor, Opacity);
				return p.Inhabitants switch
				{
					Inhabitants.Tribes => new Color(TribalLand, 0.42f),
					Inhabitants.Nomads => new Color(NomadLand, 0.42f),
					_ => new Color(0, 0, 0, 0),
				};
			case MapMode.Culture:
				Culture culture = p.MainCulture;
				return culture == null ? new Color(Unowned, 0.35f) : new Color(culture.Color, Opacity);
			case MapMode.Food:
				return p.Food == null ? new Color(Unowned, 0.35f) : new Color(p.Food.Color, Opacity);
			case MapMode.Deposits:
				return p.NonRenewable == null ? new Color(Unowned, 0.25f) : new Color(p.NonRenewable.Color, 0.85f);
			default:
				return new Color(0, 0, 0, 0);
		}
	}
}
