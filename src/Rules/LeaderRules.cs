using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Leaders and military tradition. Tradition (0..100) is won in battle and fades with the years; it makes the
/// armies fight better and the generals recruited abler. A country may keep a few leaders, more the larger it is.
/// </summary>
public static class LeaderRules
{
	public const double RecruitCost = 50;
	/// <summary>Leaders a country may keep: this, plus one for every ten provinces.</summary>
	public const int BaseLeaders = 2, ProvincesPerLeader = 10;
	/// <summary>Tradition won by the winner and loser of a battle, and lost each year.</summary>
	public const double TraditionForVictory = 3, TraditionForDefeat = 0.5, TraditionDecayPerYear = 1;
	/// <summary>At full tradition the armies (or fleets) are this much stronger in attack and defence.</summary>
	public const double TraditionBonus = 0.1;
	/// <summary>Each siege pip adds this to an army's siege power; each manoeuvre pip spares this share of attrition.</summary>
	public const double SiegePerPip = 0.2, AttritionPerPip = 0.1;

	public static int MaxLeaders(Country c, int provinces) => BaseLeaders + provinces / ProvincesPerLeader;

	public static double Tradition(Country c, LeaderKind kind) => kind == LeaderKind.Admiral ? c.NavalTradition : c.ArmyTradition;

	/// <summary>Modifiers from tradition: the land armies' attack and defence.</summary>
	public static double Mod(Country c, string key) => c == null ? 0 : key switch
	{
		"army_attack" or "army_defense" => TraditionBonus * c.ArmyTradition / 100,
		_ => 0,
	};

	/// <summary>Rolls a new leader's pips: a few, more with tradition, spread over the four skills.</summary>
	public static (int Fire, int Shock, int Manoeuvre, int Siege) RollPips(double tradition, Random rng)
	{
		int budget = 2 + (int)(tradition / 12) + rng.Next(3);
		int[] pips = new int[4];
		for (int i = 0; i < budget; i++)
		{
			int k = rng.Next(4);
			if (pips[k] < 6)
				pips[k]++;
		}
		return (pips[0], pips[1], pips[2], pips[3]);
	}

	/// <summary>Why a leader can't be recruited, or null.</summary>
	public static string RecruitObstacle(Country c, int provinces, LeaderKind kind)
	{
		if (c == null)
			return "No country";
		if (c.Leaders.Count >= MaxLeaders(c, provinces))
			return $"We may keep {MaxLeaders(c, provinces)} leaders; more with more provinces";
		if (c.Gold < RecruitCost)
			return $"Costs {RecruitCost:0} gold; we have {c.Gold:0}";
		return null;
	}

	/// <summary>Why a leader can't lead an army, or null: generals lead armies and admirals fleets.</summary>
	public static string AssignObstacle(Leader l, Army a)
	{
		if (l == null || a == null)
			return "No such leader or army";
		if (l.OwnerTag != a.OwnerTag)
			return "Not our leader";
		if (a.IsFleet != (l.Kind == LeaderKind.Admiral))
			return l.Kind == LeaderKind.Admiral ? "An admiral leads fleets" : "A general leads armies";
		return null;
	}

	/// <summary>The best leader among a side's armies in a battle, by battle pips.</summary>
	public static Leader Best(IEnumerable<Leader> leaders) => leaders.Where(l => l != null).OrderByDescending(l => l.BattlePips).FirstOrDefault();

	/// <summary>Yearly chance a leader dies, by age.</summary>
	public static double DeathChance(int age) => age < 50 ? 0.005 : age < 65 ? 0.03 : 0.12;

	public static double Decay(double tradition) => Math.Max(0, tradition - TraditionDecayPerYear / 12);
}
