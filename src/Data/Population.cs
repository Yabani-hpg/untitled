namespace Untitled.Data;

/// <summary>People of one culture, religion and occupation living in a province, counted in units of 1000.</summary>
public sealed class PopGroup
{
	public const int PeoplePerUnit = 1000;

	public Culture Culture { get; }
	public Religion Religion { get; }
	public Occupation Occupation { get; }
	public int Units { get; set; }

	public int People => Units * PeoplePerUnit;

	public PopGroup(Culture culture, Religion religion, Occupation occupation, int units)
	{
		Culture = culture;
		Religion = religion;
		Occupation = occupation;
		Units = units;
	}

	/// <summary>Same culture, religion and occupation: the two groups merge when they meet.</summary>
	public bool SameKind(PopGroup other) =>
		Culture == other.Culture && Religion == other.Religion && Occupation == other.Occupation;
}

/// <summary>A production building standing in a province. Each level adds its output, inputs and jobs once more.</summary>
public sealed class Building
{
	public BuildingType Type { get; }
	public int Level { get; set; }
	/// <summary>True if the population put it up by itself rather than the government.</summary>
	public bool BuiltByPopulation { get; set; }

	public Building(BuildingType type, int level, bool builtByPopulation)
	{
		Type = type;
		Level = level;
		BuiltByPopulation = builtByPopulation;
	}
}

/// <summary>Who lives in a province that no country controls.</summary>
public enum Inhabitants
{
	/// <summary>Nobody: land for settlers, later.</summary>
	Empty,
	/// <summary>Settled but unorganized tribes.</summary>
	Tribes,
	/// <summary>Nomads.</summary>
	Nomads,
}

public enum ControlKind
{
	/// <summary>A core province: fully part of the country, with no separatism.</summary>
	Core,
	/// <summary>The land of a tribe that allied with the country and was absorbed into it.</summary>
	Absorbed,
	/// <summary>Nomads subjugated by force and held by a garrison.</summary>
	Subjugated,
}

/// <summary>How a country holds a province. Held in rein for 50 years, a province becomes a core.</summary>
public sealed class ProvinceControl
{
	public ControlKind Kind { get; set; }
	/// <summary>When the country took control; separatism fades over the years after it.</summary>
	public Untitled.Core.GameDate Since { get; set; }
	/// <summary>Regiments (of 1000 soldiers) stationed in the province.</summary>
	public int Garrison { get; set; }
}
