using System;
using System.Collections.Generic;
using System.Linq;
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

	/// <summary>Fighters per population unit (of both sexes; the men fight): most nomad men ride to war, fewer settled villagers do.</summary>
	public const double NomadWarriorShare = 0.3;
	public const double SettledWarriorShare = 0.1;

	/// <summary>Unsettled warriors (tribes, nomads, rebels, mercenaries) fight this much harder than regular regiments.</summary>
	public const double FierceMultiplier = 1.5;

	/// <summary>Monthly chance of an uprising at full separatism, for nomads matching their garrison in strength.</summary>
	public const double NomadUprisingChance = 0.03;
	/// <summary>Monthly chance of absorbed tribes breaking away at full separatism, if they feel no kinship.</summary>
	public const double TribalRevoltChance = 0.006;

	static double YearsBetween(GameDate from, GameDate to) => (to.Day - from.Day) / 365.2425;

	/// <summary>Years a country must hold land before it is a core: <see cref="YearsToCore"/>, changed by its laws.</summary>
	public static double CoringYears(Country owner) => Math.Max(10, YearsToCore + LawRules.Mod(owner, "years_to_core"));

	/// <summary>0 for a core; for newly held land 1, fading to 0 over the owner's coring years.</summary>
	public static double Separatism(Province p, GameDate today, Country owner)
	{
		if (p.Control == null || p.Control.Kind == ControlKind.Core)
			return 0;
		return Math.Clamp(1 - YearsBetween(p.Control.Since, today) / CoringYears(owner), 0, 1);
	}

	/// <summary>Whole years left until the province becomes a core.</summary>
	public static int YearsUntilCore(Province p, GameDate today, Country owner) =>
		p.Control == null || p.IsCore ? 0 : (int)Math.Ceiling(CoringYears(owner) - YearsBetween(p.Control.Since, today));

	/// <summary>Regiments the province's people can put in the field against a country: their men.</summary>
	public static int Warriors(Province p)
	{
		double w = 0;
		foreach (PopGroup pop in p.Pops)
		{
			if (pop.IsMale)
				w += pop.Units * 2 * (pop.Occupation.Nomadic ? NomadWarriorShare : SettledWarriorShare);
		}
		return p.UnitsOf(Sex.Male) > 0 ? Math.Max(1, (int)Math.Round(w)) : 0;
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

	// ------------------------------------------------------------------------------------- battles

	/// <summary>
	/// A side in battle: its regiments, their strength (a regiment of spearmen is 1; unsettled warriors
	/// count <see cref="FierceMultiplier"/> times) and how well they keep their men alive.
	/// </summary>
	public readonly record struct Force(int Regiments, double Strength, int DiceModifier, double Defense = 1)
	{
		/// <summary>Regular regiments and fierce unsettled ones (tribal warriors, mercenaries).</summary>
		public static Force Of(int regular, int fierce, int diceModifier) =>
			new(regular + fierce, regular + fierce * FierceMultiplier, diceModifier);

		/// <summary>Two forces fighting as one, with the better dice modifier.</summary>
		public Force Plus(Force other) => new(Regiments + other.Regiments, Strength + other.Strength, Math.Max(DiceModifier, other.DiceModifier),
			Regiments + other.Regiments == 0 ? 1 : (Defense * Regiments + other.Defense * other.Regiments) / (Regiments + other.Regiments));
	}

	/// <param name="AttackerShare">Share of its men the attacker lost (for armies of regiments).</param>
	public readonly record struct BattleResult(bool AttackerWon, int AttackerLosses, int DefenderLosses, double AttackerShare = 0, double DefenderShare = 0);

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
		double aShare = Math.Min(1, (attackerWon ? winnerShare : loserShare) / Math.Max(0.1, attacker.Defense));
		double dShare = Math.Min(1, (attackerWon ? loserShare : winnerShare) / Math.Max(0.1, defender.Defense));
		int aLoss = (int)Math.Round(attacker.Regiments * aShare);
		int dLoss = (int)Math.Round(defender.Regiments * dShare);
		return new BattleResult(attackerWon, Math.Min(aLoss, attacker.Regiments), Math.Min(dLoss, defender.Regiments), aShare, dShare);
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

	/// <summary>Removes fallen warriors from a province's people: a unit of people per regiment, men first, nomads first.</summary>
	public static void KillWarriors(Province p, int regiments)
	{
		// each fallen regiment is a unit of people, taken from the warriors' groups (nomad men first)
		int left = regiments;
		foreach (var (male, nomadic) in new[] { (true, true), (true, false), (false, true), (false, false) })
		{
			foreach (PopGroup pop in p.Pops)
			{
				if (left <= 0 || pop.Occupation.Nomadic != nomadic || pop.IsMale != male)
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
		double separatism = Separatism(p, today, owner);
		if (separatism <= 0)
			return 0;
		double laws = Math.Max(0, 1 + LawRules.Mod(owner, "uprising"));
		if (p.Control.Kind == ControlKind.Subjugated)
		{
			// nomads rise when they feel strong against the garrison holding them down
			double odds = Warriors(p) / (p.Control.Garrison.Sum(r => r.Strength) + 0.5);
			return separatism * NomadUprisingChance * Math.Min(odds, 2.0) * laws;
		}
		// absorbed tribes break away the less kinship they feel with their overlord
		return separatism * TribalRevoltChance * (1 - 0.6 * PopulationRules.Affinity(owner?.Ruler?.Culture, p.MainCulture)) * laws;
	}

	public enum EventKind { Cored, UprisingCrushed, ProvinceLost }

	/// <param name="Held">How the country held the province (for a lost province: before it was lost).</param>
	/// <param name="Regiments">Regiments that fought for the country (garrison and armies), and how many of them were lost.</param>
	public readonly record struct ControlEvent(EventKind Kind, Province Province, string Tag, BattleResult Battle, ControlKind Held = ControlKind.Core,
		int Regiments = 0, int RegimentsLost = 0);

	/// <summary>
	/// One month: provinces held for <see cref="YearsToCore"/> years become cores (their garrisons go
	/// home), others may rise up against their garrison and any of the owner's armies standing there.
	/// </summary>
	public static List<ControlEvent> MonthlyStep(IReadOnlyList<Province> provinces, IReadOnlyDictionary<string, Country> countries,
		IReadOnlyCollection<Army> armies, GameDate today, Random rng)
	{
		var events = new List<ControlEvent>();
		foreach (Province p in provinces)
		{
			if (p == null || p.Control == null || p.IsCore || !countries.TryGetValue(p.OwnerTag, out Country owner))
				continue;
			if (YearsBetween(p.Control.Since, today) >= CoringYears(owner))
			{
				MilitaryRules.SendHome(p.Control.Garrison, provinces, rng);
				p.Control = new ProvinceControl { Kind = ControlKind.Core, Since = p.Control.Since };
				events.Add(new ControlEvent(EventKind.Cored, p, owner.Tag, default));
				continue;
			}
			if (rng.NextDouble() >= UprisingChance(p, owner, today))
				continue;

			// the uprising: the rebels (fierce, on home ground) attack the garrison, its fort giving it +1,
			// and the owner's armies standing in the province
			var here = armies.Where(a => a.OwnerTag == owner.Tag && a.ProvinceId == p.Id && !a.Moving).ToList();
			var defenders = p.Control.Garrison.Concat(here.SelectMany(a => a.Regiments)).ToList();
			BattleResult r = Battle(Force.Of(0, Warriors(p), HomeGroundModifier(p)), MilitaryRules.ForceOf(defenders, p, 1), rng);
			var gone = MilitaryRules.TakeLosses(defenders, r.DefenderShare, r.AttackerWon);
			foreach (Army a in here)
				a.Regiments.RemoveAll(gone.Contains);
			KillWarriors(p, r.AttackerLosses);
			ControlKind held = p.Control.Kind;
			if (r.AttackerWon)
			{
				// the garrison is overrun to the last man; armies that were there fall back, beaten
				int lost = gone.Count + p.Control.Garrison.Count(x => !gone.Contains(x));
				p.OwnerTag = null;
				p.Control = null;
				events.Add(new ControlEvent(EventKind.ProvinceLost, p, owner.Tag, r, held, defenders.Count, lost));
			}
			else
			{
				p.Control.Garrison.RemoveAll(gone.Contains);
				events.Add(new ControlEvent(EventKind.UprisingCrushed, p, owner.Tag, r, held, defenders.Count, gone.Count));
			}
		}
		return events;
	}
}
