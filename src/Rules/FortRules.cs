using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Forts, Paradox style. A province's walls (level 1 to 3) make its sieges take longer, give its
/// defenders dice in battle, and throw a zone of control over the provinces around it: an enemy army
/// may stop in them but not march through them while the fort stands unoccupied.
/// </summary>
public static class FortRules
{
	public const int MaxLevel = 3;
	public static readonly string[] Names = { "No walls", "Walls", "Citadel", "Fortress" };
	public static readonly string[] Techs = { null, "masonry", "engineering", "mechanics" };
	public static readonly double[] Cost = { 0, 60, 150, 300 };
	/// <summary>Gold a month for each level of fort.</summary>
	public const double UpkeepPerLevel = 0.3;

	/// <summary>Siege days are multiplied by this.</summary>
	public static double SiegeFactor(Province p) => (1 + (p?.FortLevel ?? 0)) * (p == null ? 1 : System.Math.Max(0.1, 1 + WorkRules.Mod(p, "defense")));

	/// <summary>Dice for the province's own side defending behind its walls.</summary>
	public static int DefenceDice(Province p) => p?.FortLevel ?? 0;

	public static string Describe(Province p) => p.FortLevel <= 0 ? "No walls"
		: $"{Names[p.FortLevel]}: sieges take {SiegeFactor(p):0}× as long, its defenders roll +{DefenceDice(p)}, and enemies may not march through the provinces around it";
}
