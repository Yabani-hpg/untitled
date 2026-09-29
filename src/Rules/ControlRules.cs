using System;
using System.Collections.Generic;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// How settled countries hold land. A province taken from unsettled peoples (see TribeRules) has
/// separatism, fading over <see cref="YearsToCore"/> years of rule until it becomes a core. Until then
/// it can rise up: rebels who beat the garrison (or find none) throw the country out, and the province
/// is uncontrolled again. Also: manpower, garrisons and the battle rules.
/// </summary>
public static class ControlRules
{
	public const int YearsToCore = 50;

	/// <summary>Fighters per population unit: most nomad men ride to war, fewer settled villagers do.</summary>
	public const double NomadWarriorShare = 0.3;
	public const double SettledWarriorShare = 0.1;

	/// <summary>Regiments (1000 soldiers) a country can raise per population unit of its core provinces.</summary>
	public const double PeasantManpowerShare = 0.03;
	public const double TribesmenManpowerShare = 0.05;
	/// <summary>Months for an empty manpower pool to refill.</summary>
	public const int ManpowerRefillMonths = 120;

	/// <summary>Unsettled warriors (tribes, nomads, rebels, mercenaries) fight this much harder than regular regiments.</summary>
	public const double FierceMultiplier = 1.5;

	/// <summary>Monthly chance of an uprising at full separatism, for nomads matching their garrison in strength.</summary>
	public const double NomadUprisingChance = 0.03;
	/// <summary>Monthly chance of absorbed tribes breaking away at full separatism, if they feel no kinship.</summary>
	public const double TribalRevoltChance = 0.006;

	static double YearsBetween(GameDate from, GameDate to) => (to.Day - from.Day) / 365.2425;

	/// <summary>0 for a core; for newly held land 1, fading to 0 over <see cref="YearsToCore"/> years.</summary>
	public static double Separatism(Province p, GameDate today)
	{
		if (p.Control == null || p.Control.Kind == ControlKind.Core)
			return 0;
		return Math.Clamp(1 - YearsBetween(p.Control.Since, today) / YearsToCore, 0, 1);
	}

	/// <summary>Whole years left until the province becomes a core.</summary>
	public static int YearsUntilCore(Province p, GameDate today) =>
		p.Control == null || p.IsCore ? 0 : (int)Math.Ceiling(YearsToCore - YearsBetween(p.Control.Since, today));

	/// <summary>Regiments the province's people can put in the field against a country.</summary>
	public static int Warriors(Province p)
	{
		double w = 0;
		foreach (PopGroup pop in p.Pops)
			w += pop.Units * (pop.Occupation.Nomadic ? NomadWarriorShare : SettledWarriorShare);
		return p.TotalUnits > 0 ? Math.Max(1, (int)Math.Round(w)) : 0;
	}

	/// <summary>Whether the country controls a province next to <paramref name="p"/> (by land, or up a navigable river).</summary>
	public static bool Borders(Province p, string tag)
	{
		foreach (Adjacency link in p.Neighbors)
		{
			if ((link.SharesBorder || link.IsNavigableRiver) && link.To.OwnerTag == tag)
				return true;
		}
		return false;
	}

	// ------------------------------------------------------------------------------------- manpower

	/// <summary>The most regiments the country can have, from the people of its core provinces.</summary>
	public static int MaxManpower(IEnumerable<Province> owned)
	{
		double m = 0;
		foreach (Province p in owned)
		{
			if (!p.IsCore)
				continue;
			foreach (PopGroup pop in p.Pops)
				m += pop.Units * (pop.Occupation.Nomadic ? TribesmenManpowerShare : PeasantManpowerShare);
		}
		return (int)m;
	}

	// ------------------------------------------------------------------------------------- battles

	/// <summary>A side in battle: regular regiments, and fierce unsettled ones (tribal warriors, mercenaries).</summary>
	public readonly record struct Force(int Regular, int Fierce, int DiceModifier)
	{
		public int Regiments => Regular + Fierce;
		public double Strength => Regular + Fierce * FierceMultiplier;
	}

	public readonly record struct BattleResult(bool AttackerWon, int AttackerLosses, int DefenderLosses);

	/// <summary>
	/// A battle, Paradox style: each side rolls a die (0-9) plus its modifier; its power is its strength
	/// times (5 + roll), unsettled warriors counting <see cref="FierceMultiplier"/> times. The stronger side
	/// wins; the loser loses 40-70% of its regiments and the winner a share in proportion to how close it was.
	/// </summary>
	public static BattleResult Battle(Force attacker, Force defender, Random rng)
	{
		if (defender.Regiments <= 0)
			return new BattleResult(true, 0, 0);
		if (attacker.Regiments <= 0)
			return new BattleResult(false, 0, 0);
		int ra = rng.Next(10), rd = rng.Next(10);
		double pa = attacker.Strength * Math.Max(1, 5 + ra + attacker.DiceModifier);
		double pd = defender.Strength * Math.Max(1, 5 + rd + defender.DiceModifier);
		bool attackerWon = pa > pd;
		double loserShare = 0.4 + 0.3 * rng.NextDouble();
		double winnerShare = 0.25 * Math.Min(pa, pd) / Math.Max(pa, pd);
		int aLoss = (int)Math.Round(attacker.Regiments * (attackerWon ? winnerShare : loserShare));
		int dLoss = (int)Math.Round(defender.Regiments * (attackerWon ? loserShare : winnerShare));
		return new BattleResult(attackerWon, Math.Min(aLoss, attacker.Regiments), Math.Min(dLoss, defender.Regiments));
	}

	/// <summary>Chance the attacker wins, over all 100 die rolls.</summary>
	public static double WinChance(Force attacker, Force defender)
	{
		if (defender.Regiments <= 0)
			return 1;
		int wins = 0;
		for (int ra = 0; ra < 10; ra++)
		{
			for (int rd = 0; rd < 10; rd++)
			{
				if (attacker.Strength * Math.Max(1, 5 + ra + attacker.DiceModifier) > defender.Strength * Math.Max(1, 5 + rd + defender.DiceModifier))
					wins++;
			}
		}
		return wins / 100.0;
	}

	/// <summary>Dice modifier of people defending their own land: hills and mountains, and deserts or steppe they know.</summary>
	public static int HomeGroundModifier(Province p) =>
		(p.HasFeature("mountains") ? 2 : p.HasFeature("hills") ? 1 : 0) + (p.HasFeature("desert") || p.HasFeature("steppe") ? 1 : 0);

	/// <summary>An army's modifier marching into <paramref name="p"/> from the best of the country's bordering provinces (a river crossing costs 1).</summary>
	public static int AttackerModifier(Province p, string tag)
	{
		int best = int.MinValue;
		foreach (Adjacency link in p.Neighbors)
		{
			if (link.To.OwnerTag == tag && (link.SharesBorder || link.IsNavigableRiver))
				best = Math.Max(best, CombatRules.AttackerDiceModifier(link.To, p));
		}
		return best == int.MinValue ? 0 : best;
	}

	/// <summary>Moves regiments between the manpower pool and a province's garrison.</summary>
	public static bool SetGarrison(Country c, Province p, int garrison, out string reason)
	{
		reason = null;
		if (p?.OwnerTag != c?.Tag || p.Control == null)
			reason = "Not your province";
		else if (garrison < 0)
			reason = "A garrison can't be negative";
		else if (garrison - p.Control.Garrison > c.Manpower)
			reason = $"You have only {c.Manpower} regiments to spare";
		if (reason != null)
			return false;
		c.Manpower -= garrison - p.Control.Garrison;
		p.Control.Garrison = garrison;
		return true;
	}

	/// <summary>Removes fallen warriors from a province's people: a unit of people per regiment, nomads first.</summary>
	public static void KillWarriors(Province p, int regiments)
	{
		// each fallen regiment is a unit of people, taken from the warriors' groups (nomads first)
		int left = regiments;
		foreach (bool nomadic in new[] { true, false })
		{
			foreach (PopGroup pop in p.Pops)
			{
				if (left <= 0 || pop.Occupation.Nomadic != nomadic)
					continue;
				int k = Math.Min(left, pop.Units);
				pop.Units -= k;
				left -= k;
			}
		}
		p.Pops.RemoveAll(g => g.Units <= 0);
	}

	// ----------------------------------------------------------------------------------- monthly step

	/// <summary>Monthly chance that a held province rises up.</summary>
	public static double UprisingChance(Province p, Country owner, GameDate today)
	{
		double separatism = Separatism(p, today);
		if (separatism <= 0)
			return 0;
		if (p.Control.Kind == ControlKind.Subjugated)
		{
			// nomads rise when they feel strong against the garrison holding them down
			double odds = Warriors(p) / (p.Control.Garrison + 0.5);
			return separatism * NomadUprisingChance * Math.Min(odds, 2.0);
		}
		// absorbed tribes break away the less kinship they feel with their overlord
		return separatism * TribalRevoltChance * (1 - 0.6 * PopulationRules.Affinity(owner?.Ruler?.Culture, p.MainCulture));
	}

	public enum EventKind { Cored, UprisingCrushed, ProvinceLost }

	/// <param name="Held">How the country held the province (for a lost province: before it was lost).</param>
	public readonly record struct ControlEvent(EventKind Kind, Province Province, string Tag, BattleResult Battle, ControlKind Held = ControlKind.Core);

	/// <summary>
	/// One month: provinces held for <see cref="YearsToCore"/> years become cores (their garrisons go
	/// home), others may rise up, and manpower refills.
	/// </summary>
	public static List<ControlEvent> MonthlyStep(IReadOnlyList<Province> provinces, IReadOnlyDictionary<string, Country> countries, GameDate today, Random rng)
	{
		var events = new List<ControlEvent>();
		foreach (Province p in provinces)
		{
			if (p == null || p.Control == null || p.IsCore || !countries.TryGetValue(p.OwnerTag, out Country owner))
				continue;
			if (YearsBetween(p.Control.Since, today) >= YearsToCore)
			{
				owner.Manpower += p.Control.Garrison;
				p.Control = new ProvinceControl { Kind = ControlKind.Core, Since = p.Control.Since };
				events.Add(new ControlEvent(EventKind.Cored, p, owner.Tag, default));
				continue;
			}
			if (rng.NextDouble() >= UprisingChance(p, owner, today))
				continue;

			// the uprising: the rebels (fierce, on home ground) attack the garrison; its fort gives it +1
			BattleResult r = Battle(new Force(0, Warriors(p), HomeGroundModifier(p)), new Force(p.Control.Garrison, 0, 1), rng);
			ControlKind held = p.Control.Kind;
			if (r.AttackerWon)
			{
				p.OwnerTag = null;
				p.Control = null;
				events.Add(new ControlEvent(EventKind.ProvinceLost, p, owner.Tag, r, held));
			}
			else
			{
				p.Control.Garrison -= r.DefenderLosses;
				KillWarriors(p, r.AttackerLosses);
				events.Add(new ControlEvent(EventKind.UprisingCrushed, p, owner.Tag, r, held));
			}
		}

		var owned = new Dictionary<string, List<Province>>();
		foreach (Province p in provinces)
		{
			if (p?.OwnerTag == null)
				continue;
			if (!owned.TryGetValue(p.OwnerTag, out List<Province> list))
				owned[p.OwnerTag] = list = new List<Province>();
			list.Add(p);
		}
		foreach (var (tag, list) in owned)
		{
			Country c = countries[tag];
			int max = MaxManpower(list);
			c.MaxManpower = max;
			if (c.Manpower < max)
				c.Manpower = Math.Min(max, c.Manpower + Math.Max(1, max / ManpowerRefillMonths));
		}
		return events;
	}
}
