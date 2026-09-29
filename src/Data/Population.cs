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
