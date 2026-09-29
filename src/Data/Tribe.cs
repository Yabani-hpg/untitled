using System.Collections.Generic;
using Untitled.Core;

namespace Untitled.Data;

public enum TribeKind
{
	/// <summary>Settled villagers with no state: they farm the same land year after year.</summary>
	Tribal,
	/// <summary>Herders who roam with their flocks; their camp moves across their lands.</summary>
	Nomadic,
}

/// <summary>
/// An unsettled country: a tribe or nomad horde living in land no settled country controls. It has
/// people, warriors and a chief, but no borders, map colour or flag. Fierce in battle, it can be won as
/// an ally by a settled country, hired as mercenaries, and in the end absorbed into it; nomads can also
/// be subjugated by force.
/// </summary>
public sealed class Tribe
{
	public int Id { get; init; }
	/// <summary>Key of a tribe from data/tribes.json ("libu"), or null for a generated one.</summary>
	public string Key { get; init; }
	public string Name { get; set; }
	public TribeKind Kind { get; set; }
	public Culture Culture { get; set; }
	public Religion Religion { get; set; }
	public Character Chief { get; set; }

	/// <summary>The provinces its people live in: its lands, with no borders drawn.</summary>
	public List<int> Provinces { get; } = new();
	/// <summary>Where its chief's camp is now. Nomads move it around their lands, and beyond.</summary>
	public int CampProvinceId { get; set; }

	/// <summary>Opinion of each settled country, -100..100.</summary>
	public Dictionary<string, int> Relations { get; } = new();
	/// <summary>The settled country it is allied with, and since when.</summary>
	public string AlliedTag { get; set; }
	public GameDate AlliedSince { get; set; }
	/// <summary>A country it turned down, which may ask again after <see cref="RefusedUntil"/>.</summary>
	public string RefusedTag { get; set; }
	public GameDate RefusedUntil { get; set; }
	/// <summary>Regiments of its warriors fighting for its ally as mercenaries.</summary>
	public int HiredRegiments { get; set; }

	/// <summary>The treasury of an unsettled people: stored food, which feeds its war bands and is bartered with others.</summary>
	public double Food { get; set; }
	/// <summary>Last month's food accounts.</summary>
	public Untitled.Rules.FoodLedger LastFood { get; set; }

	public bool IsNomadic => Kind == TribeKind.Nomadic;

	/// <summary>"the Libu"</summary>
	public string TheName => $"the {Name}";

	public int RelationWith(string tag) => tag != null && Relations.TryGetValue(tag, out int r) ? r : 0;
}
