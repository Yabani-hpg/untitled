using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Research. A settled country's people produce research points each month: its educated class
/// (scribes, priests) most of them, artisans, peasants and soldiers under arms a little. All technologies,
/// military, admin and science alike, draw on the same points, so the country must choose what comes
/// first: it keeps a queue of technologies in order of priority, and the points flow into the first one
/// whose requirements are met. With nothing to research, they pile up in a stockpile, without limit, and
/// pour into the next technology that can take them. At war (with levies in the field), research into
/// military technology runs faster.
/// </summary>
public static class TechRules
{
	/// <summary>Research points a month for each regiment under arms: soldiers learn their trade.</summary>
	public const double SoldierResearch = 0.02;
	/// <summary>At war (with an army in the field), research into a military technology is this much faster.</summary>
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
	public readonly record struct Source(string Name, double Units, double Points);

	/// <summary>
	/// A month's research points and where they come from: the occupations of the country's people and
	/// its soldiers, times its research modifiers. (The war boost applies only to military technology.)
	/// </summary>
	public static (double Points, List<Source> Sources) MonthlyPoints(Country c, IEnumerable<Province> owned, IEnumerable<Army> armies)
	{
		var units = new Dictionary<Occupation, double>();
		foreach (Province p in owned)
		{
			foreach (PopGroup pop in p.Pops)
			{
				if (pop.Occupation.Research > 0)
					units[pop.Occupation] = units.GetValueOrDefault(pop.Occupation) + pop.Units;
			}
		}
		var sources = units.Select(kv => new Source(kv.Key.Name, kv.Value, kv.Value * kv.Key.Research))
			.OrderByDescending(s => s.Points).ToList();
		int soldiers = armies.Where(a => a.OwnerTag == c.Tag).Sum(a => a.Regiments.Count) + owned.Sum(p => p.Control?.Garrison.Count ?? 0);
		if (soldiers > 0)
			sources.Add(new Source("Soldiers under arms", soldiers, soldiers * SoldierResearch));
		double bonus = 1 + Math.Max(-0.9, LawRules.Mod(c, "research"));
		return (sources.Sum(s => s.Points) * bonus, sources);
	}

	/// <summary>How fast points flow into a technology: military technology faster at war.</summary>
	public static double Rate(Country c, Tech t, double points, bool atWar) =>
		points * (t.Category == TechCategory.Military && atWar ? 1 + WarMilitaryBoost : 1);

	/// <summary>Whether research points can flow into the technology: not known yet, its requirements met.</summary>
	public static bool CanResearch(Country c, Tech t, IWorld world, out string reason)
	{
		reason = null;
		if (Knows(c, t))
			reason = "Already known";
		else if (t.Requires.Any(r => !Knows(c, r)))
			reason = $"Requires {string.Join(", ", t.Requires.Where(r => !Knows(c, r)).Select(r => r.Name))}";
		else if (t.Condition != null && !t.Condition.Holds(world, c))
			reason = $"Requires: {t.ConditionText ?? "conditions we don't meet"}";
		return reason == null;
	}

	public static double Progress(Country c, Tech t) => c.ResearchProgress.GetValueOrDefault(t.Id);

	/// <summary>The technology research points flow into now: the first in the queue that can take them.</summary>
	public static Tech Current(Country c, IWorld world) =>
		c.ResearchQueue.Select(id => Defs?.GetTech(id)).FirstOrDefault(t => t != null && CanResearch(c, t, world, out _));

	/// <summary>
	/// Puts a technology in the queue, after the technologies it builds on that are not known or queued
	/// yet (so a far technology can be aimed at). <paramref name="first"/>: at the front, before
	/// everything else. Returns the technologies added.
	/// </summary>
	public static List<Tech> Queue(Country c, Tech t, bool first)
	{
		var chain = new List<Tech>();
		void Add(Tech x)
		{
			if (Knows(c, x) || chain.Contains(x))
				return;
			x.Requires.ForEach(Add);
			chain.Add(x);
		}
		Add(t);
		if (first)
		{
			c.ResearchQueue.RemoveAll(id => chain.Exists(x => x.Id == id));
			c.ResearchQueue.InsertRange(0, chain.Select(x => x.Id));
			return chain;
		}
		var added = chain.Where(x => !c.ResearchQueue.Contains(x.Id)).ToList();
		c.ResearchQueue.AddRange(added.Select(x => x.Id));
		return added;
	}

	/// <summary>Takes a technology out of the queue, with those queued that build on it. Its progress is kept.</summary>
	public static void Dequeue(Country c, Tech t)
	{
		var gone = new HashSet<string> { t.Id };
		bool more = true;
		while (more)
		{
			more = false;
			foreach (string id in c.ResearchQueue)
			{
				if (!gone.Contains(id) && Defs.GetTech(id)?.Requires.Any(r => gone.Contains(r.Id)) == true)
					more = gone.Add(id);
			}
		}
		c.ResearchQueue.RemoveAll(gone.Contains);
	}

	/// <summary>The country knows the technology now.</summary>
	public static void Learn(Country c, Tech t)
	{
		c.Techs.Add(t.Id);
		c.ResearchProgress.Remove(t.Id);
		c.ResearchQueue.Remove(t.Id);
	}

	/// <summary>The technologies a country could put points into now, cheapest first.</summary>
	public static List<Tech> Available(Country c, IWorld world) =>
		Defs.Techs.Where(t => CanResearch(c, t, world, out _)).OrderBy(t => t.Cost - Progress(c, t)).ToList();

	/// <summary>
	/// Spends points on the queue: they flow into the first technology that can take them; one finished,
	/// what is left flows on to the next. Points nothing can take go to the stockpile. Returns the
	/// technologies discovered.
	/// </summary>
	public static List<Tech> Spend(Country c, double points, bool atWar, IWorld world)
	{
		var learned = new List<Tech>();
		while (points > 1e-9 && Current(c, world) is Tech t)
		{
			// a military technology at war takes in points faster: work out how many of ours it needs
			double factor = Rate(c, t, 1, atWar);
			double need = (t.Cost - Progress(c, t)) / factor;
			if (points < need)
			{
				c.ResearchProgress[t.Id] = Progress(c, t) + points * factor;
				return learned;
			}
			points -= need;
			Learn(c, t);
			learned.Add(t);
		}
		c.ResearchStockpile += points;
		return learned;
	}

	/// <summary>
	/// One month for a country: its points, and its stockpile if it has one, flow into its queue.
	/// Countries run by the game queue the cheapest technology when theirs is empty.
	/// </summary>
	public static List<Tech> MonthlyStep(Country c, IReadOnlyCollection<Province> owned, IEnumerable<Army> armies, IWorld world, bool player)
	{
		var armyList = armies.ToList();
		var (points, _) = MonthlyPoints(c, owned, armyList);
		c.LastResearch = points;
		if (!player && Current(c, world) == null && Available(c, world).FirstOrDefault() is Tech pick)
			Queue(c, pick, first: false);
		double stock = c.ResearchStockpile;
		c.ResearchStockpile = 0;
		return Spend(c, points + stock, AtWar(c, armyList), world);
	}

	/// <summary>Months until the technology is known at last month's pace, if all points went to it; null if none come.</summary>
	public static int? MonthsLeft(Country c, Tech t, bool atWar)
	{
		double rate = Rate(c, t, c.LastResearch, atWar);
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
