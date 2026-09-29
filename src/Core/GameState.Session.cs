using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Map;

namespace Untitled.Core;

public enum GamePhase
{
	/// <summary>Choosing a country on the map before the game starts.</summary>
	CountrySelection,
	Playing,
}

/// <summary>How the map is coloured.</summary>
public enum MapMode
{
	/// <summary>Terrain only.</summary>
	Natural,
	Political,
	Culture,
	/// <summary>Resources: each province's food slot.</summary>
	Food,
	/// <summary>Resources: each province's non-renewable deposit.</summary>
	Deposits,
}

// A game session: new game setup, the player's country, rulers, dynamic flags and the map mode.
public partial class GameState : IWorld
{
	[Signal]
	public delegate void PhaseChangedEventHandler();

	/// <summary>The country shown in the country selection screen changed.</summary>
	[Signal]
	public delegate void FocusedCountryChangedEventHandler(string tag);

	/// <summary>Some country's flag changed.</summary>
	[Signal]
	public delegate void FlagsChangedEventHandler();

	[Signal]
	public delegate void MapModeChangedEventHandler();

	public GamePhase Phase { get; private set; } = GamePhase.CountrySelection;
	/// <summary>The player's country, or null before one is chosen.</summary>
	public string PlayerTag { get; private set; }
	/// <summary>The country looked at in the selection screen.</summary>
	public string FocusedCountryTag { get; private set; }
	public MapMode MapMode { get; private set; } = MapMode.Political;

	public IReadOnlyDictionary<int, Character> Characters => _characters;

	readonly Dictionary<int, Character> _characters = new();
	int _nextCharacterId = 1;
	Dictionary<string, int> _provinceNames = new();

	public Country PlayerCountry => GetCountry(PlayerTag);

	GameDate IWorld.Date => Date;

	public IReadOnlyList<int> GetArea(string id) => Definitions.Areas.TryGetValue(id, out List<int> a) ? a : null;

	/// <summary>Province id by name, 0 if unknown or ambiguous.</summary>
	public int ProvinceIdByName(string name) =>
		name != null && _provinceNames.TryGetValue(name, out int id) && id > 0 ? id : 0;

	/// <summary>
	/// Sets up a fresh game on 1 January 1200 BC: provinces as on the map with their starting populations,
	/// each country file's start provinces, capitals, rulers and flags. Ends in the country selection phase.
	/// </summary>
	public void NewGame()
	{
		ulong start = Time.GetTicksMsec();
		foreach (Province p in _provinces)
			p?.ClearForNewGame();
		DataLoader.LoadProvinceSetup(DataLoader.ProvinceSetupPath, _provinces, Definitions);

		Date = StartDate;
		Paused = true;
		Speed = MinSpeed;
		_sinceTick = 0;
		_worldFlags.Clear();
		_characters.Clear();
		_nextCharacterId = 1;
		SelectedProvinceId = ProvinceMap.NoProvince;

		foreach (Country c in _countries.Values)
		{
			foreach (int id in c.Definition?.StartProvinces ?? new List<int>())
				_provinces[id].OwnerTag = c.Tag;
		}
		foreach (Country c in _countries.Values)
		{
			c.CapitalId = PickCapital(c);
			c.CapitalName = c.Definition?.CapitalName;
			c.Ruler = c.Definition?.Ruler != null ? CreateRuler(c, c.Definition.Ruler) : GenerateRuler(c);
			c.CurrentFlag = null;
		}
		RefreshFlags(emit: false);

		Phase = GamePhase.CountrySelection;
		PlayerTag = null;
		FocusedCountryTag = _countries.ContainsKey("EGY") ? "EGY" : _countries.Keys.FirstOrDefault();
		MapMode = MapMode.Political;
		GD.Print($"New game set up in {Time.GetTicksMsec() - start} ms");
	}

	int PickCapital(Country c)
	{
		int named = ProvinceIdByName(c.Definition?.CapitalProvince);
		if (named > 0 && _provinces[named].OwnerTag == c.Tag)
			return named;
		// otherwise the most populous province of the country's majority culture (not an overseas outpost)
		var owned = ProvincesOf(c.Tag).ToList();
		Culture majority = owned.SelectMany(p => p.Pops).GroupBy(g => g.Culture)
			.OrderByDescending(g => g.Sum(pop => pop.Units)).Select(g => g.Key).FirstOrDefault();
		Province best = owned.Where(p => majority == null || p.MainCulture == majority)
			.OrderByDescending(p => p.TotalUnits).FirstOrDefault();
		return best?.Id ?? 0;
	}

	Character CreateRuler(Country c, RulerDefinition d)
	{
		var ruler = new Character
		{
			Id = _nextCharacterId++,
			Name = d.Name,
			FullName = d.FullName,
			Dynasty = d.Dynasty,
			Birth = d.Birth,
			Portrait = d.Portrait,
			ProvinceId = ProvinceIdByName(d.Province) is var id && id > 0 ? id : c.CapitalId,
			Culture = Definitions.Cultures[d.Culture],
			Religion = Definitions.Religions[d.Religion],
			Occupation = Definitions.Occupations[d.Occupation],
		};
		// a ruler always belongs to a population: if the province has none of theirs, their household is one
		Province home = GetProvince(ruler.ProvinceId);
		if (home != null && ruler.Population(home) == null)
			home.AddPops(ruler.Culture, ruler.Religion, ruler.Occupation, 1);
		_characters[ruler.Id] = ruler;
		return ruler;
	}

	/// <summary>
	/// A ruler for a country without one of its own: from the largest population group of its capital,
	/// with a name, dynasty, age and portrait of that culture, picked from the country's tag so every new
	/// game starts with the same rulers.
	/// </summary>
	Character GenerateRuler(Country c)
	{
		Province capital = GetProvince(c.CapitalId);
		PopGroup pop = capital?.Pops.OrderByDescending(g => g.Units).FirstOrDefault();
		if (pop == null)
			return null;
		var rng = new Random(StableHash(c.Tag));
		string Pick(Dictionary<string, List<string>> table, string fallback) =>
			table.TryGetValue(pop.Culture.Id, out List<string> list) && list.Count > 0 ? list[rng.Next(list.Count)] : fallback;
		int age = 22 + rng.Next(40);
		var ruler = new Character
		{
			Id = _nextCharacterId++,
			Name = Pick(Definitions.RulerNames, c.Name),
			Dynasty = Pick(Definitions.DynastyNames, c.Adjective),
			Birth = StartDate.AddDays(-(long)(age * 365.2425) - rng.Next(365)),
			Portrait = Pick(Definitions.GenericPortraits, null),
			ProvinceId = capital.Id,
			Culture = pop.Culture,
			Religion = pop.Religion,
			Occupation = pop.Occupation,
		};
		_characters[ruler.Id] = ruler;
		return ruler;
	}

	static int StableHash(string s)
	{
		unchecked
		{
			int h = (int)2166136261;
			foreach (char ch in s)
				h = (h ^ ch) * 16777619;
			return h & 0x7fffffff;
		}
	}

	/// <summary>Re-evaluates every country's flag: the first of its flags whose condition holds.</summary>
	public void RefreshFlags(bool emit = true)
	{
		bool changed = false;
		foreach (Country c in _countries.Values)
		{
			FlagDefinition flag = c.Flags.FirstOrDefault(f => f.Condition == null || f.Condition.Holds(this, c)) ?? c.Flags[^1];
			if (flag != c.CurrentFlag)
			{
				changed |= c.CurrentFlag != null;
				c.CurrentFlag = flag;
			}
		}
		if (changed && emit)
			EmitSignal(SignalName.FlagsChanged);
	}

	public void FocusCountry(string tag)
	{
		if (tag == FocusedCountryTag)
			return;
		FocusedCountryTag = tag;
		EmitSignal(SignalName.FocusedCountryChanged, tag ?? "");
	}

	/// <summary>Starts the game as <paramref name="tag"/>.</summary>
	public void StartPlaying(string tag)
	{
		if (GetCountry(tag) == null)
			return;
		PlayerTag = tag;
		Phase = GamePhase.Playing;
		SelectProvince(ProvinceMap.NoProvince);
		EmitSignal(SignalName.PhaseChanged);
	}

	public void SetMapMode(MapMode mode)
	{
		if (mode == MapMode)
			return;
		MapMode = mode;
		EmitSignal(SignalName.MapModeChanged);
	}

	/// <summary>Provinces a country owns.</summary>
	public IEnumerable<Province> ProvincesOf(string tag) => _provinces.Where(p => p != null && p.OwnerTag == tag);

	/// <summary>The capital as it is called: its city name if it has one, else the province's name.</summary>
	public string CapitalText(Country c) => c.CapitalName ?? GetProvince(c.CapitalId)?.Name ?? "None";
}
