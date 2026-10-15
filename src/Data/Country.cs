using System.Collections.Generic;
using Godot;
using Untitled.Core;

namespace Untitled.Data;

/// <summary>
/// A country. Tag, name and map colour come from data/countries.json; a hand-authored file in
/// data/countries/TAG.json can add its government, capital, ruler and dynamic flags.
/// The rest is live game state.
/// </summary>
public sealed class Country
{
	public string Tag { get; }
	public string Name { get; set; }
	/// <summary>Political map colour: the "color" RGB triple of countries.json, or of the country's own file.</summary>
	public Color MapColor { get; set; }

	public string Adjective { get; set; }
	public string Government { get; set; } = "Tribal chiefdom";
	/// <summary>"Pharaoh", "King", "Chief"...</summary>
	public string RulerTitle { get; set; } = "Ruler";

	/// <summary>Flags in order of preference: the first whose condition holds is flown. The last has no condition.</summary>
	public List<FlagDefinition> Flags { get; } = new();
	/// <summary>Set up by data/countries/TAG.json rather than generated.</summary>
	public CountryDefinition Definition { get; set; }

	// --- live state ---

	/// <summary>Capital province id, or 0.</summary>
	public int CapitalId { get; set; }
	/// <summary>Name of the capital city, if it differs from the province's ("Pi-Ramesses").</summary>
	public string CapitalName { get; set; }
	public Character Ruler { get; set; }
	/// <summary>How its levies are armed: percent of the regiments for each unit type id. Empty: the defaults of data/units.json.</summary>
	public Dictionary<string, int> LevyTemplate { get; } = new();
	/// <summary>The number the next army it raises takes ("3rd Army").</summary>
	public int NextArmyNumber { get; set; } = 1;
	/// <summary>The treasury, in gold (taxed from settled people).</summary>
	public double Gold { get; set; }
	/// <summary>Last month's accounts (tax, garrisons, mercenaries).</summary>
	public Untitled.Rules.Ledger LastLedger { get; set; }
	/// <summary>Gold at the start of a game (the "gold" of its country file).</summary>
	public double StartingGold { get; set; } = Untitled.Rules.EconomyRules.StartingGold;

	/// <summary>The country's laws: the option in force for each law id.</summary>
	public Dictionary<string, string> Laws { get; } = new();
	/// <summary>When each law was last changed (laws can't change again for a while).</summary>
	public Dictionary<string, GameDate> LawChanged { get; } = new();
	/// <summary>Regnal names its rulers take, and how many rulers have had each ("Ramesses" 2: the next is Ramesses III).</summary>
	public List<string> RulerNames { get; } = new();
	public Dictionary<string, int> RulerNameCounts { get; } = new();
	/// <summary>The counts at the start of a game (the country file's "ruler_name_counts").</summary>
	public Dictionary<string, int> StartRulerNameCounts { get; } = new();

	/// <summary>Technologies it knows (data/techs.json ids).</summary>
	public HashSet<string> Techs { get; } = new();
	/// <summary>Points put into each technology not yet known (kept when research moves elsewhere).</summary>
	public Dictionary<string, double> ResearchProgress { get; } = new();
	/// <summary>
	/// The technologies the country means to research, in order of priority. All research points flow
	/// into the first one whose requirements are met.
	/// </summary>
	public List<string> ResearchQueue { get; } = new();
	/// <summary>Research points stockpiled while nothing in the queue could be researched.</summary>
	public double ResearchStockpile { get; set; }
	/// <summary>Last month's research points.</summary>
	public double LastResearch { get; set; }

	/// <summary>Tribes the country's diplomats are courting (improving relations with).</summary>
	public HashSet<int> ImprovingRelations { get; } = new();
	/// <summary>Settled countries its diplomats are courting.</summary>
	public HashSet<string> ImprovingCountryRelations { get; } = new();

	// --- diplomacy ---

	/// <summary>Opinion of each other settled country, -200..200.</summary>
	public Dictionary<string, int> Opinions { get; } = new();
	/// <summary>Standing changes to opinion, by country, each with a reason and the date it fades from ("insulted", "conquered a neighbour").</summary>
	public Dictionary<string, List<OpinionModifier>> OpinionModifiers { get; } = new();
	public HashSet<string> Allies { get; } = new();
	/// <summary>Countries it has a truce with, and until when.</summary>
	public Dictionary<string, GameDate> Truces { get; } = new();
	/// <summary>Provinces it claims (ids): a casus belli, and cheaper to take.</summary>
	public HashSet<int> Claims { get; } = new();
	/// <summary>Prestige, -100..100: won in battle and in peace, lost in defeat; the world's opinion of us follows it.</summary>
	public double Prestige { get; set; }
	/// <summary>The country it is a vassal of, or null: it pays tribute, follows its overlord to war and makes no war of its own.</summary>
	public string OverlordTag { get; set; }
	/// <summary>Since when it has been a vassal.</summary>
	public GameDate VassalSince { get; set; }
	/// <summary>Stability, -3..+3.</summary>
	public int Stability { get; set; }
	/// <summary>The ruler's legitimacy, 0..100.</summary>
	public double Legitimacy { get; set; } = 100;
	/// <summary>War exhaustion, 0..20.</summary>
	public double WarExhaustion { get; set; }

	/// <summary>The flag flown now (see <see cref="Flags"/>).</summary>
	public FlagDefinition CurrentFlag { get; set; }

	public Country(string tag, string name, Color mapColor)
	{
		Tag = tag;
		Name = name;
		MapColor = mapColor;
		Adjective = name;
	}
}

/// <summary>A standing change to one country's opinion of another, fading away by <see cref="Until"/>.</summary>
public sealed record OpinionModifier(string Reason, int Amount, GameDate Until);

/// <summary>One of a country's flags, flown while its condition holds.</summary>
public sealed record FlagDefinition(string Id, string Name, string ImagePath, Condition Condition);

/// <summary>What data/countries/TAG.json sets up at the start of a game.</summary>
public sealed class CountryDefinition
{
	public string Tag { get; init; }
	public string CapitalProvince { get; init; }
	public string CapitalName { get; init; }
	/// <summary>The provinces the country controls at the start, and how.</summary>
	public List<StartProvince> StartProvinces { get; } = new();
	/// <summary>Tribes (by data/tribes.json key) allied with the country at the start, since when, and their relations.</summary>
	public List<(string Tribe, GameDate Since, int Relation)> AlliedTribes { get; } = new();
	public RulerDefinition Ruler { get; init; }
	/// <summary>Law options the country starts with, where they differ from the defaults.</summary>
	public Dictionary<string, string> Laws { get; } = new();
	/// <summary>Technologies known at the start, besides those every settled country knows.</summary>
	public List<string> Techs { get; } = new();
	/// <summary>Provinces it claims at the start (ids).</summary>
	public List<int> Claims { get; } = new();
	/// <summary>Countries allied with it at the start.</summary>
	public List<string> Allies { get; } = new();
	/// <summary>The country's own names for provinces (by province id), used while it owns them.</summary>
	public Dictionary<int, string> ProvinceNames { get; } = new();
	/// <summary>Population (in units) set by hand for provinces at the start, by province id, where the generated numbers are wrong.</summary>
	public Dictionary<int, int> ProvincePops { get; } = new();
}

/// <summary>A province a country controls at the start. <c>Since</c> null means long before the start.</summary>
public sealed record StartProvince(int ProvinceId, ControlKind Control, GameDate? Since, int Garrison);

public sealed class RulerDefinition
{
	public string Name { get; init; }
	public string FullName { get; init; }
	public string Dynasty { get; init; }
	public GameDate Birth { get; init; }
	public string Portrait { get; init; }
	public string Province { get; init; }
	public string Culture { get; init; }
	public string Religion { get; init; }
	public string Occupation { get; init; }
}
