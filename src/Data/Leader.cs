using Untitled.Core;

namespace Untitled.Data;

public enum LeaderKind { General, Admiral }

/// <summary>
/// A general or admiral, Paradox style: four skills of 0 to 6 pips. Fire and shock sharpen the dice in battle,
/// manoeuvre spares the men on the march, siege hastens sieges. Recruited for gold; the better the country's
/// military tradition, the better the leaders it finds.
/// </summary>
public sealed class Leader
{
	public int Id { get; init; }
	public string Name { get; set; }
	public string OwnerTag { get; init; }
	public LeaderKind Kind { get; init; }
	public int Fire { get; set; }
	public int Shock { get; set; }
	public int Manoeuvre { get; set; }
	public int Siege { get; set; }
	public GameDate Birth { get; set; }

	/// <summary>What the leader adds to the dice in battle: fire and shock together, rounded up.</summary>
	public int BattlePips => (Fire + Shock + 1) / 2;
	public int TotalPips => Fire + Shock + Manoeuvre + Siege;
	public string Pips => $"{Fire}/{Shock}/{Manoeuvre}/{Siege}";

	public int AgeOn(GameDate date)
	{
		var (by, bm, bd) = Birth.Holocene;
		var (y, m, d) = date.Holocene;
		int age = y - by;
		if (m < bm || m == bm && d < bd)
			age--;
		return age;
	}
}
