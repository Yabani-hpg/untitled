using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Research. A settled country's people produce research points of three kinds (military, admin,
/// science) each month: its educated class (scribes, priests) most of them, artisans, peasants and
/// soldiers under arms a little. Points of a kind flow into the technology the country researches in it,
/// once it meets that technology's requirements; with nothing to research, they pile up in a stockpile,
/// without limit, and pour into the next technology chosen. At war (with levies in the field), military
/// research runs faster.
/// </summary>
public static class TechRules
{
	/// <summary>Military research points a month for each regiment under arms: soldiers learn their trade.</summary>
	public const double SoldierResearch = 0.02;
	/// <summary>Military research at war (with an army in the field) is this much faster.</summary>
	public const double WarMilitaryBoost = 0.5;

	public static TechCategory[] Categories { get; } = Enum.GetValues<TechCategory>();

	/// <summary>The definitions techs are looked up in (set when the game loads).</summary>
	public static Definitions Defs { get; set; }

	public static bool Knows(Country c, Tech t) => t == null || c != null && c.Techs.Contains(t.Id);

	/// <summary>The sum of a modifier over the technologies the country knows.</summary>
	public static double Mod(Country c, string key)
	{
		if (c == null || Defs == null)
			return 0;
		double sum = 0;
		foreach (string id in c.Techs)
		{
			if (Defs.GetTech(id) is Tech t)
				sum += t.Modifiers.GetValueOrDefault(key);
		}
		return sum;
	}

	/// <summary>A country at war: for now, one with an army in the field (its levies are called up for war).</summary>
	public static bool AtWar(Country c, IEnumerable<Army> armies) => armies.Any(a => a.OwnerTag == c.Tag && a.Regiments.Count > 0);

	/// <summary>One source of a month's research points, for the tooltips.</summary>
	public readonly record struct Source(string Name, double Units, Dictionary<TechCategory, double> Points);

	/// <summary>
	/// A month's research points of each kind, and where they come from: the occupations of the country's
	/// people, its soldiers, times its research modifiers, and the war boost to military research.
	/// </summary>
	public static (Dictionary<TechCategory, double> Points, List<Source> Sources) MonthlyPoints(Country c, IEnumerable<Province> owned, IEnumerable<Army> armies)
	{
		var sources = new Dictionary<Occupation, (double Units, Dictionary<TechCategory, double> Points)>();
		foreach (Province p in owned)
		{
			foreach (PopGroup pop in p.Pops)
			{
				if (pop.Occupation.Research.Count == 0)
					continue;
				if (!sources.TryGetValue(pop.Occupation, out var s))
					sources[pop.Occupation] = s = (0, Categories.ToDictionary(k => k, _ => 0.0));
				foreach (var (cat, perUnit) in pop.Occupation.Research)
					s.Points[cat] += pop.Units * perUnit;
				sources[pop.Occupation] = (s.Units + pop.Units, s.Points);
			}
		}
		var list = sources.OrderByDescending(kv => kv.Value.Points.Values.Sum())
			.Select(kv => new Source(kv.Key.Name, kv.Value.Units, kv.Value.Points)).ToList();
		var armyList = armies.Where(a => a.OwnerTag == c.Tag).ToList();
		int soldiers = armyList.Sum(a => a.Regiments.Count) + owned.Sum(p => p.Control?.Garrison.Count ?? 0);
		if (soldiers > 0)
			list.Add(new Source("Soldiers under arms", soldiers, Categories.ToDictionary(k => k, k => k == TechCategory.Military ? soldiers * SoldierResearch : 0)));

		double bonus = 1 + Math.Max(-0.9, LawRules.Mod(c, "research"));
		bool war = AtWar(c, armyList);
		var points = Categories.ToDictionary(k => k,
			k => list.Sum(s => s.Points[k]) * bonus * (k == TechCategory.Military && war ? 1 + WarMilitaryBoost : 1));
		return (points, list);
	}

	/// <summary>Whether research points can flow into the technology: not known yet, its requirements met.</summary>
	public static bool CanResearch(Country c, Tech t, IWorld world, out string reason)
	{
		reason = null;
		if (Knows(c, t))
			reason = "Already known";
		else if (t.Requires.FirstOrDefault(r => !Knows(c, r)) is Tech missing)
			reason = $"Requires {string.Join(", ", t.Requires.Where(r => !Knows(c, r)).Select(r => r.Name))}";
		else if (t.Condition != null && !t.Condition.Holds(world, c))
			reason = $"Requires: {t.ConditionText ?? "conditions we don't meet"}";
		return reason == null;
	}

	public static double Progress(Country c, Tech t) => c.ResearchProgress.GetValueOrDefault(t.Id);

	public static Tech Current(Country c, TechCategory cat) =>
		c.Researching.TryGetValue(cat, out string id) ? Defs?.GetTech(id) : null;

	/// <summary>
	/// Research of a kind now flows into <paramref name="t"/>; the stockpile of that kind pours into it at
	/// once (what it doesn't need stays). <paramref name="learned"/>: the stockpile was enough to finish it.
	/// </summary>
	public static bool Select(Country c, Tech t, IWorld world, out bool learned, out string reason)
	{
		learned = false;
		if (!CanResearch(c, t, world, out reason))
			return false;
		c.Researching[t.Category] = t.Id;
		double stock = c.ResearchStockpile.GetValueOrDefault(t.Category);
		double used = Math.Min(stock, t.Cost - Progress(c, t));
		c.ResearchStockpile[t.Category] = stock - used;
		c.ResearchProgress[t.Id] = Progress(c, t) + used;
		if (Progress(c, t) >= t.Cost - 1e-6)
		{
			Learn(c, t);
			learned = true;
		}
		return true;
	}

	/// <summary>The country knows the technology now.</summary>
	public static void Learn(Country c, Tech t)
	{
		c.Techs.Add(t.Id);
		c.ResearchProgress.Remove(t.Id);
		if (c.Researching.TryGetValue(t.Category, out string id) && id == t.Id)
			c.Researching.Remove(t.Category);
	}

	/// <summary>The technologies a country could put points into now, cheapest first.</summary>
	public static List<Tech> Available(Country c, IWorld world, TechCategory? cat = null) =>
		Defs.Techs.Where(t => (cat == null || t.Category == cat) && CanResearch(c, t, world, out _)).OrderBy(t => t.Cost - Progress(c, t)).ToList();

	/// <summary>
	/// One month for a country: its points of each kind flow into its research, or into the stockpile if
	/// it researches nothing it can. Countries run by the game pick the cheapest technology.
	/// Returns the technologies it discovered.
	/// </summary>
	public static List<Tech> MonthlyStep(Country c, IReadOnlyCollection<Province> owned, IEnumerable<Army> armies, IWorld world, bool player)
	{
		var learned = new List<Tech>();
		var (points, _) = MonthlyPoints(c, owned, armies);
		foreach (TechCategory cat in Categories)
		{
			c.LastResearch[cat] = points[cat];
			Tech t = Current(c, cat);
			if (t != null && !CanResearch(c, t, world, out _))
			{
				c.Researching.Remove(cat);        // it no longer qualifies (or is known): points go to the stockpile
				t = null;
			}
			if (t == null && !player && Available(c, world, cat).FirstOrDefault() is Tech pick)
			{
				c.Researching[cat] = pick.Id;
				t = pick;
			}
			if (t == null)
			{
				c.ResearchStockpile[cat] = c.ResearchStockpile.GetValueOrDefault(cat) + points[cat];
				continue;
			}
			// a month's points; any beyond what it needs are kept in the stockpile
			double need = t.Cost - Progress(c, t);
			c.ResearchProgress[t.Id] = Progress(c, t) + Math.Min(points[cat], need);
			if (points[cat] >= need)
			{
				c.ResearchStockpile[cat] = c.ResearchStockpile.GetValueOrDefault(cat) + points[cat] - need;
				Learn(c, t);
				learned.Add(t);
			}
		}
		return learned;
	}

	/// <summary>Months until the technology is known at this month's pace, or null if no points come.</summary>
	public static int? MonthsLeft(Country c, Tech t)
	{
		double rate = c.LastResearch.GetValueOrDefault(t.Category);
		return rate <= 0 ? null : (int)Math.Ceiling((t.Cost - Progress(c, t)) / rate);
	}

	/// <summary>What knowing a technology opens: unit types and law options that require it.</summary>
	public static List<string> Unlocks(Tech t, Definitions defs)
	{
		var list = defs.UnitTypes.Where(u => u.Tech == t).Select(u => $"{u.Name} ({u.Category.ToString().ToLowerInvariant()})").ToList();
		foreach (LawDefinition law in defs.Laws)
			list.AddRange(law.Options.Where(o => o.Techs.Contains(t.Id)).Select(o => $"{law.Name}: {o.Name}"));
		return list;
	}
}
