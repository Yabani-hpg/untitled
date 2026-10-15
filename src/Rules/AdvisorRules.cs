using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Advisors, Paradox style: one of each category (admin, diplomatic, military) at court, each giving its
/// kind's modifier times its skill, for a salary that rises with skill. Candidates come and go each year.
/// </summary>
public static class AdvisorRules
{
	public static readonly string[] Categories = { "admin", "diplomatic", "military" };
	public const int MaxSkill = 3, CandidatesPerCategory = 2;

	/// <summary>Gold a month an advisor asks.</summary>
	public static double Salary(Advisor a) => 2 + 3 * a.Skill * a.Skill;

	public static Advisor Hired(Country c, string category) => c.Advisors.FirstOrDefault(a => a.Type.Category == category);

	/// <summary>The sum of a modifier over the advisors at court.</summary>
	public static double Mod(Country c, string key) => c.Advisors.Where(a => a.Type.Modifier == key).Sum(a => a.Type.Value * a.Skill);

	public static double Salaries(Country c) => c.Advisors.Sum(Salary);

	public static string Describe(Advisor a) =>
		$"{a.Type.Name} (skill {a.Skill}): {a.Type.Modifier.Replace('_', ' ')} {(a.Type.Modifier is "diplomacy_opinion" ? (a.Type.Value * a.Skill).ToString("+0;-0") : (a.Type.Value * a.Skill).ToString("+0%;-0%"))}";
}
