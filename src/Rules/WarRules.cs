using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// War between settled countries, Paradox style. Armies of enemies meeting in a province fight; an army
/// standing unopposed in an enemy province besieges it and in time occupies it, taking its tax and
/// levies from the owner. War score (-100..100 from the attacker's side) comes from land occupied,
/// weighed by its people, and from battles; a peace cashes it in for provinces or gold.
/// </summary>
public static class WarRules
{
	/// <summary>Siege days (at a siege power of 1 a day) to occupy a province with no garrison.</summary>
	public const double SiegeDays = 60;
	/// <summary>Days more for each regiment of garrison.</summary>
	public const double SiegeDaysPerGarrison = 30;
	/// <summary>Score a won battle is worth, scaled by how big a share of the enemy's men it cost him.</summary>
	public const double BattleScoreMax = 15;
	/// <summary>Score for a side's occupied land, at all of the enemy's land occupied.</summary>
	public const double OccupationScoreMax = 70;
	/// <summary>Holding the enemy's capital is worth this much more.</summary>
	public const double CapitalScore = 10;
	/// <summary>Score cost to take a province: its share of the loser's people, times this.</summary>
	public const double ProvinceCostScale = 100;
	/// <summary>A claimed province costs this much of a province's price.</summary>
	public const double ClaimDiscount = 0.6;
	/// <summary>Score cost of making the loser a vassal: this, plus this much again for every province it has.</summary>
	public const double VassalizeCostBase = 30, VassalizeCostPerProvince = 8;
	/// <summary>Share of a vassal's tax paid to its overlord each month, and a tributary's.</summary>
	public const double TributeShare = 0.25, TributaryShare = 0.15;
	/// <summary>Score cost of making the loser a tributary: this, plus this much again for every province it has.</summary>
	public const double TributaryCostBase = 15, TributaryCostPerProvince = 4;
	/// <summary>Years a vassal must have been one before it can be annexed.</summary>
	public const int YearsBeforeAnnex = 10;
	/// <summary>Score cost of gold: one point for this much.</summary>
	public const double GoldPerScorePoint = 10;
	/// <summary>Score cost of humbling the loser, and the prestige that passes from it to the winner.</summary>
	public const double HumiliateCost = 10, HumiliatePrestige = 25;
	/// <summary>Score costs of the other terms, and how long reparations and ceded trade power last.</summary>
	public const double AnnulCost = 10, RevokeClaimsCost = 5, ReparationsCost = 10, TradePowerCost = 10, ReleaseVassalsCost = 15;
	public const int TreatyYears = 10;
	public const double ReparationsShare = 0.1, CededTradeShare = 0.5;
	/// <summary>Holding the war goal ticks the score up by this a month, to a limit; the defender keeping it unoccupied ticks it down after a year.</summary>
	public const double TickingPerMonth = 1, TickingMax = 25, TickingDownPerMonth = 0.5, TickingMin = -15;
	/// <summary>Months of war before a losing country (run by the game) will consider peace.</summary>
	public const int MonthsBeforePeace = 12;
	/// <summary>Opinion lost with the neighbours of a country that takes land without a claim, per province.</summary>
	public const int AggressionOpinion = -15;
	/// <summary>Prestige for winning or losing a war.</summary>
	public const double PrestigeForVictory = 15;

	// ---------------------------------------------------------------------------------- strength

	/// <summary>A country's weight in war: its regiments under arms plus the levies it can still raise.</summary>
	public static double Strength(Country c, IReadOnlyList<Province> provinces, IEnumerable<Army> armies, GameDate today)
	{
		var owned = provinces.Where(p => p?.OwnerTag == c.Tag).ToList();
		return MilitaryRules.UnderArms(c, armies, owned) + MilitaryRules.AvailableLevies(c, owned, armies, today);
	}

	/// <summary>A side's strength: the war leader and all its allies.</summary>
	public static double SideStrength(IEnumerable<string> tags, IReadOnlyDictionary<string, Country> countries, IReadOnlyList<Province> provinces, IEnumerable<Army> armies, GameDate today) =>
		tags.Sum(t => countries.TryGetValue(t, out Country c) ? Strength(c, provinces, armies, today) : 0);

	// ---------------------------------------------------------------------------------- war score

	/// <summary>What a province is worth in a war: its people (a unit at least), its capital twice.</summary>
	public static double Value(Province p, Country owner) => Math.Max(1, p.TotalUnits) * (owner?.CapitalId == p.Id ? 2 : 1);

	/// <summary>The war score of a side's occupations: the share of the enemy side's value it holds, times the maximum.</summary>
	public static double OccupationScore(War war, int side, IReadOnlyDictionary<string, Country> countries, IReadOnlyList<Province> provinces)
	{
		var enemy = side > 0 ? war.Defenders : war.Attackers;
		var ours = side > 0 ? war.Attackers : war.Defenders;
		double total = 0, held = 0, capital = 0;
		foreach (Province p in provinces)
		{
			if (p?.OwnerTag == null || !enemy.Contains(p.OwnerTag))
				continue;
			countries.TryGetValue(p.OwnerTag, out Country owner);
			double v = Value(p, owner);
			total += v;
			if (p.OccupierTag != null && ours.Contains(p.OccupierTag))
			{
				held += v;
				if (owner?.CapitalId == p.Id && owner.Tag == (side > 0 ? war.DefenderTag : war.AttackerTag))
					capital = CapitalScore;
			}
		}
		return total <= 0 ? 0 : held / total * OccupationScoreMax + capital;
	}

	/// <summary>Recomputes the war's score: occupations on both sides, and the battles.</summary>
	public static double UpdateScore(War war, IReadOnlyDictionary<string, Country> countries, IReadOnlyList<Province> provinces)
	{
		double s = OccupationScore(war, 1, countries, provinces) - OccupationScore(war, -1, countries, provinces) + war.BattleScore + war.TickingScore;
		war.Score = Math.Clamp(s, -100, 100);
		return war.Score;
	}

	/// <summary>Score from a battle: for the winner, by the share of the loser's men that fell.</summary>
	public static double BattleScoreOf(int loserMen, int loserLost, int loserTotalMen) =>
		loserTotalMen <= 0 ? 0 : BattleScoreMax * Math.Min(1, (double)loserLost / Math.Max(1000, loserTotalMen));

	// ---------------------------------------------------------------------------------- sieges

	/// <summary>Siege days needed to occupy a province.</summary>
	public static double SiegeNeeded(Province p) => (SiegeDays + SiegeDaysPerGarrison * (p.Control?.Garrison.Count ?? 0)) * FortRules.SiegeFactor(p);

	/// <summary>An army's siege power a day: 1, plus its engines, less for mountains.</summary>
	public static double SiegePower(IEnumerable<Army> armies, Province p)
	{
		double engines = 0;
		foreach (Army a in armies)
			engines += a.Regiments.Where(r => r.Type.Category == UnitCategory.Siege).Sum(r => r.Type.Siege * r.Strength);
		return (1 + engines * 0.5) * TerrainRules.CategoryFactor(UnitCategory.Siege, p);
	}

	// ---------------------------------------------------------------------------------- peace

	/// <summary>Score cost of a set of peace terms demanded of <paramref name="loser"/>'s side.</summary>
	public static double TermsCost(PeaceTerms terms, Country winner, IEnumerable<string> loserSide, IReadOnlyDictionary<string, Country> countries, IReadOnlyList<Province> provinces)
	{
		double loserValue = provinces.Where(p => p?.OwnerTag != null && loserSide.Contains(p.OwnerTag)).Sum(p => Value(p, countries.GetValueOrDefault(p.OwnerTag)));
		double cost = terms.Gold / GoldPerScorePoint;
		if (terms.Humiliate)
			cost += HumiliateCost;
		if (terms.AnnulAlliances) cost += AnnulCost;
		if (terms.RevokeClaims) cost += RevokeClaimsCost;
		if (terms.Reparations) cost += ReparationsCost;
		if (terms.TradePower) cost += TradePowerCost;
		if (terms.ReleaseVassals) cost += ReleaseVassalsCost;
		if (terms.Vassalize)
			cost += VassalizeCostBase + VassalizeCostPerProvince * provinces.Count(p => p?.OwnerTag != null && loserSide.Contains(p.OwnerTag));
		if (terms.Tributary)
			cost += TributaryCostBase + TributaryCostPerProvince * provinces.Count(p => p?.OwnerTag != null && loserSide.Contains(p.OwnerTag));
		foreach (int id in terms.Provinces)
		{
			Province p = provinces[id];
			double c = Value(p, countries.GetValueOrDefault(p.OwnerTag)) / Math.Max(1, loserValue) * ProvinceCostScale;
			if (winner.Claims.Contains(id))
				c *= ClaimDiscount;
			cost += Math.Max(2, c);
		}
		return cost;
	}

	/// <summary>Provinces of the losing side that may be demanded: those the winner's side occupies, or borders when the war is going well.</summary>
	public static List<Province> Demandable(War war, int winnerSide, IReadOnlyList<Province> provinces)
	{
		var losers = winnerSide > 0 ? war.Defenders : war.Attackers;
		var winners = winnerSide > 0 ? war.Attackers : war.Defenders;
		return provinces.Where(p => p?.OwnerTag != null && losers.Contains(p.OwnerTag)
			&& (p.OccupierTag != null && winners.Contains(p.OccupierTag) || p.Neighbors.Any(l => l.To.OwnerTag != null && winners.Contains(l.To.OwnerTag)))).ToList();
	}

	/// <summary>
	/// Whether a country run by the game accepts a peace: the terms must cost no more than the war score
	/// against it, and it wants out the longer it has been losing. A white peace is taken when the war is even.
	/// </summary>
	public static bool AcceptsPeace(War war, int offererSide, double cost, GameDate today)
	{
		double score = war.Score * offererSide;             // from the offerer's side
		int months = (int)((today.Day - war.Started.Day) / 30.44);
		if (cost <= 0)
			return score >= -10 || months >= MonthsBeforePeace * 2;
		double willing = score + (months >= MonthsBeforePeace ? 5 : 0);
		return cost <= willing;
	}
}
