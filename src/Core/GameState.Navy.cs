using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Navies: building ships, harbours, carrying armies over the sea and landing them.
public partial class GameState
{
	/// <summary>Whether a country's ships may lie in a province: a harbour of its own side or friends, not held by enemies.</summary>
	public bool FriendlyHarbour(Province p, string tag, UnitDomain domain)
	{
		if (!NavalRules.IsHarbour(p, domain) || p.OwnerTag == null)
			return false;
		string holder = p.OccupierTag ?? p.OwnerTag;
		return holder == tag || SameSide(tag, holder) || GetCountry(tag)?.Allies.Contains(holder) == true
			|| GetCountry(tag)?.OverlordTag == holder || GetCountry(holder)?.OverlordTag == tag;
	}

	Func<Province, bool> HarbourTest(Army a) => p => FriendlyHarbour(p, a.OwnerTag, a.Domain);

	public IEnumerable<Army> FleetsIn(int provinceId) => ArmiesIn(provinceId).Where(a => a.IsFleet);

	/// <summary>The armies aboard a fleet.</summary>
	public IEnumerable<Army> Cargo(Army fleet) => _armies.Values.Where(a => a.AboardFleetId == fleet.Id);

	public int CargoUsed(Army fleet) => Cargo(fleet).Sum(a => a.Regiments.Count);

	/// <summary>Ships a country could build in a province: those its techs allow and the harbour fits.</summary>
	public List<UnitType> ShipTypesFor(Country c, Province p) =>
		Definitions.UnitTypes.Where(t => t.IsShip && TechRules.Knows(c, t.Tech) && NavalRules.CanBuild(t, p)).ToList();

	Army NewFleet(Country c, int provinceId)
	{
		var fleet = new Army
		{
			Id = _nextArmyId++,
			OwnerTag = c.Tag,
			Name = $"{MilitaryRules.Ordinal(ArmiesOf(c.Tag).Count(a => a.IsFleet) + 1)} Fleet",
			ProvinceId = provinceId,
		};
		_armies[fleet.Id] = fleet;
		return fleet;
	}

	/// <summary>Builds a ship in one of the country's harbours; it joins the fleet lying there, or starts one.</summary>
	public bool BuildShip(int provinceId, string typeId, out string reason) => BuildShipFor(PlayerCountry, provinceId, typeId, out reason);

	bool BuildShipFor(Country c, int provinceId, string typeId, out string reason)
	{
		Province p = GetProvince(provinceId);
		UnitType type = Definitions.GetUnitType(typeId);
		reason = null;
		if (c == null || p == null || type == null || !type.IsShip)
			reason = "No such ship";
		else if (p.OwnerTag != c.Tag || p.OccupierTag != null)
			reason = "Ships are built in our own harbours";
		else if (!TechRules.Knows(c, type.Tech))
			reason = $"We don't know how to build {type.Name.ToLowerInvariant()}";
		else if (!NavalRules.CanBuild(type, p))
			reason = type.Domain == UnitDomain.SeaShip ? $"{p.Name} has no harbour on the sea" : $"{p.Name} is on no river or coast";
		else if (c.Gold < type.Cost)
			reason = $"{type.Name} cost {type.Cost:0} gold; we have {c.Gold:0}";
		if (reason != null)
			return false;
		c.Gold -= type.Cost;
		Army fleet = FleetsIn(p.Id).FirstOrDefault(f => f.OwnerTag == c.Tag && !f.Moving && f.Domain == type.Domain) ?? NewFleet(c, p.Id);
		fleet.Regiments.Add(new Regiment { Type = type, HomeProvinceId = p.Id });
		if (c.Tag == PlayerTag)
			Notify($"{type.Name} launched at {p.Name}: the {fleet.Name} has {fleet.Regiments.Count} ships.");
		EmitSignal(SignalName.ArmiesChanged);
		return true;
	}

	/// <summary>An army boards a fleet in the same harbour, or in the sea off its coast.</summary>
	public bool Embark(int armyId, int fleetId, out string reason)
	{
		Army a = GetArmy(armyId), fleet = GetArmy(fleetId);
		reason = null;
		if (a == null || fleet == null || a.OwnerTag != PlayerTag || fleet.OwnerTag != PlayerTag)
			reason = "Not ours";
		else if (a.IsFleet || !fleet.IsFleet)
			reason = "Only an army can board a fleet";
		else if (a.AboardFleetId != 0)
			reason = "Already aboard";
		else if (a.Moving || fleet.Moving)
			reason = "Both must be standing still";
		else if (a.ProvinceId != fleet.ProvinceId && GetProvince(a.ProvinceId)?.GetAdjacency(GetProvince(fleet.ProvinceId))?.SharesBorder != true)
			reason = "The fleet must lie in this harbour or off this coast";
		else if (CargoUsed(fleet) + a.Regiments.Count > fleet.Capacity)
			reason = $"The {fleet.Name} has room for {fleet.Capacity - CargoUsed(fleet)} more regiments";
		if (reason != null)
			return false;
		a.AboardFleetId = fleet.Id;
		a.ProvinceId = fleet.ProvinceId;
		a.Path.Clear();
		a.StepDays = 0;
		EmitSignal(SignalName.ArmiesChanged);
		EmitSignal(SignalName.ArmiesMoved);
		return true;
	}

	/// <summary>An army aboard a fleet goes ashore: into the harbour the fleet lies in, or onto a coast beside its sea.</summary>
	public bool Disembark(int armyId, int provinceId, out string reason)
	{
		Army a = GetArmy(armyId);
		Army fleet = a == null ? null : GetArmy(a.AboardFleetId);
		Province to = GetProvince(provinceId), at = fleet == null ? null : GetProvince(fleet.ProvinceId);
		reason = null;
		if (a == null || fleet == null || at == null)
			reason = "The army is not aboard a fleet";
		else if (fleet.Moving)
			reason = "The fleet is under way";
		else if (to == null || to.IsWater || !to.IsPassable)
			reason = "Troops land on a coast";
		else if (to != at && at.GetAdjacency(to)?.SharesBorder != true)
			reason = $"{to.Name} is not beside the fleet";
		if (reason != null)
			return false;
		a.AboardFleetId = 0;
		a.ProvinceId = to.Id;
		a.Path.Clear();
		a.StepDays = 0;
		if (a.OwnerTag == PlayerTag)
			Notify($"The {a.Name} lands at {to.Name}.");
		if (ArmiesIn(to.Id).Any(o => Enemies(a.OwnerTag, o.OwnerTag)))
			CheckBattle(a, at.IsWater ? at.Id : 0);
		EmitSignal(SignalName.ArmiesChanged);
		EmitSignal(SignalName.ArmiesMoved);
		return true;
	}

	/// <summary>
	/// A right-click order for the selected unit. An army aboard a fleet ordered onto a coast goes ashore;
	/// a fleet with troops aboard ordered onto a coast beside it (that is no harbour of ours) lands them;
	/// anything else is a march or a voyage.
	/// </summary>
	public bool OrderUnit(int armyId, int provinceId, out string reason)
	{
		Army a = GetArmy(armyId);
		Province to = GetProvince(provinceId);
		reason = null;
		if (a == null || to == null)
			return false;
		if (a.AboardFleetId != 0)
			return Disembark(armyId, provinceId, out reason);
		if (a.IsFleet && !to.IsWater && Cargo(a).Any() && !FriendlyHarbour(to, a.OwnerTag, a.Domain))
		{
			bool any = false;
			foreach (Army army in Cargo(a).ToList())
				any |= Disembark(army.Id, provinceId, out reason);
			return any;
		}
		return MoveArmy(armyId, provinceId, out reason);
	}

	/// <summary>The nearest harbour a fleet can reach and lie in, or null.</summary>
	Province NearestHarbour(Army fleet)
	{
		Province start = GetProvince(fleet.ProvinceId);
		if (start == null)
			return null;
		Func<Province, bool> harbour = HarbourTest(fleet);
		var seen = new HashSet<int> { start.Id };
		var queue = new Queue<Province>();
		queue.Enqueue(start);
		while (queue.Count > 0)
		{
			Province p = queue.Dequeue();
			if (p != start && harbour(p))
				return p;
			foreach (Adjacency link in p.Neighbors)
			{
				if (MovementRules.CanMove(fleet.Domain, link, harbour) && seen.Add(link.To.Id))
					queue.Enqueue(link.To);
			}
		}
		return null;
	}

	/// <summary>Whether a besieged coast has the besiegers' ships lying off it.</summary>
	bool Blockaded(Province p, string siegerTag) =>
		p.Neighbors.Any(l => l.SharesBorder && l.To.IsSea && FleetsIn(l.To.Id).Any(f => SameSide(f.OwnerTag, siegerTag)));

	/// <summary>A country run by the game keeps a few ships in its harbours for the trade they protect.</summary>
	void AiNavy(Country c)
	{
		if (_rng.NextDouble() > 0.1 || c.Gold < 150)
			return;
		int ships = ArmiesOf(c.Tag).Where(a => a.IsFleet).Sum(a => a.Regiments.Count);
		if (ships >= 4)
			return;
		Province harbour = ProvincesOf(c.Tag).FirstOrDefault(p => p.Id == c.CapitalId && NavalRules.IsHarbour(p))
			?? ProvincesOf(c.Tag).FirstOrDefault(NavalRules.IsHarbour);
		if (harbour == null)
			return;
		UnitType type = ShipTypesFor(c, harbour).OrderByDescending(t => t.Domain == UnitDomain.SeaShip).ThenByDescending(t => t.TradePower).FirstOrDefault();
		if (type != null && c.Gold >= type.Cost + 100)
			BuildShipFor(c, harbour.Id, type.Id, out _);
	}
}
