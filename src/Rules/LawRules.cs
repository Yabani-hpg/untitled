using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// A settled country's laws and code (data/laws.json): what they change, who counts as a citizen, how
/// laws are changed, the slow work of assimilation and conversion, and succession when the ruler dies.
/// </summary>
public static class LawRules
{
	/// <summary>The option in force for a law (its default if the country never set it).</summary>
	public static LawOption Option(Country c, LawDefinition law) =>
		c != null && c.Laws.TryGetValue(law.Id, out string id) ? law.GetOption(id) ?? law.Default : law.Default;

	static IEnumerable<LawOption> InForce(Country c, Definitions defs) => defs.Laws.Select(l => Option(c, l));

	/// <summary>The sum of a modifier over the country's laws (0 for no country).</summary>
	public static double Mod(Country c, string key)
	{
		if (c == null || Defs == null)
			return 0;
		double sum = 0;
		foreach (LawOption o in InForce(c, Defs))
			sum += o.Modifiers.GetValueOrDefault(key);
		return sum;
	}

	public static double Effect(Country c, string key) =>
		c == null || Defs == null ? 0 : InForce(c, Defs).Sum(o => o.Effects.GetValueOrDefault(key));

	public static string Choice(Country c, string key) =>
		c == null || Defs == null ? null : InForce(c, Defs).Select(o => o.Choices.GetValueOrDefault(key)).FirstOrDefault(v => v != null);

	/// <summary>The definitions the modifiers are looked up in (set when the game loads).</summary>
	public static Definitions Defs { get; set; }

	// ------------------------------------------------------------------------------------ citizenship

	/// <summary>Whether a population group are citizens of the country, by its citizenship law.</summary>
	public static bool IsCitizen(Country c, PopGroup pop)
	{
		Culture ruling = c?.Ruler?.Culture;
		return Choice(c, "citizens") switch
		{
			"all" => true,
			"settled" => !pop.Occupation.Nomadic,
			"language_family" => ruling != null && pop.Culture.Language.Family == ruling.Language.Family,
			_ => pop.Culture == ruling,
		};
	}

	// ------------------------------------------------------------------------------------ changing laws

	public static double ChangeCost(Country c, Definitions defs) => defs.LawChangeCost * (1 + Mod(c, "change_cost"));

	public static bool CanChange(Country c, LawDefinition law, LawOption option, IWorld world, Definitions defs, out string reason)
	{
		reason = null;
		GameDate today = world.Date;
		if (Option(c, law) == option)
			reason = "Already our law";
		else if (c.LawChanged.TryGetValue(law.Id, out GameDate last) && (today.Day - last.Day) / 365.2425 < defs.LawChangeCooldownYears)
			reason = $"Changed too recently: {law.Name} can change again in {Math.Ceiling(defs.LawChangeCooldownYears - (today.Day - last.Day) / 365.2425)} years";
		else if (option.Requires != null && !option.Requires.Holds(world, c))
			reason = $"Requires: {option.RequiresText ?? "conditions we don't meet"}";
		else if (c.Gold < ChangeCost(c, defs))
			reason = $"Costs {ChangeCost(c, defs):0} gold; we have {c.Gold:0}";
		return reason == null;
	}

	public static void Change(Country c, LawDefinition law, LawOption option, GameDate today, Definitions defs)
	{
		c.Gold -= ChangeCost(c, defs);
		c.Laws[law.Id] = option.Id;
		c.LawChanged[law.Id] = today;
	}

	// --------------------------------------------------------------------- assimilation and conversion

	/// <summary>
	/// One month of assimilation and forced conversion in the country's core provinces: each year, about
	/// the law's number of units in each province take the ruler's culture (or religion).
	/// Returns the units that changed.
	/// </summary>
	public static int AssimilationStep(Country c, IEnumerable<Province> owned, Random rng)
	{
		double assimilation = Effect(c, "assimilation") / 12, conversion = Effect(c, "conversion") / 12;
		Culture culture = c.Ruler?.Culture;
		Religion religion = c.Ruler?.Religion;
		if (culture == null || assimilation <= 0 && conversion <= 0)
			return 0;
		int changed = 0;
		foreach (Province p in owned)
		{
			if (!p.IsCore)
				continue;
			if (rng.NextDouble() < assimilation)
				changed += Shift(p, g => g.Culture != culture, g => (culture, g.Religion));
			if (religion != null && rng.NextDouble() < conversion)
				changed += Shift(p, g => g.Religion != religion, g => (g.Culture, religion));
		}
		return changed;
	}

	static int Shift(Province p, Func<PopGroup, bool> which, Func<PopGroup, (Culture, Religion)> into)
	{
		PopGroup from = p.Pops.Where(which).OrderByDescending(g => g.Units).FirstOrDefault();
		if (from == null)
			return 0;
		from.Units--;
		var (culture, religion) = into(from);
		p.AddPops(culture, religion, from.Occupation, 1);
		p.Pops.RemoveAll(g => g.Units <= 0);
		return 1;
	}

	// ------------------------------------------------------------------------------------ succession

	/// <summary>Monthly chance a ruler of this age dies.</summary>
	public static double DeathChance(int age) => age switch
	{
		< 30 => 0.0005,
		< 45 => 0.0012,
		< 60 => 0.003,
		< 70 => 0.008,
		_ => 0.02,
	};

	/// <summary>How the next ruler is chosen, in words.</summary>
	public static string SuccessionText(Country c) => Choice(c, "succession") switch
	{
		"brother" => "the ruler's eldest brother",
		"elected" => "a ruler elected by the great men of the realm",
		"chosen_by_priests" => "a ruler named by the priests",
		_ => "the ruler's eldest son",
	};

	/// <summary>
	/// The successor's age, and whether a new dynasty takes the throne: sons are young, brothers older;
	/// elected and god-chosen rulers are grown men and often of another house.
	/// </summary>
	public static (int Age, bool NewDynasty) Successor(Country c, Character old, GameDate today, Random rng)
	{
		int oldAge = old?.AgeOn(today) ?? 40;
		return Choice(c, "succession") switch
		{
			"brother" => (Math.Max(25, oldAge - 2 - rng.Next(8)), false),
			"elected" => (35 + rng.Next(20), rng.NextDouble() < 0.5),
			"chosen_by_priests" => (25 + rng.Next(30), rng.NextDouble() < 0.7),
			_ => (Math.Max(8, oldAge - 20 - rng.Next(12)), false),
		};
	}
}
