using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Diplomacy between settled countries: opinions, diplomats, alliances, truces, claims, and the
// declaring and ending of wars. The fighting itself is in GameState.War.cs.
public partial class GameState
{
	/// <summary>Opinions, alliances, truces or diplomats changed.</summary>
	[Signal]
	public delegate void DiplomacyChangedEventHandler();

	/// <summary>A war began or ended, or its score, sides or occupations changed.</summary>
	[Signal]
	public delegate void WarsChangedEventHandler();

	public IReadOnlyDictionary<int, War> Wars => _wars;

	readonly Dictionary<int, War> _wars = new();
	int _nextWarId = 1;

	/// <summary>The settled countries: those with a capital.</summary>
	public IEnumerable<Country> SettledCountries => _countries.Values.Where(c => c.CapitalId > 0);

	public War GetWar(int id) => id > 0 && _wars.TryGetValue(id, out War w) ? w : null;
	public IEnumerable<War> WarsOf(string tag) => _wars.Values.Where(w => w.Includes(tag));
	public bool AtWar(string tag) => _wars.Values.Any(w => w.Includes(tag));
	public bool AtWarWith(string a, string b) => _wars.Values.Any(w => w.AreEnemies(a, b));
	public War WarBetween(string a, string b) => _wars.Values.FirstOrDefault(w => w.AreEnemies(a, b));
	public bool AreNeighbours(string a, string b) => DiplomacyRules.Neighbours(a, b, _provinces);

	/// <summary>Diplomats a country has free: its total less those courting tribes and countries.</summary>
	public int FreeDiplomats(Country c) => TribeRules.DiplomatsOf(c) - c.ImprovingRelations.Count - c.ImprovingCountryRelations.Count;

	// ------------------------------------------------------------------------------------ set-up

	/// <summary>A new game's diplomacy: opinions at their base, the country files' claims and alliances.</summary>
	void SetUpDiplomacy()
	{
		_wars.Clear();
		_nextWarId = 1;
		var settled = SettledCountries.ToList();
		foreach (Country c in _countries.Values)
		{
			c.Opinions.Clear();
			c.OpinionModifiers.Clear();
			c.Allies.Clear();
			c.Truces.Clear();
			c.Claims.Clear();
			c.ImprovingCountryRelations.Clear();
			c.Prestige = 0;
			foreach (int id in c.Definition?.Claims ?? new())
				c.Claims.Add(id);
		}
		foreach (Country c in settled)
		{
			foreach (Country o in settled)
			{
				if (o != c)
					c.Opinions[o.Tag] = DiplomacyRules.BaseOpinion(c, o, AreNeighbours(c.Tag, o.Tag));
			}
			foreach (string ally in c.Definition?.Allies ?? new())
			{
				if (GetCountry(ally) is Country a && a.CapitalId > 0)
				{
					c.Allies.Add(ally);
					a.Allies.Add(c.Tag);
				}
			}
		}
	}

	// ------------------------------------------------------------------------------------ player actions

	public bool SetImprovingCountryRelations(string tag, bool on)
	{
		Country c = PlayerCountry;
		if (c == null || GetCountry(tag) == null || tag == c.Tag)
			return false;
		if (on && !c.ImprovingCountryRelations.Contains(tag) && FreeDiplomats(c) <= 0)
			return false;
		if (on)
			c.ImprovingCountryRelations.Add(tag);
		else
			c.ImprovingCountryRelations.Remove(tag);
		EmitSignal(SignalName.DiplomacyChanged);
		return true;
	}

	public bool ProposeAlliance(string tag, out string reason)
	{
		Country c = PlayerCountry, them = GetCountry(tag);
		reason = null;
		if (c == null || them == null || them.CapitalId <= 0)
			reason = "No such country";
		else
			reason = DiplomacyRules.AllianceObstacle(c, them, _wars.Values);
		if (reason != null)
			return false;
		double ours = WarRules.Strength(c, _provinces, _armies.Values, Date), theirs = WarRules.Strength(them, _provinces, _armies.Values, Date);
		if (!DiplomacyRules.AcceptsAlliance(them, c, ours, theirs))
		{
			reason = DiplomacyRules.Opinion(them, c.Tag) < DiplomacyRules.AllianceOpinion
				? $"{them.Name} thinks too little of us (opinion {DiplomacyRules.Opinion(them, c.Tag)}, needs {DiplomacyRules.AllianceOpinion})"
				: $"{them.Name} sees no use in so small an ally";
			DiplomacyRules.AddModifier(them, c.Tag, "Refused our alliance", -5, Date, 2);
			Notify($"{them.Name} turns down our alliance: {reason}");
			EmitSignal(SignalName.DiplomacyChanged);
			return false;
		}
		MakeAlliance(c, them);
		Notify($"{them.Name} accepts an alliance with {c.Name}.");
		return true;
	}

	void MakeAlliance(Country a, Country b)
	{
		a.Allies.Add(b.Tag);
		b.Allies.Add(a.Tag);
		DiplomacyRules.AddModifier(a, b.Tag, "Allied", 10, Date, 100);
		DiplomacyRules.AddModifier(b, a.Tag, "Allied", 10, Date, 100);
		EmitSignal(SignalName.DiplomacyChanged);
	}

	public void BreakAlliance(string tag)
	{
		Country c = PlayerCountry, them = GetCountry(tag);
		if (c == null || them == null || !c.Allies.Remove(tag))
			return;
		them.Allies.Remove(c.Tag);
		foreach (Country x in new[] { c, them })
			x.OpinionModifiers.GetValueOrDefault(x == c ? tag : c.Tag)?.RemoveAll(m => m.Reason == "Allied");
		DiplomacyRules.AddModifier(them, c.Tag, "Broke our alliance", -40, Date, 10);
		Notify($"Our alliance with {them.Name} is ended.");
		EmitSignal(SignalName.DiplomacyChanged);
	}

	/// <summary>The casus belli the player has against a country: a claim on its land, revenge for a past war, or none.</summary>
	public CasusBelli BestCasusBelli(Country us, Country them)
	{
		if (us.Claims.Any(id => _provinces[id]?.OwnerTag == them.Tag))
			return CasusBelli.Claim;
		if (us.OpinionModifiers.GetValueOrDefault(them.Tag)?.Any(m => m.Reason is "Attacked us" or "Took our land") == true)
			return CasusBelli.Revenge;
		return CasusBelli.Conquest;
	}

	public string WarObstacle(Country us, Country them)
	{
		if (them == null || them.CapitalId <= 0 || them == us)
			return "No such country";
		if (AtWarWith(us.Tag, them.Tag))
			return "Already at war with them";
		if (us.Allies.Contains(them.Tag))
			return "They are our ally";
		if (DiplomacyRules.HasTruce(us, them.Tag, Date))
			return $"A truce holds until {Definitions.DefaultCalendar.Format(us.Truces[them.Tag])}";
		if (_wars.Values.Any(w => w.Includes(us.Tag) && w.Includes(them.Tag)))
			return "We fight on the same side in a war";
		return null;
	}

	public bool DeclareWar(string tag, out string reason)
	{
		Country c = PlayerCountry, them = GetCountry(tag);
		reason = c == null ? "No country" : WarObstacle(c, them);
		if (reason != null)
			return false;
		StartWar(c, them, BestCasusBelli(c, them));
		return true;
	}

	/// <summary>A war begins: allies are called, opinions fall, levies may be raised.</summary>
	War StartWar(Country attacker, Country defender, CasusBelli cb)
	{
		var war = new War { Id = _nextWarId++, AttackerTag = attacker.Tag, DefenderTag = defender.Tag, CasusBelli = cb, Started = Date };
		war.Attackers.Add(attacker.Tag);
		war.Defenders.Add(defender.Tag);
		_wars[war.Id] = war;
		DiplomacyRules.AddModifier(defender, attacker.Tag, "Attacked us", -60, Date);
		if (cb == CasusBelli.Conquest)
		{
			foreach (Country o in SettledCountries.Where(o => o != attacker && o != defender && AreNeighbours(o.Tag, attacker.Tag)))
				DiplomacyRules.AddModifier(o, attacker.Tag, "Made war without cause", -15, Date, 10);
		}
		// allies answer the call: the defender's first, then the attacker's
		foreach (var (leader, side) in new[] { (defender, war.Defenders), (attacker, war.Attackers) })
		{
			foreach (string allyTag in leader.Allies.ToList())
			{
				Country ally = GetCountry(allyTag);
				if (ally == null || ally.CapitalId <= 0)
					continue;
				bool joins = allyTag == PlayerTag
					? !war.Includes(PlayerTag) && !PlayerCountry.Allies.Contains(side == war.Defenders ? attacker.Tag : defender.Tag)
					: DiplomacyRules.AnswersCall(ally, leader, war, _wars.Values, Date);
				if (!joins)
				{
					if (!war.Includes(allyTag))
						DiplomacyRules.AddModifier(leader, allyTag, "Ignored our call to arms", -30, Date, 10);
					continue;
				}
				side.Add(allyTag);
				if (allyTag == PlayerTag)
					Notify($"Our ally {leader.Name} calls us to war against {(side == war.Defenders ? attacker : defender).Name}, and we answer.");
			}
		}
		string cause = cb switch { CasusBelli.Claim => "to press its claims", CasusBelli.Revenge => "in revenge", _ => "without cause" };
		if (war.Includes(PlayerTag) || AreNeighbours(PlayerTag, attacker.Tag) || AreNeighbours(PlayerTag, defender.Tag))
			Notify($"{attacker.Name} declares war on {defender.Name} {cause}."
				+ (war.Attackers.Count + war.Defenders.Count > 2 ? $" Allies join: {string.Join(", ", war.Attackers.Skip(1).Concat(war.Defenders.Skip(1)).Select(t => GetCountry(t)?.Name))}." : ""));
		EmitSignal(SignalName.WarsChanged);
		EmitSignal(SignalName.DiplomacyChanged);
		return war;
	}

	/// <summary>
	/// The player offers peace in a war: demands, or a white peace, or concessions (negative terms are
	/// what we give). The enemy leader, run by the game, accepts by the war score.
	/// </summary>
	public bool OfferPeace(int warId, PeaceTerms terms, bool weConcede, out string reason)
	{
		Country c = PlayerCountry;
		War war = GetWar(warId);
		reason = null;
		if (c == null || war == null || !war.Includes(c.Tag))
		{
			reason = "We are not in that war";
			return false;
		}
		int ourSide = war.SideOf(c.Tag);
		string leaderTag = ourSide > 0 ? war.AttackerTag : war.DefenderTag;
		if (leaderTag != c.Tag)
		{
			reason = $"Only {GetCountry(leaderTag)?.Name}, who leads our side, can make peace";
			return false;
		}
		int winnerSide = weConcede ? -ourSide : ourSide;
		Country winner = GetCountry(winnerSide > 0 ? war.AttackerTag : war.DefenderTag);
		var losers = winnerSide > 0 ? war.Defenders : war.Attackers;
		double cost = WarRules.TermsCost(terms, winner, losers, _countries, _provinces);
		WarRules.UpdateScore(war, _countries, _provinces);
		bool accepted = weConcede
			? cost >= Math.Max(0, war.Score * -ourSide) * 0.5 || terms.IsWhitePeace && war.Score * -ourSide <= 10
			: WarRules.AcceptsPeace(war, ourSide, cost, Date);
		Country enemyLeader = GetCountry(ourSide > 0 ? war.DefenderTag : war.AttackerTag);
		if (!accepted)
		{
			reason = weConcede ? $"{enemyLeader.Name} wants more than that" : $"{enemyLeader.Name} will not accept: the terms cost {cost:0} war score, we have {war.Score * ourSide:0}";
			Notify($"{enemyLeader.Name} refuses our peace: {reason}");
			return false;
		}
		MakePeace(war, winnerSide, terms);
		return true;
	}

	/// <summary>The war ends: provinces and gold change hands, occupations lift, truces begin, prestige moves.</summary>
	void MakePeace(War war, int winnerSide, PeaceTerms terms)
	{
		Country winner = GetCountry(winnerSide > 0 ? war.AttackerTag : war.DefenderTag);
		Country loser = GetCountry(winnerSide > 0 ? war.DefenderTag : war.AttackerTag);
		var winners = winnerSide > 0 ? war.Attackers : war.Defenders;
		var losers = winnerSide > 0 ? war.Defenders : war.Attackers;
		var taken = new List<string>();
		foreach (int id in terms.Provinces)
		{
			Province p = _provinces[id];
			if (p?.OwnerTag == null || !losers.Contains(p.OwnerTag))
				continue;
			Country from = GetCountry(p.OwnerTag);
			taken.Add(p.Name);
			// the people who levied for the old owner come home to a province that is not theirs any more
			foreach (Regiment r in p.Control?.Garrison ?? new())
				MilitaryRules.SendHome(new[] { r }, _provinces, _rng);
			DiplomacyRules.AddModifier(from, winner.Tag, "Took our land", -40, Date);
			if (!winner.Claims.Contains(id) && war.CasusBelli == CasusBelli.Conquest)
			{
				foreach (Country o in SettledCountries.Where(o => o != winner && AreNeighbours(o.Tag, winner.Tag) || o == from))
					DiplomacyRules.AddModifier(o, winner.Tag, "Seized land from a neighbour", WarRules.AggressionOpinion, Date, 15);
			}
			p.OwnerTag = winner.Tag;
			p.Control = new ProvinceControl { Kind = ControlKind.Conquered, Since = Date };
			winner.Claims.Remove(id);
			if (from.CapitalId == id)
				from.CapitalId = PickCapital(from);
		}
		if (terms.Gold > 0)
		{
			double paid = Math.Min(terms.Gold, Math.Max(0, loser.Gold));
			loser.Gold -= paid;
			winner.Gold += paid;
		}
		foreach (Province p in _provinces)
		{
			if (p != null && (p.OccupierTag != null && (winners.Contains(p.OccupierTag) || losers.Contains(p.OccupierTag)) || p.SiegerTag != null && (winners.Contains(p.SiegerTag) || losers.Contains(p.SiegerTag))))
			{
				p.OccupierTag = null;
				p.SiegerTag = null;
				p.SiegeProgress = 0;
			}
		}
		GameDate truce = Date.AddDays((long)(DiplomacyRules.TruceYears * 365.2425));
		foreach (string a in winners)
			foreach (string b in losers)
			{
				if (GetCountry(a) is Country ca) ca.Truces[b] = truce;
				if (GetCountry(b) is Country cb) cb.Truces[a] = truce;
			}
		if (!terms.IsWhitePeace)
		{
			winner.Prestige = Math.Clamp(winner.Prestige + WarRules.PrestigeForVictory, -100, 100);
			loser.Prestige = Math.Clamp(loser.Prestige - WarRules.PrestigeForVictory, -100, 100);
		}
		// armies left in what is now foreign land go home
		foreach (Army a in _armies.Values.Where(a => winners.Contains(a.OwnerTag) || losers.Contains(a.OwnerTag)).ToList())
		{
			Province at = GetProvince(a.ProvinceId);
			if (at != null && at.OwnerTag != a.OwnerTag && !a.Moving)
				SendArmyHome(a);
		}
		_wars.Remove(war.Id);
		string what = terms.IsWhitePeace ? "a white peace" : string.Join(" and ", new[] {
			taken.Count > 0 ? $"{(taken.Count == 1 ? "the province of " : "the provinces of ")}{string.Join(", ", taken)}" : null,
			terms.Gold > 0 ? $"{terms.Gold:0} gold" : null }.Where(s => s != null));
		if (war.Includes(PlayerTag) || AreNeighbours(PlayerTag, winner.Tag) || AreNeighbours(PlayerTag, loser.Tag))
			Notify(terms.IsWhitePeace ? $"{winner.Name} and {loser.Name} make a white peace." : $"{loser.Name} makes peace with {winner.Name}, giving up {what}.");
		RefreshFlags();
		foreach (Country c in new[] { winner, loser })
		{
			if (c.CapitalId > 0 && GetProvince(c.CapitalId)?.OwnerTag != c.Tag)
				c.CapitalId = PickCapital(c);
		}
		EmitSignal(SignalName.WarsChanged);
		EmitSignal(SignalName.DiplomacyChanged);
		EmitSignal(SignalName.OwnershipChanged);
		EmitSignal(SignalName.ArmiesChanged);
	}

	// ------------------------------------------------------------------------------------ monthly

	/// <summary>Opinions drift, truces lapse, war scores are reckoned.</summary>
	void RunDiplomacyStep()
	{
		var settled = SettledCountries.ToList();
		foreach (Country c in settled)
		{
			foreach (Country o in settled)
			{
				if (o != c)
					DiplomacyRules.MonthlyStep(c, o, AreNeighbours(c.Tag, o.Tag), Date);
			}
			foreach (string t in c.Truces.Where(kv => kv.Value <= Date).Select(kv => kv.Key).ToList())
				c.Truces.Remove(t);
			c.Prestige *= 0.995;
		}
		foreach (War w in _wars.Values)
			WarRules.UpdateScore(w, _countries, _provinces);
		EmitSignal(SignalName.DiplomacyChanged);
		if (_wars.Count > 0)
			EmitSignal(SignalName.WarsChanged);
	}
}
