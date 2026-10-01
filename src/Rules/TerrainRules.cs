using System;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// What a province's terrain (its biome and its relief, data/terrain.json) does: how fast armies march
/// through it, who it favours in battle, how well horses and siege engines fight there, how many soldiers
/// it can feed, and what it costs an army to stay there.
/// </summary>
public static class TerrainRules
{
	/// <summary>An army over the land's supply loses this much more of its men a month...</summary>
	public const double OverSupplyAttrition = 0.01;
	/// <summary>...plus this much for every regiment over, as a share of the supply.</summary>
	public const double OverSupplyPerRegiment = 0.04;
	public const double MaxAttrition = 0.10;
	/// <summary>Regiments a province's people can feed besides its land: one for every this many units.</summary>
	public const double UnitsPerSuppliedRegiment = 4;

	static double Mul(Province p, Func<TerrainType, double> f) => (p?.Biome is { } b ? f(b) : 1) * (p?.Relief is { } r ? f(r) : 1);

	/// <summary>Days on the march into the province, times.</summary>
	public static double MoveFactor(Province p) => Mul(p, t => t.Move);

	/// <summary>Dice for whoever defends the province: forests, jungle, hills and mountains favour them.</summary>
	public static int DefenseDice(Province p) => (p?.Biome?.Defense ?? 0) + (p?.Relief?.Defense ?? 0);

	/// <summary>Dice for the people of the province fighting on their own ground: its terrain, and knowing the desert, steppe or jungle.</summary>
	public static int HomeGroundDice(Province p) => DefenseDice(p) + (p?.Biome?.Native ?? 0);

	/// <summary>How well a kind of troops fights there.</summary>
	public static double CategoryFactor(UnitCategory category, Province p) => category switch
	{
		UnitCategory.Mounted => Mul(p, t => t.Mounted),
		UnitCategory.Siege => p?.Relief?.Siege ?? 1,
		_ => 1,
	};

	/// <summary>Regiments the province can feed: its land (biome times relief), and its people.</summary>
	public static double Supply(Province p) =>
		p == null || p.IsWater ? 0 : Mul(p, t => t.Supply) + p.TotalUnits / UnitsPerSuppliedRegiment;

	/// <summary>
	/// Share of an army's men lost in a month in the province: the land's own toll (heat, cold, fever),
	/// and hunger when the army is larger than the land can feed.
	/// </summary>
	public static double Attrition(Province p, int regiments)
	{
		if (p == null || regiments <= 0)
			return 0;
		double share = (p.Biome?.Attrition ?? 0) + (p.Relief?.Attrition ?? 0);
		double supply = Supply(p);
		if (regiments > supply)
			share += OverSupplyAttrition + OverSupplyPerRegiment * (regiments - supply) / Math.Max(1, supply);
		return Math.Min(MaxAttrition, share);
	}

	/// <summary>Grazing for nomads (0..1 and a little more), before rivers and herds.</summary>
	public static double Pasture(Province p) => p == null || p.IsWater ? 0 : Mul(p, t => t.Pasture);

	/// <summary>"Desert hills", "Temperate forest plains"...</summary>
	public static string Describe(Province p) =>
		p == null || p.IsWater ? "" : p.Biome == null ? p.Relief?.Name ?? p.Terrain
			: p.Relief == null || p.Relief.Id == "plains" ? p.Biome.Name : $"{p.Biome.Name}, {p.Relief.Name.ToLowerInvariant()}";
}
