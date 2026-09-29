using System.Collections.Generic;

namespace Untitled.Data;

public enum TechCategory
{
	/// <summary>Weapons, tactics and the philosophy of war: horseback riding, bronze and iron working...</summary>
	Military,
	/// <summary>Governing the realm: forms of government, taxation, currency, codes of law...</summary>
	Admin,
	/// <summary>Inventions and learning: writing, priesthood, the calendar...</summary>
	Science,
}

/// <summary>
/// An advance a country can research (data/techs.json). Research points of its category flow into it
/// once the country meets its requirements (the techs it builds on, and any other condition).
/// </summary>
public sealed class Tech
{
	public string Id { get; init; }
	public string Name { get; init; }
	public TechCategory Category { get; init; }
	public string Description { get; init; }
	/// <summary>Research points to discover it.</summary>
	public double Cost { get; init; }
	/// <summary>Techs that must be known first (of any category).</summary>
	public List<Tech> Requires { get; } = new();
	/// <summary>Another condition the country must meet, and its words; or null.</summary>
	public Condition Condition { get; set; }
	public string ConditionText { get; init; }
	/// <summary>Changes to the country's numbers once known (the same keys as laws' modifiers).</summary>
	public Dictionary<string, double> Modifiers { get; } = new();
	/// <summary>Every settled country knows it from the start.</summary>
	public bool KnownAtStart { get; init; }

	public override string ToString() => Id;

	public static TechCategory ParseCategory(string text, string where) => text switch
	{
		"military" => TechCategory.Military,
		"admin" => TechCategory.Admin,
		"science" => TechCategory.Science,
		_ => throw new DataException($"{where}: research category must be military, admin or science, not '{text}'"),
	};
}
