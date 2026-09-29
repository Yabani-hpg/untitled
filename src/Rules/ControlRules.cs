using System;
using System.Collections.Generic;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// How organized countries take and keep land from the tribes and nomads living outside them.
///
/// Every province starts uncontrolled. A country bordering it can bring it under control:
/// - settled tribes: ally with them (they may refuse), and after some years of alliance make them vassals;
/// - nomads: subjugate them with an army, which then stays as the province's garrison.
/// A controlled province has separatism, fading over <see cref="YearsToCore"/> years of rule until it
/// becomes a core. Until then it can rise up: nomads who beat their garrison (or find none) throw the
/// country out, and the province is uncontrolled again.
/// </summary>
public static class ControlRules
{
	public const int YearsToCore = 50;
	public const int YearsAlliedToVassalize = 5;
	public const int YearsBeforeAskingAgain = 2;

	/// <summary>Fighters per population unit: most nomad men ride to war, fewer settled villagers do.</summary>
	public const double NomadWarriorShare = 0.3;
	public const double SettledWarriorShare = 0.1;

	/// <summary>Regiments (1000 soldiers) a country can raise per population unit of its core provinces.</summary>
	public const double PeasantManpowerShare = 0.03;
	public const double TribesmenManpowerShare = 0.05;
	/// <summary>Months for an empty manpower pool to refill.</summary>
	public const int ManpowerRefillMonths = 120;

	/// <summary>Monthly chance of an uprising at full separatism, for nomads matching their garrison in strength.</summary>
	public const double NomadUprisingChance = 0.03;
	/// <summary>Monthly chance of vassal tribes breaking away at full separatism, if they feel no kinship.</summary>
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

	// ------------------------------------------------------------------------------ tribes: alliance

	static bool CheckUncontrolled(Province p, Country c, Inhabitants who, out string reason)
	{
		reason = null;
		if (p == null || p.IsWater)
			reason = "Not a land province";
		else if (p.OwnerTag != null)
			reason = p.OwnerTag == c?.Tag ? "Already yours" : "Another country controls it";
		else if (p.Inhabitants != who)
			reason = who == Inhabitants.Tribes ? "No settled tribes live here" : "No nomads live here";
		else if (!Borders(p, c.Tag))
			reason = "It must border a province you control";
		return reason == null;
	}

	/// <summary>Chance the tribes accept an alliance: kinship with the ruler's people helps.</summary>
	public static double AllianceChance(Country c, Province p) =>
		0.3 + 0.5 * PopulationRules.Affinity(c.Ruler?.Culture, p.MainCulture);

	public static bool CanAlly(Country c, Province p, GameDate today, out string reason)
	{
		if (!CheckUncontrolled(p, c, Inhabitants.Tribes, out reason))
			return false;
		if (p.AlliedTag == c.Tag)
			reason = "Already your allies";
		else if (p.AlliedTag != null)
			reason = "The tribes are allied with another country";
		else if (p.RefusedTag == c.Tag && today < p.RefusedUntil)
			reason = "The tribes refused you; they will listen again in time";
		return reason == null;
	}

	/// <summary>Asks the tribes for an alliance. They accept with <see cref="AllianceChance"/>.</summary>
	public static bool Ally(Country c, Province p, GameDate today, Random rng)
	{
		if (!CanAlly(c, p, today, out _))
			return false;
		if (rng.NextDouble() < AllianceChance(c, p))
		{
			p.AlliedTag = c.Tag;
			p.AlliedSince = today;
			return true;
		}
		p.RefusedTag = c.Tag;
		p.RefusedUntil = today.AddDays((long)(YearsBeforeAskingAgain * 365.2425));
		return false;
	}

	public static bool CanVassalize(Country c, Province p, GameDate today, out string reason)
	{
		reason = null;
		if (p?.AlliedTag != c?.Tag || p?.OwnerTag != null)
			reason = "The tribes must be your allies first";
		else if (YearsBetween(p.AlliedSince, today) < YearsAlliedToVassalize)
			reason = $"They must be your allies for {YearsAlliedToVassalize} years";
		return reason == null;
	}

	public static bool Vassalize(Country c, Province p, GameDate today)
	{
		if (!CanVassalize(c, p, today, out _))
			return false;
		p.OwnerTag = c.Tag;
		p.Control = new ProvinceControl { Kind = ControlKind.Vassal, Since = today };
		p.AlliedTag = null;
		return true;
	}

	// ------------------------------------------------------------------------- nomads: subjugation

	public static bool CanSubjugate(Country c, Province p, int regiments, out string reason)
	{
		if (!CheckUncontrolled(p, c, Inhabitants.Nomads, out reason))
			return false;
		if (regiments < 1)
			reason = "Send at least one regiment";
		else if (regiments > c.Manpower)
			reason = $"You have only {c.Manpower} regiments to spare";
		return reason == null;
	}

	/// <summary>Dice modifiers of the nomads defending their land: hills and mountains, and deserts they know.</summary>
	static int DefenderModifier(Province p) =>
		(p.HasFeature("mountains") ? 2 : p.HasFeature("hills") ? 1 : 0) + (p.HasFeature("desert") || p.HasFeature("steppe") ? 1 : 0);

	/// <summary>The attacker's modifier, marching in from the best of its bordering provinces (a river crossing costs 1).</summary>
	static int AttackerModifier(Province p, string tag)
	{
		int best = int.MinValue;
		foreach (Adjacency link in p.Neighbors)
		{
			if (link.To.OwnerTag == tag && (link.SharesBorder || link.IsNavigableRiver))
				best = Math.Max(best, CombatRules.AttackerDiceModifier(link.To, p));
		}
		return best == int.MinValue ? 0 : best;
	}

	public readonly record struct BattleResult(bool AttackerWon, int AttackerLosses, int DefenderLosses);

	/// <summary>
	/// A battle, Paradox style: each side rolls a die (0-9) plus its modifiers; its strength is its
	/// regiments times (5 + roll). The stronger side wins; the loser loses 40-70% of its regiments and the
	/// winner a share in proportion to how close it was.
	/// </summary>
	public static BattleResult Battle(int attackers, int defenders, int attackerMod, int defenderMod, Random rng)
	{
		if (defenders <= 0)
			return new BattleResult(true, 0, 0);
		if (attackers <= 0)
			return new BattleResult(false, 0, 0);
		int ra = rng.Next(10), rd = rng.Next(10);
		double pa = attackers * Math.Max(1, 5 + ra + attackerMod);
		double pd = defenders * Math.Max(1, 5 + rd + defenderMod);
		bool attackerWon = pa > pd;
		double loserShare = 0.4 + 0.3 * rng.NextDouble();
		double winnerShare = 0.25 * Math.Min(pa, pd) / Math.Max(pa, pd);
		int aLoss = (int)Math.Round(attackers * (attackerWon ? winnerShare : loserShare));
		int dLoss = (int)Math.Round(defenders * (attackerWon ? loserShare : winnerShare));
		return new BattleResult(attackerWon, Math.Min(aLoss, attackers), Math.Min(dLoss, defenders));
	}

	/// <summary>Chance the attacker wins, over all 100 die rolls.</summary>
	public static double WinChance(int attackers, int defenders, int attackerMod, int defenderMod)
	{
		if (defenders <= 0)
			return 1;
		int wins = 0;
		for (int ra = 0; ra < 10; ra++)
		{
			for (int rd = 0; rd < 10; rd++)
			{
				if (attackers * Math.Max(1, 5 + ra + attackerMod) > defenders * Math.Max(1, 5 + rd + defenderMod))
					wins++;
			}
		}
		return wins / 100.0;
	}

	public static double SubjugationChance(Country c, Province p, int regiments) =>
		WinChance(regiments, Warriors(p), AttackerModifier(p, c.Tag), DefenderModifier(p));

	/// <summary>
	/// Marches <paramref name="regiments"/> into the nomads' land. Winning brings the province under
	/// control, with the survivors as its garrison; losing sends the survivors home. Nomads who fall are
	/// gone from the province's population.
	/// </summary>
	public static BattleResult Subjugate(Country c, Province p, int regiments, GameDate today, Random rng)
	{
		c.Manpower -= regiments;
		BattleResult r = Battle(regiments, Warriors(p), AttackerModifier(p, c.Tag), DefenderModifier(p), rng);
		KillNomads(p, r.DefenderLosses);
		int survivors = regiments - r.AttackerLosses;
		if (r.AttackerWon)
		{
			p.OwnerTag = c.Tag;
			p.Control = new ProvinceControl { Kind = ControlKind.Subjugated, Since = today, Garrison = survivors };
			p.AlliedTag = null;
		}
		else
			c.Manpower += survivors;
		return r;
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

	static void KillNomads(Province p, int regiments)
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
		// vassal tribes break away the less kinship they feel with their overlord
		return separatism * TribalRevoltChance * (1 - 0.6 * PopulationRules.Affinity(owner?.Ruler?.Culture, p.MainCulture));
	}

	public enum EventKind { Cored, UprisingCrushed, ProvinceLost, AllianceEnded }

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

			// the uprising: the rebels attack the garrison; the fort gives the defenders +1
			BattleResult r = Battle(Warriors(p), p.Control.Garrison, p.HasFeature("desert") || p.HasFeature("steppe") ? 1 : 0, 1, rng);
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
				KillNomads(p, r.AttackerLosses);
				events.Add(new ControlEvent(EventKind.UprisingCrushed, p, owner.Tag, r, held));
			}
		}

		// allies whose country no longer borders them drift away
		foreach (Province p in provinces)
		{
			if (p?.AlliedTag != null && (p.OwnerTag != null || !Borders(p, p.AlliedTag)))
			{
				events.Add(new ControlEvent(EventKind.AllianceEnded, p, p.AlliedTag, default));
				p.AlliedTag = null;
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
