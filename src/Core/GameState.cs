using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Map;

namespace Untitled.Core;

/// <summary>
/// Autoload that owns all simulation data. Scenes read from it and send it commands;
/// nothing in the 3D scene is the source of truth, which keeps save/load a matter of serializing this.
/// </summary>
public partial class GameState : Node
{
	public const string ProvinceMapPath = "res://map/provinces.png";

	public static GameState Instance { get; private set; }

	[Signal]
	public delegate void SelectedProvinceChangedEventHandler(int provinceId);

	/// <summary>Indexed by province id. Slot 0 and any unused ids are null.</summary>
	public IReadOnlyList<Province> Provinces => _provinces;
	public IReadOnlyDictionary<string, Country> Countries => _countries;
	public ProvinceMap ProvinceMap { get; private set; }
	public Texture2D ProvinceMapTexture { get; private set; }

	/// <summary><see cref="ProvinceMap.NoProvince"/> when nothing is selected.</summary>
	public int SelectedProvinceId { get; private set; } = ProvinceMap.NoProvince;

	Province[] _provinces = Array.Empty<Province>();
	Dictionary<string, Country> _countries = new();

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
	}

	public override void _Ready()
	{
		try
		{
			LoadWorld();
		}
		catch (DataException e)
		{
			GD.PushError($"Failed to load game data: {e.Message}");
		}
	}

	void LoadWorld()
	{
		ulong start = Time.GetTicksMsec();

		_countries = DataLoader.LoadCountries(DataLoader.CountriesPath);
		_provinces = DataLoader.LoadProvinces(DataLoader.ProvincesPath, _countries);

		ProvinceMapTexture = GD.Load<Texture2D>(ProvinceMapPath)
			?? throw new DataException($"{ProvinceMapPath}: cannot load");
		ProvinceMap = ProvinceMap.Build(ProvinceMapTexture.GetImage(), _provinces, out var unknownColors);

		foreach (var (color, pixel) in unknownColors.Take(20))
			GD.PushWarning($"{ProvinceMapPath}: color #{color.ToHtml(false)} at pixel {pixel} has no row in {DataLoader.ProvincesPath}");
		if (unknownColors.Count > 20)
			GD.PushWarning($"{ProvinceMapPath}: {unknownColors.Count - 20} more unlisted colors; run tools/sync_provinces.py");

		int count = _provinces.Count(p => p != null);
		GD.Print($"GameState: {count} provinces, {_countries.Count} countries loaded in {Time.GetTicksMsec() - start} ms");
	}

	public Province GetProvince(int id) =>
		id > 0 && id < _provinces.Length ? _provinces[id] : null;

	public Country GetCountry(string tag) =>
		tag != null && _countries.TryGetValue(tag, out Country c) ? c : null;

	/// <summary>Province id at a pixel of map/provinces.png. Also the entry point for GDScript, which can't see <see cref="ProvinceMap"/>.</summary>
	public int GetProvinceIdAtPixel(int x, int y) =>
		ProvinceMap?.GetIdAtPixel(x, y) ?? ProvinceMap.NoProvince;

	/// <summary>Selects a province, or clears the selection for an unknown id or <see cref="ProvinceMap.NoProvince"/>.</summary>
	public void SelectProvince(int id)
	{
		if (GetProvince(id) == null)
			id = ProvinceMap.NoProvince;
		if (id == SelectedProvinceId)
			return;
		SelectedProvinceId = id;
		EmitSignal(SignalName.SelectedProvinceChanged, id);
	}
}
