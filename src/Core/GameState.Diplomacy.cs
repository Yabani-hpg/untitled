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

	public IEnumerable<Country> VassalsOf(string tag) => _countries.Values.Where(c => c.OverlordTag == tag && c.CapitalId > 0);
	/// <summary>Tribute a vassal pays its overlord a month.</summary>
	public double TributeOf(Country vassal) => vassal.OverlordTag == null ? 0 : vassal.LastLedger.Tax * WarRules.TributeShare;

	/// <summary>Diplomats a country has free: its total less those courting tribes and countries.</summary>
	public int FreeDiplomats(Country c) => TribeRules.DiplomatsOf(c) - c.ImprovingRelations.Count - c.ImprovingCountryRelations.Count - c.Fabricating.Count;

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
			c.OverlordTag = null;
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
		StateRules.ChangeStability(c, -1);
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
		if (us.OverlordTag != null)
			return $"A vassal of {GetCountry(us.OverlordTag)?.Name} makes no war of its own";
		if (them.OverlordTag == us.Tag)
			return "They are our vassal";
		if (them.OverlordTag != null && them.OverlordTag != us.Tag)
			them = GetCountry(them.OverlordTag);     // war on a vassal is war on its overlord
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
		CasusBelli cb = BestCasusBelli(c, them);
		if (them.OverlordTag != null)
			them = GetCountry(them.OverlordTag);
		StartWar(c, them, cb);
		return true;
	}

	/// <summary>A war begins: allies are called, opinions fall, levies may be raised.</summary>
	War StartWar(Country attacker, Country defender, CasusBelli cb)
	{
		var war = new War { Id = _nextWarId++, AttackerTag = attacker.Tag, DefenderTag = defender.Tag, CasusBelli = cb, Started = Date };
		war.Attackers.Add(attacker.Tag);
		war.Defenders.Add(defender.Tag);
		// the war goal: the claimed land, else the enemy's capital
		war.GoalProvinceId = cb == CasusBelli.Claim ? attacker.Claims.FirstOrDefault(id => _provinces[id]?.OwnerTag == defender.Tag) : 0;
		if (war.GoalProvinceId <= 0)
			war.GoalProvinceId = defender.CapitalId;
		_wars[war.Id] = war;
		DiplomacyRules.AddModifier(defender, attacker.Tag, "Attacked us", -60, Date);
		if (cb == CasusBelli.Conquest)
		{
			StateRules.ChangeStability(attacker, -2);          // war without cause shakes the realm
			attacker.Legitimacy = Math.Max(0, attacker.Legitimacy - 10);
			foreach (Country o in SettledCountries.Where(o => o != attacker && o != defender && AreNeighbours(o.Tag, attacker.Tag)))
				DiplomacyRules.AddModifier(o, attacker.Tag, "Made war without cause", -15, Date, 10);
		}
		// those who guaranteed the defender stand by it
		foreach (Country g in SettledCountries.Where(g => g.Guarantees.Contains(defender.Tag) && g != attacker && !war.Includes(g.Tag) && !g.Allies.Contains(attacker.Tag) && g.OverlordTag == null))
		{
			war.Defenders.Add(g.Tag);
			if (g.Tag == PlayerTag)
				Notify($"We guaranteed {defender.Name}: we are at war with {attacker.Name}.");
		}
		// vassals follow their overlord; allies answer the call: the defender's first, then the attacker's
		foreach (var (leader, side) in new[] { (defender, war.Defenders), (attacker, war.Attackers) })
		{
			foreach (Country v in VassalsOf(leader.Tag))
			{
				if (!war.Includes(v.Tag))
					side.Add(v.Tag);
			}
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
		string cause = cb switch { CasusBelli.Claim => "to press its claims", CasusBelli.Revenge => "in revenge", CasusBelli.Coalition => "at the head of a coalition", _ => "without cause" };
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
			if (war.CasusBelli != CasusBelli.Coalition)
				AddAggression(winner, p, from, winner.Claims.Contains(id));
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
		if (terms.Vassalize && loser.OverlordTag == null && winner.OverlordTag == null)
		{
			loser.OverlordTag = winner.Tag;
			loser.VassalSince = Date;
			foreach (string ally in loser.Allies.ToList())
			{
				loser.Allies.Remove(ally);                // a vassal has its overlord's friends and enemies
				GetCountry(ally)?.Allies.Remove(loser.Tag);
			}
			DiplomacyRules.AddModifier(loser, winner.Tag, "Made us their vassal", -30, Date);
			foreach (Country v in VassalsOf(loser.Tag).ToList())
				v.OverlordTag = null;                      // its own vassals go free
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
		GameDate treatyEnd = Date.AddDays((long)(WarRules.TreatyYears * 365.2425));
		if (terms.AnnulAlliances)
		{
			foreach (string ally in loser.Allies.ToList())
			{
				loser.Allies.Remove(ally);
				GetCountry(ally)?.Allies.Remove(loser.Tag);
			}
			if (loser.Tag == PlayerTag)
				Notify("Our alliances are annulled by the treaty.");
		}
		if (terms.RevokeClaims)
			loser.Claims.RemoveWhere(id => _provinces[id]?.OwnerTag != null && winners.Contains(_provinces[id].OwnerTag));
		if (terms.Reparations)
			loser.Reparations[winner.Tag] = treatyEnd;
		if (terms.TradePower)
			loser.TradePowerCeded[winner.Tag] = treatyEnd;
		if (terms.ReleaseVassals)
		{
			foreach (Country v in VassalsOf(loser.Tag).ToList())
			{
				v.OverlordTag = null;
				DiplomacyRules.AddModifier(v, winner.Tag, "Freed us from our overlord", 30, Date, 15);
				if (v.Tag == PlayerTag)
					Notify($"The treaty frees us from {loser.Name}.");
			}
		}
		if (terms.Humiliate)
		{
			winner.Prestige = Math.Clamp(winner.Prestige + WarRules.HumiliatePrestige, -100, 100);
			loser.Prestige = Math.Clamp(loser.Prestige - WarRules.HumiliatePrestige, -100, 100);
			loser.Legitimacy = Math.Max(0, loser.Legitimacy - 10);
			DiplomacyRules.AddModifier(loser, winner.Tag, "Humbled us", -30, Date);
			if (winner.Tag == PlayerTag || loser.Tag == PlayerTag)
				Notify($"{loser.Name} is humbled before the world by {winner.Name}.");
		}
		if (war.CasusBelli == CasusBelli.Coalition)
		{
			// the coalition has had its war: it breaks up, its fear half spent
			foreach (Country m in SettledCountries.Where(m => m.CoalitionAgainst == war.DefenderTag))
			{
				m.CoalitionAgainst = null;
				if (m.Aggression.ContainsKey(war.DefenderTag))
					m.Aggression[war.DefenderTag] *= 0.5;
			}
		}
		if (!terms.IsWhitePeace)
		{
			winner.Prestige = Math.Clamp(winner.Prestige + WarRules.PrestigeForVictory, -100, 100);
			loser.Prestige = Math.Clamp(loser.Prestige - WarRules.PrestigeForVictory, -100, 100);
			loser.Legitimacy = Math.Max(0, loser.Legitimacy - 10);
			if (terms.Provinces.Count > 0)
				StateRules.ChangeStability(loser, -1);
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
			terms.Vassalize ? "its independence, becoming a vassal" : null,
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

	// ------------------------------------------------------------------------------------ subjects

	/// <summary>Why we can't offer a country vassalage, or null: it must be small beside us and fond of us.</summary>
	public string VassalageObstacle(Country us, Country them)
	{
		if (them == null || them.CapitalId <= 0 || them == us)
			return "No such country";
		if (them.OverlordTag == us.Tag)
			return "Already our vassal";
		if (them.OverlordTag != null)
			return $"A vassal of {GetCountry(them.OverlordTag)?.Name} already";
		if (us.OverlordTag != null)
			return "A vassal cannot take vassals";
		if (AtWarWith(us.Tag, them.Tag))
			return "We are at war with them";
		double ours = WarRules.Strength(us, _provinces, _armies.Values, Date), theirs = WarRules.Strength(them, _provinces, _armies.Values, Date);
		if (ours < theirs * 5 || ProvincesOf(us.Tag).Count() < ProvincesOf(them.Tag).Count() * 3)
			return "They would bow only to a realm many times their size";
		if (DiplomacyRules.Opinion(them, us.Tag) < 70)
			return $"They must think 70 or better of us (now {DiplomacyRules.Opinion(them, us.Tag)})";
		return null;
	}

	/// <summary>The player offers a small, friendly country vassalage: tribute and protection.</summary>
	public bool OfferVassalage(string tag, out string reason)
	{
		Country us = PlayerCountry, them = GetCountry(tag);
		reason = us == null ? "No country" : VassalageObstacle(us, them);
		if (reason != null)
			return false;
		them.OverlordTag = us.Tag;
		them.VassalSince = Date;
		foreach (string ally in them.Allies.ToList())
		{
			them.Allies.Remove(ally);
			GetCountry(ally)?.Allies.Remove(them.Tag);
		}
		Notify($"{them.Name} accepts the protection of {us.Name} and becomes our vassal.");
		EmitSignal(SignalName.DiplomacyChanged);
		return true;
	}

	public string AnnexObstacle(Country us, Country vassal)
	{
		if (vassal == null || vassal.OverlordTag != us.Tag)
			return "Not our vassal";
		double years = (Date.Day - vassal.VassalSince.Day) / 365.2425;
		if (years < WarRules.YearsBeforeAnnex)
			return $"Our vassal for {years:0} years; {WarRules.YearsBeforeAnnex} are needed";
		if (DiplomacyRules.Opinion(vassal, us.Tag) < 50)
			return $"They must think 50 or better of us (now {DiplomacyRules.Opinion(vassal, us.Tag)})";
		if (AtWar(vassal.Tag))
			return "Not while they are at war";
		return null;
	}

	/// <summary>A vassal of long standing is taken into the realm: its provinces become ours, conquered.</summary>
	public bool Annex(string tag, out string reason)
	{
		Country us = PlayerCountry, vassal = GetCountry(tag);
		reason = us == null ? "No country" : AnnexObstacle(us, vassal);
		if (reason != null)
			return false;
		AnnexVassal(us, vassal);
		return true;
	}

	void AnnexVassal(Country us, Country vassal)
	{
		foreach (Province p in ProvincesOf(vassal.Tag).ToList())
		{
			AddAggression(us, p, vassal, false, 0.5);
			p.OwnerTag = us.Tag;
			p.Control = new ProvinceControl { Kind = ControlKind.Conquered, Since = Date };
		}
		foreach (Army a in ArmiesOf(vassal.Tag).ToList())
		{
			MilitaryRules.SendHome(a.Regiments, _provinces, _rng);
			RemoveArmy(a);
		}
		us.Gold += Math.Max(0, vassal.Gold);
		vassal.Gold = 0;
		vassal.OverlordTag = null;
		vassal.CapitalId = 0;
		foreach (Country o in SettledCountries.Where(o => o != us && (o.OverlordTag == us.Tag || AreNeighbours(o.Tag, us.Tag))))
			DiplomacyRules.AddModifier(o, us.Tag, "Swallowed a vassal", -20, Date, 10);
		if (us.Tag == PlayerTag || AreNeighbours(PlayerTag, us.Tag))
			Notify($"{us.Name} annexes its vassal {vassal.Name}.");
		RefreshFlags();
		EmitSignal(SignalName.DiplomacyChanged);
		EmitSignal(SignalName.OwnershipChanged);
	}

	// ------------------------------------------------------------------------------------ monthly

	/// <summary>Opinions drift, truces lapse, tribute is paid, war scores are reckoned.</summary>
	void RunDiplomacyStep()
	{
		var settled = SettledCountries.ToList();
		foreach (Country c in settled)
		{
			if (c.OverlordTag != null && GetCountry(c.OverlordTag) is Country overlord && overlord.CapitalId > 0)
			{
				double tribute = TributeOf(c);
				c.Gold -= tribute;
				overlord.Gold += tribute;
			}
			else if (c.OverlordTag != null)
				c.OverlordTag = null;                    // the overlord is gone
			foreach (Country o in settled)
			{
				if (o != c)
					DiplomacyRules.MonthlyStep(c, o, AreNeighbours(c.Tag, o.Tag), Date);
			}
			foreach (string t in c.Truces.Where(kv => kv.Value <= Date).Select(kv => kv.Key).ToList())
				c.Truces.Remove(t);
			c.Prestige *= 0.995;
			StateRules.MonthlyStep(c, AtWar(c.Tag));
		}
		RunCoalitionStep();
		RunFabricationStep();
		// war goals tick; reparations are paid; treaties lapse
		foreach (War w in _wars.Values)
		{
			Province goal = GetProvince(w.GoalProvinceId);
			if (goal == null)
				continue;
			if (goal.OccupierTag != null && w.SideOf(goal.OccupierTag) > 0)
				w.TickingScore = Math.Min(WarRules.TickingMax, w.TickingScore + WarRules.TickingPerMonth);
			else if (goal.OccupierTag == null && (Date.Day - w.Started.Day) / 365.2425 >= 1)
				w.TickingScore = Math.Max(WarRules.TickingMin, w.TickingScore - WarRules.TickingDownPerMonth);
		}
		foreach (Country c in settled)
		{
			foreach (var (toTag, until) in c.Reparations.ToList())
			{
				if (until <= Date || GetCountry(toTag) is not Country to || to.CapitalId <= 0)
				{
					c.Reparations.Remove(toTag);
					continue;
				}
				double paid = Math.Max(0, c.LastLedger.Income) * WarRules.ReparationsShare;
				c.Gold -= paid;
				to.Gold += paid;
			}
			foreach (string toTag in c.TradePowerCeded.Where(kv => kv.Value <= Date).Select(kv => kv.Key).ToList())
				c.TradePowerCeded.Remove(toTag);
		}
		foreach (War w in _wars.Values)
			WarRules.UpdateScore(w, _countries, _provinces);
		EmitSignal(SignalName.DiplomacyChanged);
		if (_wars.Count > 0)
			EmitSignal(SignalName.WarsChanged);
	}
}
