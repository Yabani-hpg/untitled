using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Map;
using Untitled.Rules;

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

	/// <summary>A province's populations or buildings changed (a building was built).</summary>
	[Signal]
	public delegate void ProvinceChangedEventHandler(int provinceId);

	/// <summary>A month passed: nomads migrated and populations built; any province may have changed.</summary>
	[Signal]
	public delegate void MonthAdvancedEventHandler();

	/// <summary>The game starts in 1000 BC; years before Christ are negative, with no year 0.</summary>
	public const int StartYear = -1000;

	/// <summary>Indexed by province id. Slot 0 and any unused ids are null.</summary>
	public IReadOnlyList<Province> Provinces => _provinces;
	public IReadOnlyDictionary<string, Country> Countries => _countries;
	public ProvinceMap ProvinceMap { get; private set; }
	public Texture2D ProvinceMapTexture { get; private set; }
	public Definitions Definitions { get; private set; } = new();

	public int Year { get; private set; } = StartYear;
	/// <summary>1..12</summary>
	public int Month { get; private set; } = 1;

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
		int adjacencies = DataLoader.LoadAdjacencies(DataLoader.AdjacenciesPath, _provinces);
		Definitions = DataLoader.LoadDefinitions();
		int setups = DataLoader.LoadProvinceSetup(DataLoader.ProvinceSetupPath, _provinces, Definitions);

		ProvinceMapTexture = GD.Load<Texture2D>(ProvinceMapPath)
			?? throw new DataException($"{ProvinceMapPath}: cannot load");
		ProvinceMap = ProvinceMap.Build(ProvinceMapTexture.GetImage(), _provinces, out var unknownColors);

		foreach (var (color, pixel) in unknownColors.Take(20))
			GD.PushWarning($"{ProvinceMapPath}: color #{color.ToHtml(false)} at pixel {pixel} has no row in {DataLoader.ProvincesPath}");
		if (unknownColors.Count > 20)
			GD.PushWarning($"{ProvinceMapPath}: {unknownColors.Count - 20} more unlisted colors; run tools/sync_provinces.py");

		int count = _provinces.Count(p => p != null);
		long units = _provinces.Where(p => p != null).Sum(p => (long)p.TotalUnits);
		GD.Print($"GameState: {count} provinces ({setups} populated, {units * PopGroup.PeoplePerUnit / 1e6:F1}M people), "
			+ $"{_countries.Count} countries, {adjacencies} adjacencies loaded in {Time.GetTicksMsec() - start} ms");
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

	/// <summary>"January 1000 BC"</summary>
	public string DateText =>
		$"{System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(Month)} {(Year < 0 ? $"{-Year} BC" : $"{Year} AD")}";

	/// <summary>Monthly production of a province (computed on demand; nothing is stockpiled yet).</summary>
	public ProductionReport GetProduction(int provinceId)
	{
		Province p = GetProvince(provinceId);
		return p == null || p.IsWater ? null : ProductionRules.Compute(p, Definitions);
	}

	/// <summary>The owner's government builds a building in a province, or expands it. Returns false with a reason if it can't.</summary>
	public bool GovernmentBuild(int provinceId, string buildingId, out string reason)
	{
		Province p = GetProvince(provinceId);
		BuildingType type = Definitions.GetBuilding(buildingId);
		if (p == null || type == null)
		{
			reason = "Unknown province or building";
			return false;
		}
		if (p.OwnerTag == null)
		{
			reason = "Nobody governs this province";
			return false;
		}
		if (!BuildingRules.Build(p, type, Builder.Government, out reason))
			return false;
		EmitSignal(SignalName.ProvinceChanged, provinceId);
		return true;
	}

	/// <summary>Advances the calendar a month: nomads migrate, then populations build for themselves.</summary>
	public void AdvanceMonth()
	{
		ulong start = Time.GetTicksMsec();
		var moves = PopulationRules.MigrateNomads(_provinces);
		int built = BuildingRules.PopulationBuildStep(_provinces, Definitions.Buildings);
		if (++Month > 12)
		{
			Month = 1;
			Year = Year == -1 ? 1 : Year + 1;
		}
		GD.Print($"{DateText}: {moves.Count} nomad migrations, {built} buildings built by populations ({Time.GetTicksMsec() - start} ms)");
		EmitSignal(SignalName.MonthAdvanced);
	}
}
