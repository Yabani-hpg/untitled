using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>Institutions: which exist, which a country lacks, what that costs it in research, and how they spread.</summary>
public static class InstitutionRules
{
	public const double Full = 100;

	public static bool Exists(Institution i, GameDate today) => today.Holocene.Year >= i.Year;
	public static bool Has(Country c, Institution i) => c.Institutions.Contains(i.Id);
	public static double Progress(Country c, Institution i) => c.InstitutionProgress.GetValueOrDefault(i.Id);

	public static IEnumerable<Institution> Missing(Country c, Definitions defs, GameDate today) =>
		defs.Institutions.Where(i => Exists(i, today) && !Has(c, i));

	/// <summary>Technology costs this much more for every institution the country lacks.</summary>
	public static double CostFactor(Country c, Definitions defs, GameDate today) =>
		1 + defs.InstitutionPenalty * Missing(c, defs, today).Count();

	public static double EmbraceCost(Country c, Definitions defs, int provinces) =>
		Math.Max(defs.EmbraceCostMin, defs.EmbraceCostPerProvince * provinces);

	/// <summary>Spread a month: a little on its own, more beside countries that have it, more again where its origin technology is known.</summary>
	public static double MonthlySpread(Country c, Institution i, bool neighbourHasIt) =>
		1 + (neighbourHasIt ? 3 : 0) + (i.OriginTech != null && c.Techs.Contains(i.OriginTech.Id) ? 4 : 0);
}
