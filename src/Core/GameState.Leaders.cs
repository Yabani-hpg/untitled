using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Generals and admirals: recruited, given armies, lost to age; military tradition won and lost.
public partial class GameState
{
	int _nextLeaderId = 1;

	public Leader GetLeader(string tag, int id) => GetCountry(tag)?.Leaders.FirstOrDefault(l => l.Id == id);
	public Leader LeaderOf(Army a) => a == null || a.LeaderId == 0 ? null : GetLeader(a.OwnerTag, a.LeaderId);
	/// <summary>The country's leaders not leading any army.</summary>
	public IEnumerable<Leader> FreeLeaders(Country c) => c.Leaders.Where(l => !_armies.Values.Any(a => a.OwnerTag == c.Tag && a.LeaderId == l.Id));

	public bool RecruitLeader(LeaderKind kind, out string reason)
	{
		Country c = PlayerCountry;
		reason = LeaderRules.RecruitObstacle(c, c == null ? 0 : ProvincesOf(c.Tag).Count(), kind);
		if (reason != null)
			return false;
		Leader l = RecruitLeaderFor(c, kind);
		Notify($"{l.Name} takes up the {(kind == LeaderKind.Admiral ? "admiral's" : "general's")} staff ({l.Pips}).");
		return true;
	}

	Leader RecruitLeaderFor(Country c, LeaderKind kind)
	{
		Province capital = GetProvince(c.CapitalId);
		string cultureId = capital?.Pops.OrderByDescending(g => g.Units).FirstOrDefault()?.Culture?.Id;
		List<string> names = c.RulerNames.Count > 0 ? c.RulerNames
			: cultureId != null && Definitions.RulerNames.TryGetValue(cultureId, out List<string> list) && list.Count > 0 ? list : null;
		var (fire, shock, manoeuvre, siege) = LeaderRules.RollPips(LeaderRules.Tradition(c, kind), _rng);
		int age = 28 + _rng.Next(25);
		var l = new Leader
		{
			Id = _nextLeaderId++,
			Name = names != null ? names[_rng.Next(names.Count)] : $"{c.Adjective} {(kind == LeaderKind.Admiral ? "admiral" : "general")}",
			OwnerTag = c.Tag,
			Kind = kind,
			Fire = fire, Shock = shock, Manoeuvre = manoeuvre, Siege = siege,
			Birth = Date.AddDays(-(long)(age * 365.2425) - _rng.Next(365)),
		};
		c.Gold -= LeaderRules.RecruitCost;
		c.Leaders.Add(l);
		EmitSignal(SignalName.ArmiesChanged);
		return l;
	}

	public void DismissLeader(int leaderId)
	{
		Country c = PlayerCountry;
		if (c == null)
			return;
		RemoveLeader(c, leaderId);
		EmitSignal(SignalName.ArmiesChanged);
	}

	void RemoveLeader(Country c, int leaderId)
	{
		c.Leaders.RemoveAll(l => l.Id == leaderId);
		foreach (Army a in _armies.Values.Where(a => a.OwnerTag == c.Tag && a.LeaderId == leaderId))
			a.LeaderId = 0;
	}

	/// <summary>Gives an army a leader (0: none); a leader leaves any other army it led.</summary>
	public bool AssignLeader(int armyId, int leaderId, out string reason)
	{
		Army a = GetArmy(armyId);
		reason = a == null || a.OwnerTag != PlayerTag ? "Not our army" : null;
		if (reason != null)
			return false;
		if (leaderId == 0)
		{
			a.LeaderId = 0;
			EmitSignal(SignalName.ArmiesChanged);
			return true;
		}
		Leader l = GetLeader(a.OwnerTag, leaderId);
		reason = LeaderRules.AssignObstacle(l, a);
		if (reason != null)
			return false;
		AssignLeaderTo(a, l);
		EmitSignal(SignalName.ArmiesChanged);
		return true;
	}

	void AssignLeaderTo(Army a, Leader l)
	{
		foreach (Army other in _armies.Values.Where(o => o.OwnerTag == a.OwnerTag && o.LeaderId == l.Id))
			other.LeaderId = 0;
		a.LeaderId = l.Id;
	}

	/// <summary>Tradition is won in battle: the winner's more, the loser's a little; naval for sea fights.</summary>
	void AwardTradition(Country winner, Country loser, bool atSea)
	{
		if (atSea)
		{
			winner.NavalTradition = Math.Min(100, winner.NavalTradition + LeaderRules.TraditionForVictory);
			loser.NavalTradition = Math.Min(100, loser.NavalTradition + LeaderRules.TraditionForDefeat);
		}
		else
		{
			winner.ArmyTradition = Math.Min(100, winner.ArmyTradition + LeaderRules.TraditionForVictory);
			loser.ArmyTradition = Math.Min(100, loser.ArmyTradition + LeaderRules.TraditionForDefeat);
		}
	}

	/// <summary>Each month tradition fades a little, and old leaders may die; the game's countries find leaders for their armies at war.</summary>
	void RunLeaderStep()
	{
		bool changed = false;
		foreach (Country c in SettledCountries.ToList())
		{
			c.ArmyTradition = LeaderRules.Decay(c.ArmyTradition);
			c.NavalTradition = LeaderRules.Decay(c.NavalTradition);
			foreach (Leader l in c.Leaders.ToList())
			{
				if (_rng.NextDouble() < LeaderRules.DeathChance(l.AgeOn(Date)) / 12)
				{
					RemoveLeader(c, l.Id);
					changed = true;
					if (c.Tag == PlayerTag)
						Notify($"{l.Name}, our {(l.Kind == LeaderKind.Admiral ? "admiral" : "general")}, has died at {l.AgeOn(Date)}.");
				}
			}
			if (c.Tag != PlayerTag)
				changed |= AiLeaders(c);
		}
		if (changed)
			EmitSignal(SignalName.ArmiesChanged);
	}

	/// <summary>A country run by the game recruits a general for its largest leaderless army at war (an admiral for a fleet), when it can afford one.</summary>
	bool AiLeaders(Country c)
	{
		if (!AtWar(c.Tag))
			return false;
		bool changed = false;
		foreach (Army a in ArmiesOf(c.Tag).Where(a => a.LeaderId == 0).OrderByDescending(a => a.Regiments.Count).ToList())
		{
			LeaderKind kind = a.IsFleet ? LeaderKind.Admiral : LeaderKind.General;
			Leader free = FreeLeaders(c).Where(l => l.Kind == kind).OrderByDescending(l => l.BattlePips).FirstOrDefault();
			if (free == null && LeaderRules.RecruitObstacle(c, ProvincesOf(c.Tag).Count(), kind) == null && c.Gold > LeaderRules.RecruitCost + 50)
				free = RecruitLeaderFor(c, kind);
			if (free == null)
				break;
			AssignLeaderTo(a, free);
			changed = true;
		}
		return changed;
	}
}
