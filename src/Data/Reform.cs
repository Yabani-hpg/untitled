using System.Collections.Generic;

namespace Untitled.Data;

/// <summary>A government reform: one choice of its tier, with its modifiers.</summary>
public sealed class Reform
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	public Dictionary<string, double> Modifiers { get; } = new();
	/// <summary>Government law options the reform is open to (empty: all).</summary>
	public List<string> RequiresGovernment { get; } = new();
	public ReformTier Tier { get; set; }
}

/// <summary>A tier of reforms (data/reforms.json), unlocked in order.</summary>
public sealed class ReformTier
{
	public int Index { get; init; }
	public string Id { get; init; }
	public string Name { get; init; }
	public List<Reform> Reforms { get; } = new();
}
