using System.Collections.Generic;

namespace Untitled.Data;

/// <summary>A disaster (data/disasters.json): it brews while its trigger holds, strikes at full progress, and lasts until its end condition has held long enough.</summary>
public sealed class Disaster
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	public Condition Trigger { get; init; }
	public Condition End { get; init; }
	public double ProgressPerMonth { get; init; } = 2;
	public double DeclinePerMonth { get; init; } = 1;
	public int MinYears { get; init; } = 1;
	public Effects OnStart { get; init; } = new();
	public Effects OnEnd { get; init; } = new();
	public Dictionary<string, double> Modifiers { get; } = new();
}
