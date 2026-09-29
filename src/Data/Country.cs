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
	public Color MapColor { get; }

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

/// <summary>One of a country's flags, flown while its condition holds.</summary>
public sealed record FlagDefinition(string Id, string Name, string ImagePath, Condition Condition);

/// <summary>What data/countries/TAG.json sets up at the start of a game.</summary>
public sealed class CountryDefinition
{
	public string Tag { get; init; }
	public string CapitalProvince { get; init; }
	public string CapitalName { get; init; }
	/// <summary>Provinces the country owns at the start besides those it owns on the map (by area or name).</summary>
	public List<int> StartProvinces { get; } = new();
	public RulerDefinition Ruler { get; init; }
}

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
