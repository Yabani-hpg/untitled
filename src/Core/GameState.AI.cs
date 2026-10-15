using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// The countries the game runs: each month they court their neighbours, seek allies, press their
// claims, raise their levies, march, besiege, and make peace when the war is won or lost.
public partial class GameState
{
	/// <summary>Monthly chance a country with a claim and the strength to press it declares war.</summary>
	const double AiWarChance = 0.04;
	/// <summary>A country attacks only when its side is this much stronger than the enemy's.</summary>
	const double AiAttackRatio = 1.3;
	/// <summary>War score at which a winner offers peace and takes what it can.</summary>
	const double AiPeaceScore = 35;

	void RunAiStep()
	{
		foreach (Country c in SettledCountries.Where(c => c.Tag != PlayerTag).ToList())
		{
			AiDiplomacy(c);
			AiRivals(c);
			AiTrade(c);
			AiNavy(c);
			if (AtWar(c.Tag))
				AiWar(c);
			else
				AiPeacetime(c);
		}
		foreach (War w in _wars.Values.ToList())
			AiPeace(w);
		// the player's unanswered offers lapse
		foreach (War w in _wars.Values)
		{
			if (w.Offer != null && w.Offer.Expires <= Date)
				w.Offer = null;
		}
	}

	// ------------------------------------------------------------------------------------ diplomacy

	void AiDiplomacy(Country c)
	{
		AiFinances(c);
		// a steady realm first: stability bought when it is low and the treasury allows
		int provinces = ProvincesOf(c.Tag).Count();
		if (c.Stability < 1 && c.Gold > StateRules.StabilityCost(c, provinces) * 2)
		{
			c.Gold -= StateRules.StabilityCost(c, provinces);
			StateRules.ChangeStability(c, 1);
		}
		var settled = SettledCountries.Where(o => o != c).ToList();
		var neighbours = settled.Where(o => AreNeighbours(c.Tag, o.Tag)).ToList();

		// diplomats court the strongest neighbours that are not enemies, allies first
		c.ImprovingCountryRelations.RemoveWhere(t => GetCountry(t) == null || AtWarWith(c.Tag, t));
		foreach (Country o in neighbours.Where(o => !AtWarWith(c.Tag, o.Tag) && DiplomacyRules.Opinion(o, c.Tag) < 60)
			.OrderByDescending(o => c.Allies.Contains(o.Tag) ? 1 : 0).ThenByDescending(o => WarRules.Strength(o, _provinces, _armies.Values, Date)))
		{
			if (FreeDiplomats(c) <= 0 || c.ImprovingCountryRelations.Contains(o.Tag))
				continue;
			c.ImprovingCountryRelations.Add(o.Tag);
		}

		// an alliance with a neighbour that likes us, if we have fewer than two
		if (c.Allies.Count < 2 && _rng.NextDouble() < 0.03)
		{
			Country partner = neighbours.Where(o => o.Tag != PlayerTag && DiplomacyRules.AllianceObstacle(c, o, _wars.Values) == null
					&& DiplomacyRules.Opinion(c, o.Tag) >= DiplomacyRules.AllianceOpinion)
				.OrderByDescending(o => DiplomacyRules.Opinion(o, c.Tag)).FirstOrDefault();
			if (partner != null && DiplomacyRules.AcceptsAlliance(partner, c,
				WarRules.Strength(c, _provinces, _armies.Values, Date), WarRules.Strength(partner, _provinces, _armies.Values, Date)))
			{
				MakeAlliance(c, partner);
				if (AreNeighbours(PlayerTag, c.Tag) || AreNeighbours(PlayerTag, partner.Tag))
					Notify($"{c.Name} and {partner.Name} make an alliance.");
			}
		}

		AiForts(c);
		// a vassal of long standing that loves us is taken in
		foreach (Country v in VassalsOf(c.Tag).ToList())
		{
			if (AnnexObstacle(c, v) == null && _rng.NextDouble() < 0.1)
				AnnexVassal(c, v);
		}
		// war: press a claim or take revenge on a neighbour we outmatch (vassals make no war)
		if (AtWar(c.Tag) || c.Gold < 20 || c.OverlordTag != null)
			return;
		foreach (Country target in neighbours.OrderByDescending(o => c.Rivals.Contains(o.Tag) ? 1 : 0).ThenBy(_ => _rng.Next()))
		{
			CasusBelli cb = BestCasusBelli(c, target);
			// war without cause only on a rival, and only when the neighbourhood does not fear us yet
			if (cb == CasusBelli.Conquest && (!c.Rivals.Contains(target.Tag) || neighbours.Any(o => AggressionOf(o, c.Tag) > DiplomacyRules.CoalitionThreshold * 0.5)) || WarObstacle(c, target) != null)
				continue;
			double ours = WarRules.SideStrength(c.Allies.Append(c.Tag), _countries, _provinces, _armies.Values, Date);
			double theirs = WarRules.SideStrength(target.Allies.Append(target.Tag), _countries, _provinces, _armies.Values, Date);
			if (ours < theirs * AiAttackRatio || _rng.NextDouble() >= AiWarChance)
				continue;
			StartWar(c, target, cb);
			return;
		}
	}

	// ------------------------------------------------------------------------------------ war

	void AiWar(Country c)
	{
		var owned = ProvincesOf(c.Tag).ToList();
		var armies = ArmiesOf(c.Tag).ToList();
		// call up the levies, as many as the treasury can arm
		int available = AvailableLevies(c);
		int under = MilitaryRules.UnderArms(c, _armies.Values, owned);
		if (available > 0 && under < 0.8 * (under + available))
		{
			int want = Math.Min(available, Math.Max(1, (int)(0.8 * (under + available)) - under));
			if (RaiseLeviesFor(c, want, out _) is Army raised)
				armies.Add(raised);
		}
		// the strongest enemy army in the field decides whether we may march or must gather
		int enemyField = _armies.Values.Where(e => _wars.Values.Any(w => w.AreEnemies(c.Tag, e.OwnerTag))).Select(e => e.Men).DefaultIfEmpty(0).Max();
		// armies standing together join
		foreach (var group in armies.Where(a => !a.Moving).GroupBy(a => a.ProvinceId).Where(g => g.Count() > 1))
		{
			Army main = group.OrderByDescending(a => a.Regiments.Count).First();
			foreach (Army other in group.Where(a => a != main).ToList())
			{
				main.Regiments.AddRange(other.Regiments);
				RemoveArmy(other);
				armies.Remove(other);
			}
		}
		var enemies = _wars.Values.Where(w => w.Includes(c.Tag)).SelectMany(w => w.SideOf(c.Tag) > 0 ? w.Defenders : w.Attackers).Distinct().ToList();
		foreach (Army a in armies.Where(a => !a.Moving && a.Regiments.Count > 0))
		{
			Province here = GetProvince(a.ProvinceId);
			// stay to finish a siege
			if (here != null && here.OwnerTag != null && enemies.Contains(here.OwnerTag) && here.OccupierTag == null)
				continue;
			// an enemy army in our land that we can beat: go for it
			Army invader = _armies.Values.Where(e => enemies.Contains(e.OwnerTag) && !e.Moving && GetProvince(e.ProvinceId)?.OwnerTag == c.Tag)
				.OrderBy(e => Steps(here, GetProvince(e.ProvinceId))).FirstOrDefault();
			if (invader != null && a.Men >= invader.Men * 0.9)
			{
				OrderArmy(a, invader.ProvinceId);
				continue;
			}
			// too weak to face the enemy in the field: gather at home (the capital) and wait for more levies
			if (a.Men < enemyField * 0.7)
			{
				if (a.ProvinceId != c.CapitalId && GetProvince(c.CapitalId)?.OccupierTag == null)
					OrderArmy(a, c.CapitalId);
				continue;
			}
			// else the nearest enemy province still to take, the capital first when it is near, avoiding stacks we can't beat
			Province target = _provinces.Where(p => p?.OwnerTag != null && enemies.Contains(p.OwnerTag) && p.OccupierTag == null && p.IsPassable
					&& ArmiesIn(p.Id).Where(e => enemies.Contains(e.OwnerTag)).Sum(e => e.Men) <= a.Men * 1.1)
				.OrderBy(p => Steps(here, p) - (GetCountry(p.OwnerTag)?.CapitalId == p.Id ? 2 : 0)).FirstOrDefault();
			if (target != null && Steps(here, target) < 40)
				OrderArmy(a, target.Id);
			else if (here?.OwnerTag != c.Tag && NearestFriendly(a) is Province home)
				OrderArmy(a, home.Id);
		}
	}

	/// <summary>Provinces between two provinces, as the crow flies over the map.</summary>
	int Steps(Province a, Province b)
	{
		if (a == null || b == null || ProvinceMap.GetCentroid(a.Id) is not Godot.Vector2 pa || ProvinceMap.GetCentroid(b.Id) is not Godot.Vector2 pb)
			return 999;
		float dx = Math.Abs(pa.X - pb.X);
		dx = Math.Min(dx, ProvinceMap.Width - dx);
		return (int)(Math.Sqrt(dx * dx + (pa.Y - pb.Y) * (pa.Y - pb.Y)) / 40);
	}

	void AiPeacetime(Country c)
	{
		// levies go home when there is no war
		foreach (Army a in ArmiesOf(c.Tag).Where(a => !a.Moving).ToList())
			Disband(a);
	}

	// ------------------------------------------------------------------------------------ peace

	/// <summary>
	/// The leaders of a war that the game runs make peace: a winner at a good score demands what it can
	/// (its claims first, then occupied land, then gold); a war going nowhere for long ends white.
	/// Against the player, the offer is put to them instead.
	/// </summary>
	void AiPeace(War war)
	{
		if (!_wars.ContainsKey(war.Id))
			return;
		WarRules.UpdateScore(war, _countries, _provinces);
		int months = (int)((Date.Day - war.Started.Day) / 30.44);
		foreach (int side in new[] { 1, -1 })
		{
			string leaderTag = side > 0 ? war.AttackerTag : war.DefenderTag;
			string enemyTag = side > 0 ? war.DefenderTag : war.AttackerTag;
			if (leaderTag == PlayerTag)
				continue;
			Country leader = GetCountry(leaderTag), enemy = GetCountry(enemyTag);
			double score = war.Score * side;
			PeaceTerms terms = null;
			int winnerSide = side;
			if (score >= AiPeaceScore || score > 5 && months >= WarRules.MonthsBeforePeace * 2)
				terms = AiDemands(war, side, leader, score);
			else if (Math.Abs(score) <= 10 && months >= WarRules.MonthsBeforePeace * 2)
				terms = new PeaceTerms();                         // white peace
			else if (score <= -AiPeaceScore && months >= WarRules.MonthsBeforePeace / 2 && enemyTag != PlayerTag)
				continue;                                           // the winner will name its price
			else if (score <= -AiPeaceScore && months >= WarRules.MonthsBeforePeace / 2)
			{
				// losing to the player: offer what the score is worth
				winnerSide = -side;
				terms = AiDemands(war, -side, enemy, -score);
			}
			if (terms == null)
				continue;
			if (enemyTag == PlayerTag)
			{
				if (war.Offer == null)
				{
					war.Offer = new PeaceOffer(leaderTag, winnerSide, terms, Date.AddDays(31));
					Notify(terms.IsWhitePeace ? $"{leader.Name} offers a white peace." : winnerSide == side
						? $"{leader.Name} demands peace: {DescribeTerms(terms)}. Answer in the war window."
						: $"{leader.Name} sues for peace, offering {DescribeTerms(terms)}. Answer in the war window.");
					EmitSignal(SignalName.WarsChanged);
				}
				continue;
			}
			double cost = WarRules.TermsCost(terms, GetCountry(winnerSide > 0 ? war.AttackerTag : war.DefenderTag),
				winnerSide > 0 ? war.Defenders : war.Attackers, _countries, _provinces);
			if (WarRules.AcceptsPeace(war, winnerSide, cost, Date))
			{
				MakePeace(war, winnerSide, terms);
				return;
			}
		}
	}

	/// <summary>What a winner asks for with its score: claimed land first, then occupied land, then gold.</summary>
	PeaceTerms AiDemands(War war, int winnerSide, Country winner, double score)
	{
		var terms = new PeaceTerms();
		var losers = winnerSide > 0 ? war.Defenders : war.Attackers;
		Country loserLeaderFirst = GetCountry(winnerSide > 0 ? war.DefenderTag : war.AttackerTag);
		// a small loser is made a vassal when the score allows and it has no overlord
		if (loserLeaderFirst != null && loserLeaderFirst.OverlordTag == null && winner.OverlordTag == null
			&& ProvincesOf(loserLeaderFirst.Tag).Count() <= 3 && ProvincesOf(loserLeaderFirst.Tag).Count() * 3 <= ProvincesOf(winner.Tag).Count())
		{
			terms.Vassalize = true;
			if (WarRules.TermsCost(terms, winner, losers, _countries, _provinces) <= score)
				return terms;
			terms.Vassalize = false;
		}
		var candidates = WarRules.Demandable(war, winnerSide, _provinces)
			.OrderByDescending(p => winner.Claims.Contains(p.Id) ? 2 : 0 + (p.OccupierTag != null ? 1 : 0))
			.ThenBy(p => WarRules.Value(p, GetCountry(p.OwnerTag))).ToList();
		double budget = score;
		// a rival beaten is a rival humbled
		if (loserLeaderFirst != null && winner.Rivals.Contains(loserLeaderFirst.Tag) && budget >= WarRules.HumiliateCost + 10)
		{
			terms.Humiliate = true;
			budget -= WarRules.HumiliateCost;
		}
		foreach (Province p in candidates)
		{
			if (!winner.Claims.Contains(p.Id) && p.OccupierTag == null)
				continue;
			terms.Provinces.Add(p.Id);
			if (WarRules.TermsCost(terms, winner, losers, _countries, _provinces) > budget)
			{
				terms.Provinces.RemoveAt(terms.Provinces.Count - 1);
				break;
			}
		}
		double left = budget - WarRules.TermsCost(terms, winner, losers, _countries, _provinces);
		Country loserLeader = GetCountry(winnerSide > 0 ? war.DefenderTag : war.AttackerTag);
		if (left >= 2 && loserLeader != null)
			terms.Gold = Math.Min(Math.Max(0, loserLeader.Gold) * 0.8, Math.Floor(left) * WarRules.GoldPerScorePoint);
		return terms;
	}

	public string DescribeTerms(PeaceTerms terms)
	{
		if (terms.IsWhitePeace)
			return "a white peace";
		var parts = new List<string>();
		if (terms.Provinces.Count > 0)
			parts.Add(string.Join(", ", terms.Provinces.Select(id => GetProvince(id)?.Name)));
		if (terms.Gold > 0)
			parts.Add($"{terms.Gold:0} gold");
		if (terms.Humiliate)
			parts.Add("a humbling before the world");
		return string.Join(" and ", parts);
	}

	/// <summary>The player answers a peace offered by the game.</summary>
	public void AnswerPeaceOffer(int warId, bool accept)
	{
		War war = GetWar(warId);
		if (war?.Offer == null)
			return;
		PeaceOffer offer = war.Offer;
		war.Offer = null;
		if (accept)
			MakePeace(war, offer.WinnerSide, offer.Terms);
		else
		{
			Notify($"We refuse the peace {GetCountry(offer.FromTag)?.Name} offered.");
			EmitSignal(SignalName.WarsChanged);
		}
	}
}
