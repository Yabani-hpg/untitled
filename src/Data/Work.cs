using System.Collections.Generic;

namespace Untitled.Data;

/// <summary>A public work (data/works.json): a building the government raises in a province for gold, giving it a lasting modifier.</summary>
public sealed class Work
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	public double Cost { get; init; }
	/// <summary>The technology needed, or null.</summary>
	public Tech Tech { get; init; }
	/// <summary>The work this one replaces, or null.</summary>
	public Work Upgrades { get; init; }
	/// <summary>The province needs any one of these features (none: any province).</summary>
	public IReadOnlyList<string> RequiredFeatures { get; init; }
	public IReadOnlyDictionary<string, double> Modifiers { get; init; }

	public override string ToString() => Id;
}
