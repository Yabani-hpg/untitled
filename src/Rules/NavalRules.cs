using System.Collections.Generic;
using System.Linq;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Ships and fleets. A fleet is an army of ships: it sails the sea zones (river boats also the navigable
/// rivers) and lies in harbours, the coastal provinces of its country and its friends. Fleets carry armies
/// across the water and land them on any coast; fleets of enemies meeting at sea fight; a fleet off a
/// besieged coast blockades it; ships in a sea zone add trade power to its trade node.
/// </summary>
public static class NavalRules
{
	/// <summary>Siege power gained when the besieged coast is blockaded by the besiegers' ships.</summary>
	public const double BlockadeSiegeBonus = 0.5;

	/// <summary>Whether ships of a domain can lie in the province: a coast on a sea zone for seagoing ships; a coast or a navigable river for river boats.</summary>
	public static bool IsHarbour(Province p, UnitDomain domain)
	{
		if (p == null || p.IsWater)
			return false;
		return domain switch
		{
			UnitDomain.SeaShip => p.Neighbors.Any(l => l.SharesBorder && l.To.IsSea),
			UnitDomain.RiverShip => p.Neighbors.Any(l => l.SharesBorder && l.To.IsWater) || MovementRules.HasNavigableRiver(p),
			_ => false,
		};
	}

	public static bool IsHarbour(Province p) => IsHarbour(p, UnitDomain.SeaShip) || IsHarbour(p, UnitDomain.RiverShip);

	public static bool CanBuild(UnitType t, Province p) => t.IsShip && IsHarbour(p, t.Domain);

	/// <summary>The land provinces along a sea zone.</summary>
	public static IEnumerable<Province> Coast(Province sea) =>
		sea.Neighbors.Where(l => l.SharesBorder && !l.To.IsWater).Select(l => l.To);

	/// <summary>Trade power a fleet's ships bring to the node of the sea they sail.</summary>
	public static double TradePower(Army fleet) => fleet.Regiments.Sum(r => r.Type.TradePower * r.Strength);
}
