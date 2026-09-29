using System.Collections.Generic;
using Godot;

namespace Untitled.Data;

/// <summary>A land or water (sea, lake) province. Static fields come from data/provinces.csv; the rest is live game state.</summary>
public sealed class Province
{
	public int Id { get; }
	public Color MapColor { get; }
	public string Name { get; }
	public string Terrain { get; }

	/// <summary>Tag of the owning country, or null when unowned.</summary>
	public string OwnerTag { get; set; }

	public bool IsSea => Terrain == "sea";
	public bool IsLake => Terrain == "lake";
	public bool IsWater => IsSea || IsLake;

	/// <summary>Borders and river links to other provinces, filled from data/adjacencies.csv.</summary>
	public IReadOnlyList<Adjacency> Neighbors => _neighbors;

	// --- Population and production (data/province_setup.json, then live) ---

	/// <summary>Population groups, at most one per culture, religion and occupation.</summary>
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

	public bool HasFeature(string feature) => Features.Contains(feature);

	public Building GetBuilding(BuildingType type) => Buildings.Find(b => b.Type == type);

	/// <summary>Adds people, merging into the group of the same kind if there is one.</summary>
	public void AddPops(Culture culture, Religion religion, Occupation occupation, int units)
	{
		if (units <= 0)
			return;
		foreach (PopGroup pop in Pops)
		{
			if (pop.Culture == culture && pop.Religion == religion && pop.Occupation == occupation)
			{
				pop.Units += units;
				return;
			}
		}
		Pops.Add(new PopGroup(culture, religion, occupation, units));
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
		Name = name;
		Terrain = terrain;
		OwnerTag = ownerTag;
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
