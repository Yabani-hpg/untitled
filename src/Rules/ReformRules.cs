using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Government reforms, Paradox style: reform progress accrues each month and, tier by tier, buys a
/// choice of reform; each enacted reform bends the realm's rules. A change of government law loses
/// the reforms it no longer fits.
/// </summary>
public static class ReformRules
{
	public const double BaseProgress = 2;

	/// <summary>Progress needed for the next tier.</summary>
	public static double Cost(int tierIndex) => 60 + 40 * (tierIndex + 1);

	/// <summary>Progress a month: the base, more with a steady realm and a legitimate crown.</summary>
	public static double MonthlyProgress(Country c) =>
		BaseProgress + (c.Stability >= 1 ? 1 : 0) + (c.Legitimacy >= 80 ? 1 : 0) + Math.Max(-1, LawRules.Mod(c, "reform_progress"));

	/// <summary>The next tier a reform can be chosen from, or null when every tier is enacted.</summary>
	public static ReformTier NextTier(Country c, Definitions defs) =>
		defs.ReformTiers.FirstOrDefault(t => !t.Reforms.Any(r => c.Reforms.Contains(r.Id)));

	public static bool Open(Country c, Reform r) =>
		r.RequiresGovernment.Count == 0 || r.RequiresGovernment.Contains(LawRules.OptionId(c, "government") ?? "");

	public static double Mod(Country c, string key, Definitions defs)
	{
		double sum = 0;
		foreach (string id in c.Reforms)
		{
			if (defs.GetReform(id) is Reform r)
				sum += r.Modifiers.GetValueOrDefault(key);
		}
		return sum;
	}
}
