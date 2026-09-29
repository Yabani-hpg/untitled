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

	/// <summary>A day passed (one tick).</summary>
	[Signal]
	public delegate void DayAdvancedEventHandler();

	/// <summary>A new month began: nomads migrated and populations built; any province may have changed.</summary>
	[Signal]
	public delegate void MonthAdvancedEventHandler();

	/// <summary>The clock was paused or resumed, or its speed changed.</summary>
	[Signal]
	public delegate void ClockChangedEventHandler();

	/// <summary>1 January 8801 HE, i.e. 1 January 1200 BC: the Bronze Age collapse.</summary>
	public static readonly GameDate StartDate = GameDate.FromHolocene(8801, 1, 1);

	public const int MinSpeed = 1;
	public const int MaxSpeed = 5;
	/// <summary>Real seconds per game day at each speed, as in Paradox games: speed 1 ticks a day a second,
	/// speed 5 ticks as fast as the game can (a day every frame).</summary>
	static readonly double[] SecondsPerDay = { 0, 1.0, 0.5, 0.2, 0.1, 0.0 };

	/// <summary>Indexed by province id. Slot 0 and any unused ids are null.</summary>
	public IReadOnlyList<Province> Provinces => _provinces;
	public IReadOnlyDictionary<string, Country> Countries => _countries;
	public ProvinceMap ProvinceMap { get; private set; }
	public Texture2D ProvinceMapTexture { get; private set; }
	public Definitions Definitions { get; private set; } = new();

	/// <summary>Today. Counted in days; the Holocene calendar governs, other calendars convert from it.</summary>
	public GameDate Date { get; private set; } = StartDate;
	public bool Paused { get; private set; } = true;
	/// <summary><see cref="MinSpeed"/>..<see cref="MaxSpeed"/></summary>
	public int Speed { get; private set; } = MinSpeed;

	/// <summary>World flags set by events, e.g. "hijra", which enables the Hijri calendar.</summary>
	public IReadOnlySet<string> WorldFlags => _worldFlags;

	readonly HashSet<string> _worldFlags = new();
	double _sinceTick;

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
		_provinceNames = DataLoader.ProvinceNames(_provinces);
		DataLoader.LoadAreas(Definitions, _provinceNames);
		DataLoader.LoadNamesAndPortraits(Definitions);
		DataLoader.LoadTribes(Definitions, _provinceNames);
		int countryFiles = DataLoader.LoadCountryFiles(_countries, Definitions, _provinceNames);

		ProvinceMapTexture = GD.Load<Texture2D>(ProvinceMapPath)
			?? throw new DataException($"{ProvinceMapPath}: cannot load");
		ProvinceMap = ProvinceMap.Build(ProvinceMapTexture.GetImage(), _provinces, out var unknownColors);

		foreach (var (color, pixel) in unknownColors.Take(20))
			GD.PushWarning($"{ProvinceMapPath}: color #{color.ToHtml(false)} at pixel {pixel} has no row in {DataLoader.ProvincesPath}");
		if (unknownColors.Count > 20)
			GD.PushWarning($"{ProvinceMapPath}: {unknownColors.Count - 20} more unlisted colors; run tools/sync_provinces.py");

		int count = _provinces.Count(p => p != null);
		GD.Print($"GameState: {count} provinces, {_countries.Count} countries ({countryFiles} with a country file), "
			+ $"{adjacencies} adjacencies loaded in {Time.GetTicksMsec() - start} ms");
		NewGame();
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

	// ------------------------------------------------------------------------------ time and calendars

	/// <summary>Today in the default (Gregorian) calendar: "1 January 1200 BC".</summary>
	public string DateText => Definitions.DefaultCalendar?.Format(Date) ?? Date.Holocene.ToString();

	public void SetPaused(bool paused)
	{
		if (paused == Paused)
			return;
		Paused = paused;
		_sinceTick = 0;
		EmitSignal(SignalName.ClockChanged);
	}

	public void SetSpeed(int speed)
	{
		speed = Math.Clamp(speed, MinSpeed, MaxSpeed);
		if (speed == Speed)
			return;
		Speed = speed;
		EmitSignal(SignalName.ClockChanged);
	}

	public override void _Process(double delta)
	{
		if (Paused)
			return;
		_sinceTick += delta;
		double interval = SecondsPerDay[Speed];
		if (interval <= 0)
		{
			AdvanceDay();          // fastest: a day every frame
			return;
		}
		// catch up at most a few days, so a slow frame doesn't turn into a burst of ticks
		for (int i = 0; i < 4 && _sinceTick >= interval; i++)
		{
			_sinceTick -= interval;
			AdvanceDay();
		}
		_sinceTick = Math.Min(_sinceTick, interval);
	}

	/// <summary>One tick: the next day. On the first of a month the monthly rules run.</summary>
	public void AdvanceDay()
	{
		Date = Date.AddDays(1);
		EmitSignal(SignalName.DayAdvanced);
		if (Date.Holocene.Day == 1)
			RunMonthlyRules();
	}

	/// <summary>Sets a world flag, which may enable calendars (and later, other content).</summary>
	public void SetWorldFlag(string flag)
	{
		if (_worldFlags.Add(flag))
			GD.Print($"{DateText}: world flag '{flag}' set");
	}

	/// <summary>Calendars in use: those needing no flag, and those whose flag an event has set.</summary>
	public IEnumerable<Calendar> EnabledCalendars => Definitions.Calendars.Where(c => c.IsEnabled(_worldFlags));

	/// <summary>
	/// The calendar a country writes its dates in: the first enabled calendar adopted by the religion of
	/// most of its people (the Hijri calendar for Islamic countries), otherwise the default.
	/// </summary>
	public Calendar CalendarOf(string countryTag)
	{
		var people = new Dictionary<string, long>();
		foreach (Province p in _provinces)
		{
			if (p == null || p.OwnerTag != countryTag)
				continue;
			foreach (PopGroup pop in p.Pops)
				people[pop.Religion.Id] = people.GetValueOrDefault(pop.Religion.Id) + pop.Units;
		}
		string religion = people.Count > 0 ? people.MaxBy(kv => kv.Value).Key : null;
		return EnabledCalendars.FirstOrDefault(c => religion != null && c.AdoptedByReligions.Contains(religion))
			?? Definitions.DefaultCalendar;
	}

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

	/// <summary>The start of a month: nomads migrate, then populations build for themselves.</summary>
	void RunMonthlyRules()
	{
		ulong start = Time.GetTicksMsec();
		var moves = PopulationRules.MigrateNomads(_provinces);
		int built = BuildingRules.PopulationBuildStep(_provinces, Definitions.Buildings);
		RunControlStep();
		RunTribeStep();
		RefreshFlags();
		if (Date.Holocene.Month == 1 && Phase == GamePhase.Playing)
			SaveGames.Autosave(this);
		GD.Print($"{DateText}: {moves.Count} nomad migrations, {built} buildings built by populations ({Time.GetTicksMsec() - start} ms)");
		EmitSignal(SignalName.MonthAdvanced);
	}
}
