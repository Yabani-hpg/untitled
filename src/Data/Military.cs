using System.Collections.Generic;
using Untitled.Core;

namespace Untitled.Data;

public enum UnitCategory
{
	/// <summary>Infantry, ranged, swordsmen, pikemen...</summary>
	Foot,
	/// <summary>Chariots, cavalry, knights, horse archers... and tanks once there are motor vehicles.</summary>
	Mounted,
	/// <summary>Rams, catapults, trebuchets, cannon, artillery: powerful but very vulnerable, manned by soldiers.</summary>
	Siege,
}

/// <summary>A kind of regiment (data/units.json).</summary>
public sealed class UnitType
{
	public string Id { get; init; }
	public string Name { get; init; }
	public UnitCategory Category { get; init; }
	public string Description { get; init; }
	/// <summary>Strength in battle; a regiment of spearmen is 1.</summary>
	public double Attack { get; init; }
	/// <summary>How well it keeps its men alive: its losses are divided by it.</summary>
	public double Defense { get; init; }
	/// <summary>Power against fortifications.</summary>
	public double Siege { get; init; }
	/// <summary>Km a day on the march.</summary>
	public double Speed { get; init; }
	/// <summary>Gold to equip a regiment when it is raised.</summary>
	public double Cost { get; init; }
	/// <summary>Gold a month while under arms.</summary>
	public double Upkeep { get; init; }
	/// <summary>When it is available (for now, a date), or null for always.</summary>
	public Condition Requires { get; set; }
	public string RequiresText { get; init; }
	/// <summary>Its share of a new country's levies, in percent.</summary>
	public int DefaultShare { get; init; }

	public override string ToString() => Id;
}

/// <summary>
/// A regiment: a unit of 1000 people taken from a population group and armed as a <see cref="UnitType"/>.
/// It remembers whom it was raised from, so the survivors go back to their homes when it is disbanded.
/// </summary>
public sealed class Regiment
{
	public UnitType Type { get; set; }
	/// <summary>Share of its 1000 still fighting, 0..1. The rest have fallen.</summary>
	public double Strength { get; set; } = 1;

	// whom it was raised from
	public int HomeProvinceId { get; init; }
	public Culture Culture { get; init; }
	public Religion Religion { get; init; }
	public Occupation Occupation { get; init; }
	public Sex Sex { get; init; }

	/// <summary>Tribesmen and nomads fight fiercely, whatever arms they are given.</summary>
	public bool Fierce => Occupation?.Nomadic == true;

	public int Men => (int)(Strength * PopGroup.PeoplePerUnit);
}

/// <summary>A country's army in the field: regiments marching together.</summary>
public sealed class Army
{
	public int Id { get; init; }
	public string OwnerTag { get; init; }
	public string Name { get; set; }
	public List<Regiment> Regiments { get; } = new();

	/// <summary>Where it stands, or the province it is leaving while on the march.</summary>
	public int ProvinceId { get; set; }
	/// <summary>The provinces still to march through, the next first; empty when it stands still.</summary>
	public List<int> Path { get; } = new();
	/// <summary>Days marched toward the next province, and the days that step takes.</summary>
	public int DaysMarched { get; set; }
	public int StepDays { get; set; }

	public bool Moving => Path.Count > 0;
	public int Destination => Path.Count > 0 ? Path[^1] : ProvinceId;

	public int Count(UnitCategory category) => Regiments.FindAll(r => r.Type.Category == category).Count;

	public int Men
	{
		get
		{
			int men = 0;
			foreach (Regiment r in Regiments)
				men += r.Men;
			return men;
		}
	}
}
