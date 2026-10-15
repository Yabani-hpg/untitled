using System.Collections.Generic;

namespace Untitled.Data;

/// <summary>
/// What an event option, a decision or a mission does to a country (data/events.json effects): gold,
/// stability, prestige, legitimacy, war_exhaustion, research (points to the stockpile), set_flag /
/// clear_flag (country flags), set_world_flag, claims (province names), opinion ({tag or "neighbours":
/// amount}), capital_pops (units), ruler_dies, tech (learned), levies (free regiments), message.
/// </summary>
public sealed class Effects
{
	public double Gold { get; init; }
	public int Stability { get; init; }
	public double Prestige { get; init; }
	public double Legitimacy { get; init; }
	public double WarExhaustion { get; init; }
	public double Research { get; init; }
	public List<string> SetFlags { get; } = new();
	public List<string> ClearFlags { get; } = new();
	public List<string> SetWorldFlags { get; } = new();
	public List<int> Claims { get; } = new();
	public Dictionary<string, int> Opinion { get; } = new();
	public int CapitalPops { get; init; }
	public bool RulerDies { get; init; }
	public string Tech { get; init; }
	public int Levies { get; init; }
	public string Message { get; init; }
}

public sealed class EventOption
{
	public string Text { get; init; }
	public Effects Effects { get; init; }
	/// <summary>How likely a country run by the game is to take it.</summary>
	public double AiWeight { get; init; } = 1;
}

/// <summary>Something that happens to a country when its trigger holds (data/events.json), with choices.</summary>
public sealed class GameEvent
{
	public string Id { get; init; }
	public string Title { get; init; }
	public string Text { get; init; }
	public Condition Trigger { get; set; }
	/// <summary>Mean years until it fires once the trigger holds (0: at once).</summary>
	public double MeanTimeYears { get; init; } = 5;
	/// <summary>Fires only once per country.</summary>
	public bool Once { get; init; } = true;
	public List<EventOption> Options { get; } = new();
}

/// <summary>A choice a country can make whenever its requirements hold (data/decisions.json).</summary>
public sealed class Decision
{
	public string Id { get; init; }
	public string Title { get; init; }
	public string Text { get; init; }
	/// <summary>Shown to countries meeting this (null: everyone).</summary>
	public Condition Visible { get; set; }
	public Condition Requires { get; set; }
	public string RequiresText { get; init; }
	public Effects Effects { get; init; }
	public bool Once { get; init; } = true;
}

/// <summary>A goal set for a country (its country file's "missions"): done when its condition holds, for a reward.</summary>
public sealed class Mission
{
	public string Id { get; init; }
	public string Title { get; init; }
	public string Text { get; init; }
	public Condition Requires { get; set; }
	public string RequiresText { get; init; }
	public Effects Reward { get; init; }
	/// <summary>A mission that must be done first, or null.</summary>
	public string After { get; init; }
}
