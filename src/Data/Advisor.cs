using Untitled.Core;

namespace Untitled.Data;

/// <summary>A kind of advisor (data/advisors.json): its modifier, times the advisor's skill.</summary>
public sealed class AdvisorType
{
	public string Id { get; init; }
	public string Name { get; init; }
	/// <summary>admin, diplomatic or military: one of each may serve at a time.</summary>
	public string Category { get; init; }
	public string Description { get; init; }
	public string Modifier { get; init; }
	public double Value { get; init; }
}

/// <summary>An advisor at court or waiting to be hired.</summary>
public sealed class Advisor
{
	public int Id { get; init; }
	public string Name { get; init; }
	public string Portrait { get; init; }
	public AdvisorType Type { get; init; }
	/// <summary>1 to 3.</summary>
	public int Skill { get; init; }
	public GameDate Birth { get; init; }
	public int AgeOn(GameDate date) => (int)((date.Day - Birth.Day) / 365.2425);
}
