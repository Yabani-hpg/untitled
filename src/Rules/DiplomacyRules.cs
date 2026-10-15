using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Relations between settled countries. Each has an opinion of every other (-200..200): a base from
/// kinship of culture and faith, prestige and borders, which it drifts toward, plus standing modifiers
/// that fade (an insult, a war, land taken from a neighbour), plus the work of diplomats. Opinion decides
/// who accepts an alliance, who answers a call to arms, and who swallows a peace.
/// </summary>
public static class DiplomacyRules
{
	public const int MinOpinion = -200, MaxOpinion = 200;
	/// <summary>Opinion gained a month by a diplomat improving relations.</summary>
	public const int ImproveRelationsPerMonth = 2;
	/// <summary>Opinion drifts toward its base by this much a month.</summary>
	public const int DriftPerMonth = 1;
	/// <summary>Opinion a country must have of us to accept an alliance.</summary>
	public const int AllianceOpinion = 30;
	/// <summary>Years a truce lasts after a peace.</summary>
	public const int TruceYears = 5;
	/// <summary>Years a wrong is remembered.</summary>
	public const int MemoryYears = 20;
	/// <summary>Rivals: how many a country may name, what it costs them in opinion, who qualifies.</summary>
	public const int MaxRivals = 3, RivalOpinion = -50, RivalReach = 12;
	public const double RivalMinStrength = 0.4, RivalMaxStrength = 2.5, EndRivalryPrestige = 10;
	/// <summary>Aggressive expansion: felt per unit of people in land taken, halved with a claim, fading each year; capped.</summary>
	public const double AggressionPerUnit = 1.0, ClaimAggressionShare = 0.5, AggressionDecayPerYear = 4, MaxAggression = 200;
	/// <summary>How far (in map steps) from the land taken a country must be to feel it, when not a neighbour of those concerned.</summary>
	public const int AggressionReach = 6;
	/// <summary>Coalitions: aggression needed to join; members needed and strength (against the aggressor's side) to strike.</summary>
	public const double CoalitionThreshold = 50, CoalitionStrikeRatio = 0.8;
	public const int CoalitionMinMembers = 2;
	/// <summary>Fabricated claims: progress a month toward 100, and what the owner thinks of it.</summary>
	public const double FabricationPerMonth = 5;
	public const int FabricationOpinion = -15, GuaranteeOpinion = 10, MarriageOpinion = 15, MarriageOpinionNeeded = 0, MarriageYears = 25;

	/// <summary>Where a country's opinion of another settles with nothing else going on.</summary>
	public static int BaseOpinion(Country of, Country other, bool neighbours)
	{
		double affinity = PopulationRules.Affinity(of.Ruler?.Culture, other.Ruler?.Culture);
		int o = (int)(40 * affinity) - 10;
		if (ReligionRules.Of(of) != null && ReligionRules.Of(other) != null)
			o += ReligionRules.Of(of) == ReligionRules.Of(other) ? 10 : -10;   // the same gods, or strange ones
		if (neighbours)
			o -= 15;                       // neighbours have borders to quarrel over
		o += (int)(other.Prestige / 5);
		o += (int)LawRules.Mod(of, "diplomacy_opinion");
		return Math.Clamp(o, MinOpinion, MaxOpinion);
	}

	public static int Opinion(Country of, string tag) => of.Opinions.TryGetValue(tag, out int o) ? o : 0;

	/// <summary>The standing modifiers' sum, dropping those that have faded.</summary>
	public static int Modifiers(Country of, string tag, GameDate today)
	{
		if (!of.OpinionModifiers.TryGetValue(tag, out List<OpinionModifier> list))
			return 0;
		list.RemoveAll(m => m.Until <= today);
		return list.Sum(m => m.Amount);
	}

	public static void AddModifier(Country of, string tag, string reason, int amount, GameDate today, int years = MemoryYears)
	{
		if (!of.OpinionModifiers.TryGetValue(tag, out List<OpinionModifier> list))
			of.OpinionModifiers[tag] = list = new List<OpinionModifier>();
		// the same wrong twice replaces the old memory
		list.RemoveAll(m => m.Reason == reason);
		list.Add(new OpinionModifier(reason, amount, today.AddDays((long)(years * 365.2425))));
		of.Opinions[tag] = Math.Clamp(Opinion(of, tag) + amount, MinOpinion, MaxOpinion);
	}

	/// <summary>A month: opinion drifts toward base plus modifiers, faster with a diplomat at work.</summary>
	public static void MonthlyStep(Country of, Country other, bool neighbours, GameDate today)
	{
		int target = Math.Clamp(BaseOpinion(of, other, neighbours) + Modifiers(of, other.Tag, today) - (int)Math.Round(of.Aggression.GetValueOrDefault(other.Tag)), MinOpinion, MaxOpinion);
		int o = Opinion(of, other.Tag);
		if (other.ImprovingCountryRelations.Contains(of.Tag))
			o += ImproveRelationsPerMonth;
		if (of.Allies.Contains(other.Tag))
			target = Math.Max(target, AllianceOpinion + 20);
		if (o < target)
			o = Math.Min(target, o + DriftPerMonth);
		else if (o > target)
			o = Math.Max(target, o - DriftPerMonth);
		of.Opinions[other.Tag] = Math.Clamp(o, MinOpinion, MaxOpinion);
	}

	/// <summary>Whether two countries share a land border (or a navigable river).</summary>
	public static bool Neighbours(string a, string b, IReadOnlyList<Province> provinces)
	{
		foreach (Province p in provinces)
		{
			if (p?.OwnerTag != a)
				continue;
			foreach (Adjacency link in p.Neighbors)
			{
				if ((link.SharesBorder || link.IsNavigableRiver) && link.To.OwnerTag == b)
					return true;
			}
		}
		return false;
	}

	public static bool HasTruce(Country a, string tag, GameDate today) => a.Truces.TryGetValue(tag, out GameDate until) && until > today;

	// ------------------------------------------------------------------------------------ alliances

	/// <summary>Why an alliance can't be proposed, or null.</summary>
	public static string AllianceObstacle(Country us, Country them, IEnumerable<War> wars)
	{
		if (us.Allies.Contains(them.Tag))
			return "Already our ally";
		if (us.FollowsOverlord || them.FollowsOverlord)
			return "Vassals make no alliances of their own";
		if (wars.Any(w => w.AreEnemies(us.Tag, them.Tag)))
			return "We are at war with them";
		if (wars.Any(w => w.Includes(them.Tag) && us.Allies.Any(a => w.AreEnemies(a, them.Tag))))
			return "They are at war with our ally";
		return null;
	}

	/// <summary>Whether a country run by the game accepts an alliance: it must like us, and we must be worth it.</summary>
	public static bool AcceptsAlliance(Country them, Country us, double ourStrength, double theirStrength)
	{
		int o = Opinion(them, us.Tag);
		if (o < AllianceOpinion)
			return false;
		// a great power gains little from a tiny ally, unless it is very fond of it
		return ourStrength >= theirStrength * 0.25 || o >= 80;
	}

	/// <summary>Whether an ally answers a call to arms (the game's countries): opinion, and not already fighting the other side.</summary>
	public static bool AnswersCall(Country ally, Country caller, War war, IEnumerable<War> wars, GameDate today)
	{
		if (war.Includes(ally.Tag))
			return false;
		string enemy = war.IsAttacker(caller.Tag) ? war.DefenderTag : war.AttackerTag;
		if (ally.Allies.Contains(enemy) || HasTruce(ally, enemy, today))
			return false;
		return Opinion(ally, caller.Tag) >= 0;
	}
}
