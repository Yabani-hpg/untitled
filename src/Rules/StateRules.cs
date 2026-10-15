using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// The health of the state, Paradox style: stability (-3..+3), the ruler's legitimacy (0..100) and
/// war exhaustion (0..20). Stability steadies the tax and quiets the provinces; legitimacy comes
/// with a rightful succession and leaks away under a usurper or a lost war; exhaustion mounts with every
/// month at war and every battle, and the provinces grow restless under it. All three feed unrest.
/// </summary>
public static class StateRules
{
	public const int MinStability = -3, MaxStability = 3;
	public const double MaxExhaustion = 20;
	/// <summary>Gold to raise stability a point: this much, times (stability + 4), times the realm's size.</summary>
	public const double StabilityCostBase = 20;
	/// <summary>Gold to lower war exhaustion by one.</summary>
	public const double ExhaustionReliefCost = 30;
	/// <summary>Exhaustion a month at war, and its fall a month at peace.</summary>
	public const double ExhaustionPerMonth = 0.4, ExhaustionRecovery = 0.25;
	/// <summary>Exhaustion for a battle, by the share of the country's men lost.</summary>
	public const double ExhaustionPerBattle = 2;
	/// <summary>Legitimacy regained a year under a rightful ruler.</summary>
	public const double LegitimacyPerYear = 2;

	/// <summary>Gold to raise stability one point.</summary>
	public static double StabilityCost(Country c, int provinces) =>
		StabilityCostBase * (c.Stability + 4) * Math.Max(1, Math.Sqrt(provinces));

	public static bool CanRaiseStability(Country c, int provinces, out string reason)
	{
		reason = null;
		if (c.Stability >= MaxStability)
			reason = "Stability is as high as it goes";
		else if (c.Gold < StabilityCost(c, provinces))
			reason = $"Costs {StabilityCost(c, provinces):0} gold; we have {c.Gold:0}";
		return reason == null;
	}

	public static void ChangeStability(Country c, int by) => c.Stability = Math.Clamp(c.Stability + by, MinStability, MaxStability);

	/// <summary>Multiplier on tax: each point of stability 5%, and a ruler nobody believes in loses some.</summary>
	public static double TaxFactor(Country c) =>
		c == null ? 1 : Math.Max(0.3, 1 + 0.05 * c.Stability - 0.2 * (1 - c.Legitimacy / 100));

	/// <summary>
	/// Multiplier on the chance of uprisings: stability quiets the land, exhaustion and an illegitimate
	/// ruler stir it. Stability -3 and 20 exhaustion make uprisings four times as likely.
	/// </summary>
	public static double UnrestFactor(Country c) =>
		c == null ? 1 : Math.Max(0.1, 1 - 0.2 * c.Stability + 0.1 * c.WarExhaustion + 0.5 * (1 - c.Legitimacy / 100));

	/// <summary>Chance a core province rises in a month, from unrest alone (nothing at a quiet, stable realm).</summary>
	public static double CoreUprisingChance(Country c)
	{
		double unrest = UnrestFactor(c) - 1;        // 0 when all is well
		return unrest <= 0.5 ? 0 : 0.004 * (unrest - 0.5);
	}

	/// <summary>A month: exhaustion mounts at war and fades at peace; legitimacy grows back.</summary>
	public static void MonthlyStep(Country c, bool atWar)
	{
		c.WarExhaustion = Math.Clamp(c.WarExhaustion + (atWar ? ExhaustionPerMonth : -ExhaustionRecovery), 0, MaxExhaustion);
		c.Legitimacy = Math.Min(100, c.Legitimacy + LegitimacyPerYear / 12);
	}

	/// <summary>The toll of a battle on exhaustion, by the share of the country's men that fell.</summary>
	public static void BattleExhaustion(Country c, int lost, int menUnderArms)
	{
		if (menUnderArms <= 0)
			return;
		c.WarExhaustion = Math.Min(MaxExhaustion, c.WarExhaustion + ExhaustionPerBattle * Math.Min(1, (double)lost / menUnderArms));
	}

	/// <summary>The legitimacy a new ruler starts with, by how the throne came to them.</summary>
	public static double SuccessionLegitimacy(string succession, bool newDynasty) => succession switch
	{
		"elected" => 80,
		"chosen_by_priests" => newDynasty ? 70 : 90,
		_ => newDynasty ? 50 : 90,
	};

	public static string StabilityText(int s) => s switch
	{
		<= -3 => "Chaos",
		-2 => "Turmoil",
		-1 => "Unsettled",
		0 => "Steady",
		1 => "Settled",
		2 => "Prosperous",
		_ => "Golden age",
	};
}
