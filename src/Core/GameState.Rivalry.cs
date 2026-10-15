using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Rivals, aggressive expansion and coalitions, Paradox style.
public partial class GameState
{
	// ------------------------------------------------------------------------------------ rivals

	/// <summary>Why a country can't name another its rival, or null.</summary>
	public string RivalObstacle(Country us, Country them)
	{
		if (them == null || them == us || them.CapitalId <= 0)
			return "No such country";
		if (us.Rivals.Contains(them.Tag))
			return "Already our rival";
		if (us.Rivals.Count >= DiplomacyRules.MaxRivals)
			return $"We have {DiplomacyRules.MaxRivals} rivals already";
		if (us.Allies.Contains(them.Tag))
			return "They are our ally";
		if (us.OverlordTag == them.Tag || them.OverlordTag == us.Tag)
			return "Not between a lord and his vassal";
		if (us.FollowsOverlord)
			return "A vassal has its overlord's rivals";
		double ours = WarRules.Strength(us, _provinces, _armies.Values, Date), theirs = WarRules.Strength(them, _provinces, _armies.Values, Date);
		if (theirs < ours * DiplomacyRules.RivalMinStrength)
			return "Too weak to be worth our rivalry";
		if (theirs > ours * DiplomacyRules.RivalMaxStrength)
			return "Too strong: it would be a rivalry in name only";
		if (!AreNeighbours(us.Tag, them.Tag) && Steps(GetProvince(us.CapitalId), GetProvince(them.CapitalId)) > DiplomacyRules.RivalReach)
			return "Too far away to be our rival";
		return null;
	}

	public bool SetRival(string tag, out string reason)
	{
		Country us = PlayerCountry, them = GetCountry(tag);
		reason = us == null ? "No country" : RivalObstacle(us, them);
		if (reason != null)
			return false;
		SetRivalFor(us, them);
		return true;
	}

	void SetRivalFor(Country us, Country them)
	{
		us.Rivals.Add(them.Tag);
		DiplomacyRules.AddModifier(them, us.Tag, "Rivalry", DiplomacyRules.RivalOpinion, Date, 25);
		DiplomacyRules.AddModifier(us, them.Tag, "Rivalry", DiplomacyRules.RivalOpinion, Date, 25);
		if (them.Tag == PlayerTag)
			Notify($"{us.Name} names us its rival.");
		else if (us.Tag == PlayerTag)
			Notify($"{them.Name} is our rival: their opinion of us falls, and humbling them in war brings prestige.");
		EmitSignal(SignalName.DiplomacyChanged);
	}

	/// <summary>The player drops a rival, at a cost in prestige.</summary>
	public void EndRivalry(string tag)
	{
		Country us = PlayerCountry, them = GetCountry(tag);
		if (us == null || them == null || !us.Rivals.Remove(tag))
			return;
		us.Prestige = Math.Clamp(us.Prestige - DiplomacyRules.EndRivalryPrestige, -100, 100);
		foreach (var (a, b) in new[] { (us, them), (them, us) })
			a.OpinionModifiers.GetValueOrDefault(b.Tag)?.RemoveAll(m => m.Reason == "Rivalry");
		Notify($"We are no longer rivals of {them.Name}.");
		EmitSignal(SignalName.DiplomacyChanged);
	}

	/// <summary>A country run by the game names rivals: those who named it, then the strongest neighbours it can match.</summary>
	void AiRivals(Country c)
	{
		if (c.Rivals.Count >= 2 || _rng.NextDouble() > 0.03)
			return;
		Country pick = SettledCountries.Where(o => o != c && RivalObstacle(c, o) == null)
			.OrderByDescending(o => o.Rivals.Contains(c.Tag) ? 1 : 0)
			.ThenByDescending(o => AreNeighbours(c.Tag, o.Tag) ? 1 : 0)
			.ThenByDescending(o => WarRules.Strength(o, _provinces, _armies.Values, Date))
			.FirstOrDefault();
		if (pick != null)
			SetRivalFor(c, pick);
	}

	// ------------------------------------------------------------------------------------ aggressive expansion

	/// <summary>Land taken in a peace frightens the neighbourhood: each country near the land or its loser remembers it as aggression.</summary>
	void AddAggression(Country aggressor, Province taken, Country from, bool hadClaim, double scale = 1)
	{
		double amount = Math.Max(1, taken.TotalUnits) * DiplomacyRules.AggressionPerUnit * (hadClaim ? DiplomacyRules.ClaimAggressionShare : 1) * scale
			* Math.Max(0.2, 1 + LawRules.Mod(aggressor, "aggression"));
		Province capital = GetProvince(aggressor.CapitalId);
		foreach (Country o in SettledCountries)
		{
			if (o == aggressor)
				continue;
			int steps = Steps(GetProvince(o.CapitalId), taken);
			bool near = o == from || AreNeighbours(o.Tag, aggressor.Tag) || AreNeighbours(o.Tag, from.Tag) || steps <= DiplomacyRules.AggressionReach;
			if (!near)
				continue;
			// the fear fades with distance from the land taken
			double felt = amount * Math.Max(0.3, 1 - 0.5 * steps / DiplomacyRules.AggressionReach);
			if (o == from)
				felt *= 1.5;
			else if (o.Ruler?.Religion != null && o.Ruler.Religion != from.Ruler?.Religion)
				felt *= 0.5;
			o.Aggression[aggressor.Tag] = Math.Min(DiplomacyRules.MaxAggression, o.Aggression.GetValueOrDefault(aggressor.Tag) + felt);
		}
	}

	/// <summary>Aggression a country holds against another, 0 when none.</summary>
	public double AggressionOf(Country c, string aggressorTag) => c.Aggression.GetValueOrDefault(aggressorTag);

	// ------------------------------------------------------------------------------------ coalitions

	/// <summary>Members of the coalition against a country.</summary>
	public IEnumerable<Country> CoalitionAgainst(string tag) => SettledCountries.Where(o => o.CoalitionAgainst == tag);

	/// <summary>Why a country can't join the coalition against another, or null.</summary>
	public string CoalitionObstacle(Country us, Country them)
	{
		if (them == null || them == us || them.CapitalId <= 0)
			return "No such country";
		if (us.CoalitionAgainst == them.Tag)
			return "Already in it";
		if (us.CoalitionAgainst != null)
			return $"We stand in a coalition against {GetCountry(us.CoalitionAgainst)?.Name} already";
		if (AggressionOf(us, them.Tag) < DiplomacyRules.CoalitionThreshold)
			return $"Their aggression must frighten us more ({AggressionOf(us, them.Tag):0} of {DiplomacyRules.CoalitionThreshold})";
		if (us.Allies.Contains(them.Tag) || us.OverlordTag == them.Tag || them.OverlordTag == us.Tag)
			return "Not against an ally, overlord or vassal";
		if (us.FollowsOverlord)
			return "A vassal follows its overlord";
		if (AtWarWith(us.Tag, them.Tag))
			return "We are at war with them already";
		return null;
	}

	public bool JoinCoalition(string tag, out string reason)
	{
		Country us = PlayerCountry, them = GetCountry(tag);
		reason = us == null ? "No country" : CoalitionObstacle(us, them);
		if (reason != null)
			return false;
		us.CoalitionAgainst = tag;
		Notify($"We join the coalition against {them.Name}: {CoalitionAgainst(tag).Count()} members.");
		EmitSignal(SignalName.DiplomacyChanged);
		return true;
	}

	public void LeaveCoalition()
	{
		if (PlayerCountry?.CoalitionAgainst == null)
			return;
		PlayerCountry.CoalitionAgainst = null;
		EmitSignal(SignalName.DiplomacyChanged);
	}

	/// <summary>
	/// Each month: aggression fades; frightened countries join the coalition against an aggressor and leave it
	/// as the fear fades; a coalition that outweighs the aggressor goes to war together.
	/// </summary>
	void RunCoalitionStep()
	{
		var settled = SettledCountries.ToList();
		foreach (Country c in settled)
		{
			foreach (string tag in c.Aggression.Keys.ToList())
			{
				c.Aggression[tag] -= DiplomacyRules.AggressionDecayPerYear / 12.0;
				if (c.Aggression[tag] <= 0 || GetCountry(tag)?.CapitalId <= 0)
					c.Aggression.Remove(tag);
			}
			// the game's countries join and leave by their fear; the player chooses
			if (c.CoalitionAgainst != null && (CoalitionObstacle(c, GetCountry(c.CoalitionAgainst)) is string why && why != "Already in it"
				|| AggressionOf(c, c.CoalitionAgainst) < DiplomacyRules.CoalitionThreshold * 0.6))
			{
				if (c.Tag == PlayerTag)
					Notify($"We leave the coalition against {GetCountry(c.CoalitionAgainst)?.Name}.");
				c.CoalitionAgainst = null;
			}
			if (c.CoalitionAgainst == null && c.Tag != PlayerTag)
			{
				string worst = c.Aggression.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault(t => CoalitionObstacle(c, GetCountry(t)) == null);
				if (worst != null)
				{
					c.CoalitionAgainst = worst;
					if (worst == PlayerTag)
						Notify($"{c.Name} joins a coalition against us: {CoalitionAgainst(worst).Count()} members.");
				}
			}
		}
		// coalitions strike when they outweigh their target
		foreach (Country target in settled)
		{
			var members = CoalitionAgainst(target.Tag).Where(m => !AtWarWith(m.Tag, target.Tag) && !DiplomacyRules.HasTruce(m, target.Tag, Date) && !m.FollowsOverlord).ToList();
			if (members.Count < DiplomacyRules.CoalitionMinMembers || AtWar(target.Tag) && members.Any(m => target.Allies.Contains(m.Tag)))
				continue;
			double theirs = WarRules.SideStrength(members.Select(m => m.Tag), _countries, _provinces, _armies.Values, Date);
			double its = WarRules.SideStrength(target.Allies.Append(target.Tag).Concat(VassalsOf(target.Tag).Select(v => v.Tag)), _countries, _provinces, _armies.Values, Date);
			if (theirs < its * DiplomacyRules.CoalitionStrikeRatio || _rng.NextDouble() > 0.25)
				continue;
			Country leader = members.OrderByDescending(m => WarRules.Strength(m, _provinces, _armies.Values, Date)).First();
			War war = StartWar(leader, target, CasusBelli.Coalition);
			foreach (Country m in members)
			{
				if (!war.Includes(m.Tag))
					war.Attackers.Add(m.Tag);
			}
			if (target.Tag == PlayerTag || members.Any(m => m.Tag == PlayerTag) || AreNeighbours(PlayerTag, target.Tag))
				Notify($"A coalition of {members.Count} led by {leader.Name} declares war on {target.Name} to curb its aggression: {string.Join(", ", members.Select(m => m.Name))}.");
		}
	}
}
