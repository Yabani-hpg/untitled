using Untitled.Data;

namespace Untitled.Rules;

public enum UnitDomain
{
	/// <summary>Armies: move between land provinces that share a border, rivers or not.</summary>
	Land,
	/// <summary>Small ships: seas, lakes, and navigable rivers (including the land provinces along them).</summary>
	RiverShip,
	/// <summary>Seagoing ships: seas only. They can't enter lakes or rivers.</summary>
	SeaShip,
}

/// <summary>Which single steps between adjacent provinces a unit may take. Pathfinding builds on this.</summary>
public static class MovementRules
{
	public static bool CanMove(UnitDomain domain, Adjacency link) => domain switch
	{
		UnitDomain.Land => link.SharesBorder && !link.From.IsWater && !link.To.IsWater,
		UnitDomain.SeaShip => link.SharesBorder && link.From.IsSea && link.To.IsSea,
		UnitDomain.RiverShip => link.IsNavigableRiver || (link.SharesBorder && link.From.IsWater && link.To.IsWater),
		_ => false,
	};

	/// <summary>Whether a unit of this domain can be in the province at all.</summary>
	public static bool CanOccupy(UnitDomain domain, Province province) => domain switch
	{
		UnitDomain.Land => !province.IsWater,
		UnitDomain.SeaShip => province.IsSea,
		UnitDomain.RiverShip => province.IsWater || HasNavigableRiver(province),
		_ => false,
	};

	public static bool HasNavigableRiver(Province province)
	{
		foreach (Adjacency link in province.Neighbors)
		{
			if (link.IsNavigableRiver)
				return true;
		}
		return false;
	}
}
