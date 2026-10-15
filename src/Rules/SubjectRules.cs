using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// What each kind of subject owes and gets. A vassal pays a share of its tax; a tributary a smaller one; a march
/// nothing, but it holds the border with more levies and a stronger defence. Marches and tributaries can't be annexed.
/// </summary>
public static class SubjectRules
{
	/// <summary>A march's bonuses: a bigger share of its people answer the levy (on a base of a tenth), and it is stronger in defence.</summary>
	public const double MarchLevies = 0.03, MarchDefense = 0.2;
	/// <summary>Opinion a vassal must have of its overlord to be made a march; what revoking it costs; what release earns.</summary>
	public const int MarchOpinionNeeded = 50, RevokeMarchOpinion = -30, ReleaseOpinion = 50;
	/// <summary>A tributary run by the game breaks free when this much stronger than its overlord and ill-disposed; the monthly chance.</summary>
	public const double BreakFreeStrength = 1.2, BreakFreeChance = 0.05;

	public static double TributeShare(SubjectKind kind) => kind switch
	{
		SubjectKind.March => 0,
		SubjectKind.Tributary => WarRules.TributaryShare,
		_ => WarRules.TributeShare,
	};

	public static string Word(SubjectKind kind) => kind switch
	{
		SubjectKind.March => "march",
		SubjectKind.Tributary => "tributary",
		_ => "vassal",
	};

	/// <summary>A subject's modifiers: a march's levies and defence.</summary>
	public static double Mod(Country c, string key)
	{
		if (c?.OverlordTag == null || c.Subject != SubjectKind.March)
			return 0;
		return key switch
		{
			"levy_share" => MarchLevies,
			"army_defense" => MarchDefense,
			_ => 0,
		};
	}
}
