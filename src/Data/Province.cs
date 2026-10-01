using System.Collections.Generic;
using Godot;
using Untitled.Core;

namespace Untitled.Data;

/// <summary>A land or water (sea, lake) province. Static fields come from data/provinces.csv; the rest is live game state.</summary>
public sealed class Province
{
	public int Id { get; }
	public Color MapColor { get; }
	/// <summary>Its name in data/provinces.csv, which areas, country files and saves refer to.</summary>
	public string BaseName { get; }
	/// <summary>
	/// What its owner calls it: a country file's "province_names" (London is Londinium under Rome), by
	/// owner tag.
	/// </summary>
	public Dictionary<string, string> AlternateNames { get; } = new();
	/// <summary>Its name as shown: the owner's name for it if it has one, else <see cref="BaseName"/>.</summary>
	public string Name => OwnerTag != null && AlternateNames.TryGetValue(OwnerTag, out string name) ? name : BaseName;
	public string Terrain { get; }

	/// <summary>
	/// Tag of the country controlling the province, or null when it is uncontrolled: land of tribes and
	/// nomads that no organized country rules (see <see cref="Inhabitants"/>).
	/// </summary>
	public string OwnerTag { get; set; }

	/// <summary>How the controlling country holds the province; null when uncontrolled.</summary>
	public ProvinceControl Control { get; set; }

	/// <summary>The tribe (unsettled country) whose people live here, or 0. Only uncontrolled land has one.</summary>
	public int TribeId { get; set; }

	public bool IsCore => Control?.Kind == ControlKind.Core;

	/// <summary>Its vegetation (data/terrain.json biomes), from the setup; null for water.</summary>
	public TerrainType Biome { get; set; }
	/// <summary>Its relief (plains, hills, mountains, impassable): <see cref="Terrain"/> as a terrain type; null for water.</summary>
	public TerrainType Relief { get; set; }
	/// <summary>Land armies can enter it (not water, not impassable peaks).</summary>
	public bool IsPassable => !IsWater && Relief?.Passable != false;

	/// <summary>Who lives in the province, which decides how a country can take control of it.</summary>
	public Inhabitants Inhabitants
	{
		get
		{
			int total = 0, nomads = 0;
			foreach (PopGroup pop in Pops)
			{
				total += pop.Units;
				if (pop.Occupation.Nomadic)
					nomads += pop.Units;
			}
			return total == 0 ? Inhabitants.Empty : 2 * nomads >= total ? Inhabitants.Nomads : Inhabitants.Tribes;
		}
	}

	public bool IsSea => Terrain == "sea";
	public bool IsLake => Terrain == "lake";
	public bool IsWater => IsSea || IsLake;

	/// <summary>Borders and river links to other provinces, filled from data/adjacencies.csv.</summary>
	public IReadOnlyList<Adjacency> Neighbors => _neighbors;

	// --- Population and production (data/province_setup.json, then live) ---

	/// <summary>Population groups, at most one per culture, religion, occupation and sex.</summary>
	public List<PopGroup> Pops { get; } = new();
	/// <summary>forest, river, coast, desert, steppe, tundra, mountains, hills.</summary>
	public HashSet<string> Features { get; } = new();
	/// <summary>The non-renewable slot: a deposit in the ground, or null.</summary>
	public ResourceType NonRenewable { get; set; }
	/// <summary>The food slot: what the land is farmed or herded for, or null.</summary>
	public ResourceType Food { get; set; }
	/// <summary>Production buildings, at most one per type.</summary>
	public List<Building> Buildings { get; } = new();

	public int TotalUnits
	{
		get
		{
			int units = 0;
			foreach (PopGroup pop in Pops)
				units += pop.Units;
			return units;
		}
	}

	public int UnitsOf(Occupation occupation)
	{
		int units = 0;
		foreach (PopGroup pop in Pops)
		{
			if (pop.Occupation == occupation)
				units += pop.Units;
		}
		return units;
	}

	public int UnitsOf(Sex sex)
	{
		int units = 0;
		foreach (PopGroup pop in Pops)
		{
			if (pop.Sex == sex)
				units += pop.Units;
		}
		return units;
	}

	public bool HasFeature(string feature) => Features.Contains(feature);

	public Building GetBuilding(BuildingType type) => Buildings.Find(b => b.Type == type);

	/// <summary>Adds people, merging into the group of the same kind if there is one.</summary>
	public void AddPops(Culture culture, Religion religion, Occupation occupation, Sex sex, int units)
	{
		if (units <= 0)
			return;
		foreach (PopGroup pop in Pops)
		{
			if (pop.Culture == culture && pop.Religion == religion && pop.Occupation == occupation && pop.Sex == sex)
			{
				pop.Units += units;
				return;
			}
		}
		Pops.Add(new PopGroup(culture, religion, occupation, sex, units));
	}

	/// <summary>
	/// Adds people of both sexes: half men and half women, an odd unit going to one or the other by
	/// <paramref name="oddIsMale"/>.
	/// </summary>
	public void AddPeople(Culture culture, Religion religion, Occupation occupation, int units, bool oddIsMale)
	{
		int men = units / 2 + (units % 2 == 1 && oddIsMale ? 1 : 0);
		AddPops(culture, religion, occupation, Sex.Male, men);
		AddPops(culture, religion, occupation, Sex.Female, units - men);
	}

	/// <summary>The culture with the most people, or null for an empty province.</summary>
	public Culture MainCulture
	{
		get
		{
			var units = new Dictionary<Culture, int>();
			Culture best = null;
			foreach (PopGroup pop in Pops)
			{
				units[pop.Culture] = units.GetValueOrDefault(pop.Culture) + pop.Units;
				if (best == null || units[pop.Culture] > units[best])
					best = pop.Culture;
			}
			return best;
		}
	}

	readonly List<Adjacency> _neighbors = new();

	public Province(int id, Color mapColor, string name, string terrain, string ownerTag)
	{
		Id = id;
		MapColor = mapColor;
		BaseName = name;
		Terrain = terrain;
		OwnerTag = ownerTag;
		MapOwnerTag = ownerTag;
	}

	/// <summary>The modern country the province belongs to on the map (data/provinces.csv). Not used in play.</summary>
	public string MapOwnerTag { get; }

	/// <summary>Back to the map's state, with no people or production, before a new game's setup is applied.</summary>
	internal void ClearForNewGame()
	{
		OwnerTag = null;                 // every province starts uncontrolled; country files take theirs
		Control = null;
		TribeId = 0;
		Pops.Clear();
		Buildings.Clear();
		Features.Clear();
		NonRenewable = null;
		Food = null;
	}

	internal void AddNeighbor(Adjacency adjacency) => _neighbors.Add(adjacency);

	/// <summary>The link to <paramref name="other"/>, or null if they are not neighbours.</summary>
	public Adjacency GetAdjacency(Province other)
	{
		foreach (Adjacency a in _neighbors)
		{
			if (a.To == other)
				return a;
		}
		return null;
	}
}
