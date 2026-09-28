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
