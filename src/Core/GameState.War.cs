using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// The fighting of wars: battles where enemy armies meet, sieges and occupation, retreats.
public partial class GameState
{
	/// <summary>Whether the owners of two armies are enemies in some war.</summary>
	bool Enemies(string a, string b) => a != b && AtWarWith(a, b);

	/// <summary>Everyone on a country's side in its wars with <paramref name="enemy"/>: itself and its co-belligerents.</summary>
	bool SameSide(string a, string b) => a == b || _wars.Values.Any(w => w.SideOf(a) != 0 && w.SideOf(a) == w.SideOf(b));

	/// <summary>Orders any army (the player's or the game's) to march, by the quickest way over land.</summary>
	bool OrderArmy(Army a, int provinceId)
	{
		Province to = GetProvince(provinceId);
		if (a == null || to == null || !a.IsFleet && !to.IsPassable)
			return false;
		List<int> path = MilitaryRules.FindPath(GetProvince(a.ProvinceId), to, MilitaryRules.Speed(a, GetCountry(a.OwnerTag)), _provinces, ProvinceMap.GetCentroid, ProvinceMap.Width,
			a.Domain, a.IsFleet ? HarbourTest(a) : null, a.IsFleet ? null : p => InHostileZone(p, a.OwnerTag));
		if (path == null)
			return false;
		a.Path.Clear();
		a.Path.AddRange(path);
		a.DaysMarched = 0;
		a.StepDays = a.Path.Count > 0 ? StepDays(a, a.Path[0]) : 0;
		return true;
	}

	/// <summary>The nearest province (by steps) owned by the army's country or its side; null if none can be reached.</summary>
	Province NearestFriendly(Army a, bool excludeHere = true)
	{
		Province start = GetProvince(a.ProvinceId);
		if (start == null)
			return null;
		var seen = new HashSet<int> { start.Id };
		var queue = new Queue<Province>();
		queue.Enqueue(start);
		while (queue.Count > 0)
		{
			Province p = queue.Dequeue();
			if (p.OwnerTag != null && SameSide(a.OwnerTag, p.OwnerTag) && p.OccupierTag == null && (!excludeHere || p != start))
				return p;
			foreach (Adjacency link in p.Neighbors)
			{
				if (MovementRules.CanMove(UnitDomain.Land, link) && seen.Add(link.To.Id))
					queue.Enqueue(link.To);
			}
		}
		return null;
	}

	/// <summary>An army goes back to its own land (at the peace); with nowhere to go, it disbands.</summary>
	void SendArmyHome(Army a)
	{
		Province home = NearestFriendly(a);
		if (home == null || !OrderArmy(a, home.Id))
		{
			MilitaryRules.SendHome(a.Regiments, _provinces, _rng);
			RemoveArmy(a);
		}
	}

	// ------------------------------------------------------------------------------------ battles

	/// <summary>
	/// An army has arrived in a province: if enemies stand there, battle. The arriving side attacks; the
	/// defenders have the terrain and a river crossing against the attacker. All armies of each side in the
	/// province fight together; the loser's armies retreat to the nearest friendly province.
	/// </summary>
	bool CheckBattle(Army arrived, int fromProvinceId)
	{
		Province p = GetProvince(arrived.ProvinceId);
		if (p == null)
			return false;
		var enemies = ArmiesIn(p.Id).Where(o => o.IsFleet == arrived.IsFleet && Enemies(arrived.OwnerTag, o.OwnerTag)).ToList();
		if (enemies.Count == 0)
			return true;
		var attackers = ArmiesIn(p.Id).Where(o => o.IsFleet == arrived.IsFleet && SameSide(arrived.OwnerTag, o.OwnerTag)).ToList();
		var defenders = ArmiesIn(p.Id).Where(o => o.IsFleet == arrived.IsFleet && enemies.Any(e => SameSide(e.OwnerTag, o.OwnerTag))).ToList();
		return FightBattle(attackers, defenders, p, fromProvinceId);
	}

	bool FightBattle(List<Army> attackers, List<Army> defenders, Province p, int fromProvinceId)
	{
		Country ao = GetCountry(attackers[0].OwnerTag), dO = GetCountry(defenders[0].OwnerTag);
		var aRegs = attackers.SelectMany(a => a.Regiments).ToList();
		var dRegs = defenders.SelectMany(a => a.Regiments).ToList();
		int aMen = aRegs.Sum(r => r.Men), dMen = dRegs.Sum(r => r.Men);
		int aDice = fromProvinceId > 0 && !p.IsWater ? CombatRules.AttackerDiceModifier(GetProvince(fromProvinceId), p) : 0;
		int dDice = p.IsWater ? 0 : TerrainRules.DefenseDice(p) + (p.OwnerTag != null && SameSide(defenders[0].OwnerTag, p.OwnerTag) ? 1 + FortRules.DefenceDice(p) : 0);
		var result = ControlRules.Battle(MilitaryRules.ForceOf(aRegs, p, aDice, ao), MilitaryRules.ForceOf(dRegs, p, dDice, dO), _rng);
		var aGone = MilitaryRules.TakeLosses(aRegs, result.AttackerShare, !result.AttackerWon);
		var dGone = MilitaryRules.TakeLosses(dRegs, result.DefenderShare, result.AttackerWon);
		foreach (Army a in attackers) a.Regiments.RemoveAll(aGone.Contains);
		foreach (Army a in defenders) a.Regiments.RemoveAll(dGone.Contains);
		int aLost = aMen - attackers.Sum(a => a.Men), dLost = dMen - defenders.Sum(a => a.Men);

		// the war score of the battle goes to the war between the two leaders' countries
		War war = WarBetween(ao.Tag, dO.Tag);
		if (war != null)
		{
			double score = result.AttackerWon
				? WarRules.BattleScoreOf(dMen, dLost, SideMen(war, war.SideOf(dO.Tag)) + dLost)
				: -WarRules.BattleScoreOf(aMen, aLost, SideMen(war, war.SideOf(ao.Tag)) + aLost);
			war.BattleScore = Math.Clamp(war.BattleScore + score * war.SideOf(ao.Tag), -40, 40);
			war.Battles.Add(new BattleRecord(Date, p.Id, ao.Tag, dO.Tag, result.AttackerWon, aMen, dMen, aLost, dLost));
			WarRules.UpdateScore(war, _countries, _provinces);
			Country winner = result.AttackerWon ? ao : dO, loser = result.AttackerWon ? dO : ao;
			winner.Prestige = Math.Clamp(winner.Prestige + 3, -100, 100);
			loser.Prestige = Math.Clamp(loser.Prestige - 3, -100, 100);
			StateRules.BattleExhaustion(ao, aLost, _armies.Values.Where(x => x.OwnerTag == ao.Tag).Sum(x => x.Men) + aLost);
			StateRules.BattleExhaustion(dO, dLost, _armies.Values.Where(x => x.OwnerTag == dO.Tag).Sum(x => x.Men) + dLost);
		}
		foreach (Army a in result.AttackerWon ? defenders : attackers)
			Retreat(a);
		RemoveEmptyArmies();
		if (attackers.Concat(defenders).Any(a => a.OwnerTag == PlayerTag))
		{
			bool weAttacked = attackers.Any(a => a.OwnerTag == PlayerTag);
			bool weWon = weAttacked == result.AttackerWon;
			Notify($"Battle of {p.Name}: {(weWon ? "victory" : "defeat")}. {ao.Adjective} {aMen:N0} against {dO.Adjective} {dMen:N0}; "
				+ $"{ao.Adjective} losses {aLost:N0}, {dO.Adjective} losses {dLost:N0}. The {(result.AttackerWon ? dO.Adjective : ao.Adjective)} {(p.IsWater ? "fleet" : "army")} falls back.");
		}
		EmitSignal(SignalName.ArmiesChanged);
		EmitSignal(SignalName.WarsChanged);
		return result.AttackerWon;
	}

	/// <summary>Men under arms on one side of a war (its armies in the field).</summary>
	int SideMen(War war, int side) =>
		_armies.Values.Where(a => war.SideOf(a.OwnerTag) == side).Sum(a => a.Men);

	/// <summary>A beaten army falls back to the nearest friendly province; with none, it scatters.</summary>
	void Retreat(Army a)
	{
		if (a.Regiments.Count == 0)
			return;
		Province home = a.IsFleet ? NearestHarbour(a) : NearestFriendly(a);
		if (home == null || !OrderArmy(a, home.Id))
		{
			MilitaryRules.SendHome(a.Regiments, _provinces, _rng);
			a.Regiments.Clear();
		}
	}

	// ------------------------------------------------------------------------------------ sieges

	/// <summary>
	/// A day of sieges. Armies standing unopposed in an enemy province put in siege days; at enough, the
	/// province is occupied and its garrison taken. An owner's army standing in its own occupied province
	/// retakes it the same way, twice as fast.
	/// </summary>
	void RunSiegeDay()
	{
		if (_wars.Count == 0)
			return;
		bool changed = false;
		foreach (var group in _armies.Values.Where(a => !a.Moving && a.Regiments.Count > 0 && !a.IsFleet && a.AboardFleetId == 0).GroupBy(a => a.ProvinceId))
		{
			Province p = GetProvince(group.Key);
			if (p?.OwnerTag == null)
				continue;
			// the enemies of the owner standing here, with no owner-side army to stop them
			var besiegers = group.Where(a => Enemies(a.OwnerTag, p.OwnerTag)).ToList();
			var owners = group.Where(a => SameSide(a.OwnerTag, p.OwnerTag)).ToList();
			if (besiegers.Count > 0 && owners.Count == 0 && (p.OccupierTag == null || !SameSide(besiegers[0].OwnerTag, p.OccupierTag)))
			{
				string sieger = besiegers[0].OwnerTag;
				if (p.SiegerTag != null && !SameSide(sieger, p.SiegerTag))
					p.SiegeProgress = 0;
				p.SiegerTag = sieger;
				// ships off the coast cut the province from the sea: the siege goes faster
				p.SiegeProgress += WarRules.SiegePower(besiegers, p) * (Blockaded(p, sieger) ? 1 + NavalRules.BlockadeSiegeBonus : 1);
				if (p.SiegeProgress >= WarRules.SiegeNeeded(p))
				{
					Occupy(p, sieger);
					changed = true;
				}
			}
			else if (owners.Count > 0 && besiegers.Count == 0 && p.OccupierTag != null)
			{
				// the owner's side takes its province back
				p.SiegerTag = p.OwnerTag;
				p.SiegeProgress += 2 * WarRules.SiegePower(owners, p);
				if (p.SiegeProgress >= WarRules.SiegeDays)
				{
					string former = p.OccupierTag;
					p.OccupierTag = null;
					p.SiegerTag = null;
					p.SiegeProgress = 0;
					if (p.OwnerTag == PlayerTag)
						Notify($"{p.Name} is ours again: the {GetCountry(former)?.Adjective} occupiers are driven out.");
					changed = true;
				}
			}
			else if (p.SiegerTag != null && besiegers.Count == 0 && owners.Count == 0)
			{
				p.SiegeProgress = Math.Max(0, p.SiegeProgress - 1);   // a siege lifted crumbles
			}
		}
		if (changed)
		{
			foreach (War w in _wars.Values)
				WarRules.UpdateScore(w, _countries, _provinces);
			EmitSignal(SignalName.WarsChanged);
			EmitSignal(SignalName.OwnershipChanged);
		}
	}

	void Occupy(Province p, string byTag)
	{
		Country owner = GetCountry(p.OwnerTag), by = GetCountry(byTag);
		p.OccupierTag = byTag;
		p.SiegerTag = null;
		p.SiegeProgress = 0;
		if (p.Control != null && p.Control.Garrison.Count > 0)
		{
			MilitaryRules.SendHome(p.Control.Garrison, _provinces, _rng);
			p.Control.Garrison.Clear();
		}
		if (p.OwnerTag == PlayerTag)
			Notify($"{p.Name} falls to the {by?.Adjective} army after a siege.{(owner?.CapitalId == p.Id ? " Our capital is in enemy hands!" : "")}");
		else if (byTag == PlayerTag)
			Notify($"Our army takes {p.Name} from {owner?.Name}.{(owner?.CapitalId == p.Id ? " Their capital is ours!" : "")}");
	}
}
