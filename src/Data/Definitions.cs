using System.Collections.Generic;
using Godot;

namespace Untitled.Data;

public sealed record Religion(string Id, string Name, Color Color);

public sealed record LanguageFamily(string Id, string Name);

/// <summary>Populations feel kinship with others who speak the same language, and less so the same family.</summary>
public sealed record Language(string Id, string Name, LanguageFamily Family);

/// <summary>A culture speaks one language and builds its cities in one art style (see CityView).</summary>
public sealed record Culture(string Id, string Name, Color Color, Language Language, string ArtStyle);

/// <summary>What a population group does for a living. Nomadic groups migrate between provinces for better pasture.</summary>
public sealed record Occupation(string Id, string Name, bool Nomadic, Color Color);

public enum ResourceCategory
{
	/// <summary>Deposits in the ground: the province's non-renewable slot.</summary>
	NonRenewable,
	/// <summary>Farmed or herded: the province's food slot.</summary>
	Food,
	/// <summary>Made by production buildings, from nature (wood) or by hand (tools).</summary>
	Produced,
}

public sealed class ResourceType
{
	public string Id { get; init; }
	public string Name { get; init; }
	public ResourceCategory Category { get; init; }
	public Color Color { get; init; }
	/// <summary>Food per working population unit per month (food resources only).</summary>
	public float FoodYield { get; init; }
	/// <summary>Occupations that work the food slot (food resources only).</summary>
	public IReadOnlyList<Occupation> WorkedBy { get; init; } = new List<Occupation>();
	/// <summary>"natural" or "manufactured" (produced resources only).</summary>
	public string Origin { get; init; }

	public bool IsWorkedBy(Occupation occupation)
	{
		foreach (Occupation o in WorkedBy)
		{
			if (o == occupation)
				return true;
		}
		return false;
	}

	public override string ToString() => Id;
}

public sealed class BuildingType
{
	/// <summary>Key in <see cref="Produces"/> standing for the province's own non-renewable resource (mines).</summary>
	public const string LocalDeposit = "$non_renewable";

	public string Id { get; init; }
	public string Name { get; init; }
	/// <summary>City view model.</summary>
	public string Model { get; init; }
	/// <summary>Output per level per month. May hold <see cref="LocalDeposit"/>.</summary>
	public IReadOnlyDictionary<string, float> Produces { get; init; }
	/// <summary>Input per level per month, taken from what the province produces.</summary>
	public IReadOnlyDictionary<string, float> Consumes { get; init; }
	public Occupation JobOccupation { get; init; }
	/// <summary>Population units employed per level.</summary>
	public int JobUnits { get; init; }
	/// <summary>Requirements: the province needs any one of each non-empty list.</summary>
	public IReadOnlyList<string> RequiredFeatures { get; init; }
	public IReadOnlyList<ResourceType> RequiredFood { get; init; }
	public IReadOnlyList<ResourceType> RequiredNonRenewable { get; init; }
	public bool GovernmentCanBuild { get; init; }
	public bool PopulationCanBuild { get; init; }
	/// <summary>The population builds (and expands) it by itself with at least this many units of this occupation.</summary>
	public Occupation PopulationBuildOccupation { get; init; }
	public int PopulationBuildMinUnits { get; init; }

	public override string ToString() => Id;
}

/// <summary>All definition files of res://data, loaded once at startup.</summary>
public sealed class Definitions
{
	public Dictionary<string, Religion> Religions { get; } = new();
	public Dictionary<string, LanguageFamily> LanguageFamilies { get; } = new();
	public Dictionary<string, Language> Languages { get; } = new();
	public Dictionary<string, Culture> Cultures { get; } = new();
	public Dictionary<string, Occupation> Occupations { get; } = new();
	public Dictionary<string, ResourceType> Resources { get; } = new();
	/// <summary>In file order, which is also the order they are listed and produced in.</summary>
	public List<BuildingType> Buildings { get; } = new();

	public BuildingType GetBuilding(string id) => Buildings.Find(b => b.Id == id);
}
