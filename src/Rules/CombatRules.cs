using Untitled.Data;

namespace Untitled.Rules;

/// <summary>Terrain effects on battles. Modifiers are added to the side's dice roll, Paradox style.</summary>
public static class CombatRules
{
	/// <summary>Attacking across a river that runs along the border (EU4 uses -1).</summary>
	public const int RiverCrossingAttackerModifier = -1;

	/// <summary>Dice modifier for an army attacking from <paramref name="from"/> into <paramref name="to"/>.</summary>
	public static int AttackerDiceModifier(Province from, Province to)
	{
		Adjacency link = from.GetAdjacency(to);
		return link != null && link.IsRiverCrossing ? RiverCrossingAttackerModifier : 0;
	}
}
