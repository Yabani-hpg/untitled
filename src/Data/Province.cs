using Godot;

namespace Untitled.Data;

/// <summary>A land or sea province. Static fields come from data/provinces.csv; the rest is live game state.</summary>
public sealed class Province
{
	public int Id { get; }
	public Color MapColor { get; }
	public string Name { get; }
	public string Terrain { get; }

	/// <summary>Tag of the owning country, or null when unowned.</summary>
	public string OwnerTag { get; set; }

	public bool IsSea => Terrain == "sea";

	public Province(int id, Color mapColor, string name, string terrain, string ownerTag)
	{
		Id = id;
		MapColor = mapColor;
		Name = name;
		Terrain = terrain;
		OwnerTag = ownerTag;
	}
}
