using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Map;
using Untitled.Rules;

namespace Untitled.Core;

// Armies: raising the levies, marching, garrisons, and sending the survivors home.
public partial class GameState
{
	/// <summary>Armies were raised, disbanded, split, merged, given orders, or lost regiments.</summary>
	[Signal]
	public delegate void ArmiesChangedEventHandler();

	/// <summary>A day's march: moving armies advanced (or arrived).</summary>
	[Signal]
	public delegate void ArmiesMovedEventHandler();

	[Signal]
	public delegate void SelectedArmyChangedEventHandler(int armyId);

	/// <summary>Armies in the field, by id.</summary>
	public IReadOnlyDictionary<int, Army> Armies => _armies;
	/// <summary>0 when no army is selected.</summary>
	public int SelectedArmyId { get; private set; }

	readonly Dictionary<int, Army> _armies = new();
	int _nextArmyId = 1;

	public Army GetArmy(int id) => id > 0 && _armies.TryGetValue(id, out Army a) ? a : null;

	public IEnumerable<Army> ArmiesOf(string tag) => _armies.Values.Where(a => a.OwnerTag == tag);

	public IEnumerable<Army> ArmiesIn(int provinceId) => _armies.Values.Where(a => a.ProvinceId == provinceId && !a.Moving);

	public void SelectArmy(int id)
	{
		if (GetArmy(id) == null)
			id = 0;
		if (id == SelectedArmyId)
			return;
		SelectedArmyId = id;
		EmitSignal(SignalName.SelectedArmyChanged, id);
	}

	Army NewArmy(Country c, int provinceId)
	{
		var army = new Army
		{
			Id = _nextArmyId++,
			OwnerTag = c.Tag,
			Name = $"{MilitaryRules.Ordinal(c.NextArmyNumber++)} Army",
			ProvinceId = provinceId,
		};
		_armies[army.Id] = army;
		return army;
	}

	void RemoveArmy(Army a)
	{
		_armies.Remove(a.Id);
		if (SelectedArmyId == a.Id)
			SelectArmy(0);
	}

	/// <summary>Regiments the country can still levy now.</summary>
	public int AvailableLevies(Country c) =>
		c == null ? 0 : MilitaryRules.AvailableLevies(c, ProvincesOf(c.Tag).ToList(), _armies.Values, Date);

	/// <summary>
	/// The player raises levies: <paramref name="regiments"/> units of people are taken from the provinces
	/// and armed by the levy template, gathering as a new army in the capital.
	/// </summary>
	public Army RaiseLevies(int regiments, out string reason)
	{
		Country c = PlayerCountry;
		reason = null;
		int available = AvailableLevies(c);
		if (c == null || GetProvince(c.CapitalId) == null)
			reason = "We have no capital to muster in";
		else if (regiments < 1)
			reason = "Raise at least one regiment";
		else if (regiments > available)
			reason = $"We can levy only {available} regiments";
		if (reason != null)
			return null;

		var (types, cost) = MilitaryRules.Arm(c, regiments, Definitions, this);
		var raised = MilitaryRules.Levy(c, ProvincesOf(c.Tag).ToList(), types, Date);
		if (raised.Count == 0)
		{
			reason = "Nobody answers the call";
			return null;
		}
		c.Gold -= raised.Sum(r => r.Type.Cost);
		Army army = NewArmy(c, c.CapitalId);
		army.Regiments.AddRange(raised);
		Notify($"The levies are called up: {raised.Count} regiments ({Describe(army)}) muster at {CapitalText(c)} as the {army.Name}."
			+ (cost > 0 ? $" Arming them cost {raised.Sum(r => r.Type.Cost):0} gold." : ""));
		AfterArmyChange(army.ProvinceId, affectsPeople: true);
		SelectArmy(army.Id);
		return army;
	}

	/// <summary>"12 foot, 2 mounted, 1 siege"</summary>
	public static string Describe(Army a)
	{
		var parts = new List<string>();
		foreach (UnitCategory cat in Enum.GetValues<UnitCategory>())
		{
			int n = a.Count(cat);
			if (n > 0)
				parts.Add($"{n} {cat.ToString().ToLowerInvariant()}");
		}
		return parts.Count > 0 ? string.Join(", ", parts) : "no regiments";
	}

	/// <summary>The army is disbanded: its survivors go home to their provinces and their fields.</summary>
	public void DisbandArmy(int armyId)
	{
		Army a = GetArmy(armyId);
		if (a == null || a.OwnerTag != PlayerTag)
			return;
		int home = MilitaryRules.SendHome(a.Regiments, _provinces, _rng);
		Notify($"The {a.Name} is disbanded: {home * PopGroup.PeoplePerUnit:N0} soldiers go home.");
		RemoveArmy(a);
		AfterArmyChange(a.ProvinceId, affectsPeople: true);
	}

	/// <summary>Orders the army to march to a province, by the quickest way over land.</summary>
	public bool MoveArmy(int armyId, int provinceId, out string reason)
	{
		reason = null;
		Army a = GetArmy(armyId);
		Province to = GetProvince(provinceId);
		if (a == null || a.OwnerTag != PlayerTag)
			reason = "Not our army";
		else if (to == null || to.IsWater)
			reason = "Armies march over land";
		if (reason != null)
			return false;
		// an army on the march turns back from where it stands
		List<int> path = MilitaryRules.FindPath(GetProvince(a.ProvinceId), to, MilitaryRules.Speed(a, GetCountry(a.OwnerTag)), _provinces, ProvinceMap.GetCentroid, ProvinceMap.Width);
		if (path == null)
		{
			reason = $"There is no way over land to {to.Name}";
			return false;
		}
		a.Path.Clear();
		a.Path.AddRange(path);
		a.DaysMarched = 0;
		a.StepDays = a.Path.Count > 0 ? StepDays(a, a.Path[0]) : 0;
		EmitSignal(SignalName.ArmiesChanged);
		return true;
	}

	int StepDays(Army a, int next) =>
		MilitaryRules.StepDays(GetProvince(a.ProvinceId), GetProvince(next), MilitaryRules.Speed(a, GetCountry(a.OwnerTag)), ProvinceMap.GetCentroid, ProvinceMap.Width);

	/// <summary>Days until the army reaches its destination.</summary>
	public int DaysToArrive(Army a)
	{
		if (!a.Moving)
			return 0;
		int days = a.StepDays - a.DaysMarched;
		for (int i = 1; i < a.Path.Count; i++)
			days += MilitaryRules.StepDays(GetProvince(a.Path[i - 1]), GetProvince(a.Path[i]), MilitaryRules.Speed(a, GetCountry(a.OwnerTag)), ProvinceMap.GetCentroid, ProvinceMap.Width);
		return days;
	}

	/// <summary>Splits the army in two, each half taking its share of foot, mounted and siege.</summary>
	public Army SplitArmy(int armyId)
	{
		Army a = GetArmy(armyId);
		if (a == null || a.OwnerTag != PlayerTag || a.Regiments.Count < 2 || a.Moving)
			return null;
		Army b = NewArmy(GetCountry(a.OwnerTag), a.ProvinceId);
		var ordered = a.Regiments.OrderBy(r => r.Type.Category).ThenBy(r => r.Type.Id).ToList();
		for (int i = 1; i < ordered.Count; i += 2)
		{
			a.Regiments.Remove(ordered[i]);
			b.Regiments.Add(ordered[i]);
		}
		AfterArmyChange(a.ProvinceId);
		return b;
	}

	/// <summary>The player's other armies standing in the same province join this one.</summary>
	public void MergeArmies(int armyId)
	{
		Army a = GetArmy(armyId);
		if (a == null || a.OwnerTag != PlayerTag || a.Moving)
			return;
		foreach (Army other in ArmiesIn(a.ProvinceId).Where(o => o != a && o.OwnerTag == a.OwnerTag).ToList())
		{
			a.Regiments.AddRange(other.Regiments);
			RemoveArmy(other);
		}
		AfterArmyChange(a.ProvinceId);
	}

	/// <summary>A regiment of the player's army in the province (the given one, or the largest) stays behind as part of its garrison.</summary>
	public bool GarrisonFromArmy(int provinceId, int armyId = 0)
	{
		Province p = GetProvince(provinceId);
		Army a = ArmiesIn(provinceId).Where(x => x.OwnerTag == PlayerTag && x.Regiments.Count > 0 && (armyId == 0 || x.Id == armyId))
			.OrderByDescending(x => x.Regiments.Count).FirstOrDefault();
		if (p?.Control == null || p.OwnerTag != PlayerTag || a == null)
			return false;
		// foot hold a province best; engines and horses last
		Regiment r = a.Regiments.OrderBy(x => x.Type.Category).ThenByDescending(x => x.Strength).First();
		a.Regiments.Remove(r);
		p.Control.Garrison.Add(r);
		if (a.Regiments.Count == 0)
			RemoveArmy(a);
		AfterArmyChange(provinceId);
		return true;
	}

	/// <summary>A regiment of the garrison joins the player's army in the province (or forms one).</summary>
	public bool ArmyFromGarrison(int provinceId)
	{
		Province p = GetProvince(provinceId);
		if (p?.Control == null || p.OwnerTag != PlayerTag || p.Control.Garrison.Count == 0)
			return false;
		Army a = ArmiesIn(provinceId).Where(x => x.OwnerTag == PlayerTag).OrderByDescending(x => x.Regiments.Count).FirstOrDefault()
			?? NewArmy(PlayerCountry, provinceId);
		Regiment r = p.Control.Garrison[^1];
		p.Control.Garrison.RemoveAt(p.Control.Garrison.Count - 1);
		a.Regiments.Add(r);
		AfterArmyChange(provinceId);
		return true;
	}

	/// <summary>Sets how the player's levies are armed: percent of the regiments for a unit type.</summary>
	public void SetLevyShare(string unitTypeId, int percent)
	{
		Country c = PlayerCountry;
		if (c == null || Definitions.GetUnitType(unitTypeId) == null)
			return;
		if (c.LevyTemplate.Count == 0)
		{
			// start from the defaults the player saw
			foreach (var (type, share) in MilitaryRules.Template(c, Definitions, this))
				c.LevyTemplate[type.Id] = share;
		}
		c.LevyTemplate[unitTypeId] = Math.Clamp(percent, 0, 100);
	}

	void AfterArmyChange(int provinceId, bool affectsPeople = false)
	{
		if (affectsPeople)
		{
			// people left or came home: every province's tax and work may have changed
			foreach (Country c in _countries.Values.Where(c => c.CapitalId > 0))
				c.LastLedger = EconomyRules.MonthlyLedger(c, ProvincesOf(c.Tag), _tribes.Values, _armies.Values, Date);
		}
		EmitSignal(SignalName.ProvinceChanged, provinceId);
		EmitSignal(SignalName.ArmiesChanged);
	}

	/// <summary>A day on the march for every moving army.</summary>
	void RunArmyDay()
	{
		bool moved = false;
		foreach (Army a in _armies.Values)
		{
			if (!a.Moving)
				continue;
			moved = true;
			if (++a.DaysMarched < a.StepDays)
				continue;
			a.ProvinceId = a.Path[0];
			a.Path.RemoveAt(0);
			a.DaysMarched = 0;
			a.StepDays = a.Moving ? StepDays(a, a.Path[0]) : 0;
			if (!a.Moving && a.OwnerTag == PlayerTag)
				Notify($"The {a.Name} reaches {GetProvince(a.ProvinceId)?.Name}.");
		}
		if (moved)
			EmitSignal(SignalName.ArmiesMoved);
	}

	/// <summary>Armies that lost all their regiments are gone.</summary>
	void RemoveEmptyArmies()
	{
		foreach (Army a in _armies.Values.Where(a => a.Regiments.Count == 0).ToList())
			RemoveArmy(a);
	}

	/// <summary>
	/// The standing garrisons of the country files at the start: soldiers taken from the country's people,
	/// armed as basic foot.
	/// </summary>
	void RaiseStartGarrisons(Country c)
	{
		var owned = ProvincesOf(c.Tag).ToList();
		foreach (StartProvince sp in c.Definition?.StartProvinces ?? new())
		{
			Province p = _provinces[sp.ProvinceId];
			if (p.Control == null || p.IsCore || sp.Garrison <= 0)
				continue;
			var types = Enumerable.Repeat(MilitaryRules.BasicFoot(Definitions), sp.Garrison).ToList();
			p.Control.Garrison.AddRange(MilitaryRules.Levy(c, owned, types, Date));
		}
	}
}
