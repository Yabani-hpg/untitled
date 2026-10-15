using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// The estates of the realm, Paradox style. Each has influence (from its people, its privileges and the
/// land the crown has let slip) and loyalty (drifting toward what its privileges earn it). A loyal estate
/// lends the realm its strengths; a disloyal one withholds them and stirs unrest. Together, too much
/// influence is a danger of its own. The crown grants privileges, revokes them, and seizes land.
/// </summary>
public static class EstateRules
{
	public const double LoyalAt = 60, DisloyalBelow = 30, BaseLoyalty = 40, StartLoyalty = 50, StartCrownland = 30;
	public const double RevokeLoyalty = -20, SeizeLoyalty = -15, SeizeCrownland = 10;
	/// <summary>Over this much influence in all, the estates together stir unrest.</summary>
	public const double DangerInfluence = 160;

	public static double Loyalty(Country c, Estate e) => c.EstateLoyalty.TryGetValue(e.Id, out double l) ? l : StartLoyalty;
	public static bool IsLoyal(Country c, Estate e) => Loyalty(c, e) >= LoyalAt;
	public static bool IsDisloyal(Country c, Estate e) => Loyalty(c, e) < DisloyalBelow;
	public static IEnumerable<Privilege> Granted(Country c, Estate e) => e.Privileges.Where(p => c.Privileges.Contains(p.Id));

	/// <summary>An estate's influence: its base, its people's share of the realm, its privileges, and the land the crown does not hold.</summary>
	public static double Influence(Country c, Estate e, IReadOnlyCollection<Province> owned)
	{
		double influence = e.BaseInfluence + Granted(c, e).Sum(p => p.Influence) + Math.Max(0, 50 - c.Crownland) * 0.3;
		if (e.Occupation != null)
		{
			long people = owned.Sum(p => (long)p.TotalUnits), these = owned.Sum(p => (long)p.UnitsOf(e.Occupation));
			if (people > 0)
				influence += e.PerShare * these / people;
		}
		return Math.Clamp(influence, 0, 100);
	}

	/// <summary>Where an estate's loyalty settles: its privileges keep it content; a crown with no land of its own alarms it.</summary>
	public static double Equilibrium(Country c, Estate e) =>
		Math.Clamp(BaseLoyalty + Granted(c, e).Sum(p => p.Loyalty) - (c.Crownland < 20 ? 10 : 0) + (c.Stability < 0 ? 5 * c.Stability : 0), 0, 100);

	/// <summary>A month: loyalty drifts toward its equilibrium.</summary>
	public static void MonthlyStep(Country c, Definitions defs)
	{
		foreach (Estate e in defs.Estates)
		{
			double now = Loyalty(c, e), target = Equilibrium(c, e);
			c.EstateLoyalty[e.Id] = now < target ? Math.Min(target, now + 1) : Math.Max(target, now - 1);
		}
	}

	/// <summary>The sum of a modifier over the estates: granted privileges, loyal and disloyal estates, and crown land.</summary>
	public static double Mod(Country c, string key, Definitions defs)
	{
		double sum = 0;
		foreach (Estate e in defs.Estates)
		{
			sum += Granted(c, e).Sum(p => p.Modifiers.GetValueOrDefault(key));
			if (IsLoyal(c, e))
				sum += e.Loyal.GetValueOrDefault(key);
			else if (IsDisloyal(c, e))
				sum += e.Disloyal.GetValueOrDefault(key);
		}
		if (key == "tax")
			sum += (c.Crownland - StartCrownland) / 200;
		return sum;
	}

	public static string Standing(Country c, Estate e) => IsLoyal(c, e) ? "loyal" : IsDisloyal(c, e) ? "disloyal" : "watchful";
}
