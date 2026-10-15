using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>One node's month: what it was worth, who had power there, and who took what.</summary>
public sealed class NodeReport
{
	public TradeNode Node { get; init; }
	/// <summary>Value made by the node's own provinces.</summary>
	public double Local { get; set; }
	/// <summary>Value that flowed in from upstream.</summary>
	public double Incoming { get; set; }
	public double Total => Local + Incoming;
	/// <summary>Trade power of each country in the node.</summary>
	public Dictionary<string, double> Power { get; } = new();
	public double TotalPower => Power.Values.Sum();
	/// <summary>Gold taken out by each collecting country (before the income factor).</summary>
	public Dictionary<string, double> Collected { get; } = new();
	/// <summary>Value sent down each outgoing link, by the downstream node's id.</summary>
	public Dictionary<string, double> Forwarded { get; } = new();
	public double Share(string tag) => TotalPower > 0 ? Power.GetValueOrDefault(tag) / TotalPower : 0;
}

/// <summary>The world's trade this month.</summary>
public sealed class TradeReport
{
	public Dictionary<string, NodeReport> Nodes { get; } = new();
	/// <summary>Each province's trade value this month.</summary>
	public Dictionary<int, double> ProvinceValue { get; } = new();
	/// <summary>Gold a month each country makes from trade.</summary>
	public Dictionary<string, double> Income { get; } = new();
	/// <summary>Gold a month each country makes from its provinces' production.</summary>
	public Dictionary<string, double> Production { get; } = new();
	public NodeReport Of(TradeNode node) => node == null ? null : Nodes.GetValueOrDefault(node.Id);
}

/// <summary>
/// Trade, Paradox style. Every province's goods (its crops' surplus, what its mines dig and its
/// workshops make, at the going prices) are its trade value, sold in its trade node. Countries have trade
/// power in a node from the provinces they hold there and the merchants they send; those collecting there
/// (in their home node, or with a merchant collecting) take the node's value in proportion to their power,
/// and the rest flows downstream to the next nodes, steered by merchants, until an end node keeps it.
/// Owners also earn a share of their provinces' production directly.
/// </summary>
public static class TradeRules
{
	/// <summary>Trade value collected turns into this much gold.</summary>
	public const double TradeIncomeFactor = 0.08;
	/// <summary>A province's owner earns this share of its trade value as production income.</summary>
	public const double ProductionIncomeFactor = 0.05;
	/// <summary>Share of food made that is surplus for sale.</summary>
	public const double FoodSurplusShare = EconomyRules.FoodSurplusShare;
	/// <summary>Trade power a merchant brings.</summary>
	public const double MerchantPower = 5;
	/// <summary>Every country has this many merchants, plus those its techs and laws give.</summary>
	public const int BaseMerchants = 1;
	/// <summary>Value steered down a link gains this much for every merchant steering it.</summary>
	public const double SteerBonus = 0.05;
	/// <summary>Share of a node's uncollected value that reaches the next nodes; the rest is lost on the road.</summary>
	public const double TransitShare = 0.5;
	/// <summary>A province no settled country owns sells only this share of its goods.</summary>
	public const double UnsettledShare = 0.25;

	public static int MerchantsOf(Country c) => Math.Max(0, BaseMerchants + (int)Math.Round(TechRules.Mod(c, "merchants") + LawRules.Mod(c, "merchants")));

	/// <summary>A province's trade value: goods made this month at the going prices.</summary>
	public static double ProvinceValue(Province p, Definitions defs)
	{
		if (p == null || p.IsWater || p.TotalUnits <= 0)
			return 0;
		ProductionReport r = ProductionRules.Compute(p, defs);
		double v = 0;
		if (p.Food != null)
			v += r.FoodOutput * FoodSurplusShare * p.Food.Price;
		foreach (var (res, net) in r.Net)
		{
			if (net > 0 && res.Category == ResourceCategory.Produced)
				v += net * res.Price;
		}
		if (p.NonRenewable != null)
			v += r.Extracted * p.NonRenewable.Price;
		if (p.OccupierTag != null)
			v *= 0.5;                    // an occupied province's markets are half shut
		if (p.OwnerTag == null)
			v *= UnsettledShare;
		return v;
	}

	/// <summary>A province's trade power for its owner: its people, its coast and river, its capital.</summary>
	public static double ProvincePower(Province p, Country owner)
	{
		if (p.OccupierTag != null)
			return 0;
		double power = 1 + p.TotalUnits * 0.1;
		if (p.HasFeature("coast")) power += 1;
		if (p.HasFeature("river")) power += 0.5;
		if (owner.CapitalId == p.Id) power += 2;
		return power * Math.Max(0.1, 1 + TechRules.Mod(owner, "trade_power") + LawRules.Mod(owner, "trade_power") + WorkRules.Mod(p, "trade_power"));
	}

	/// <summary>The node a country collects in without a merchant: its capital's.</summary>
	public static TradeNode HomeNode(Country c, IReadOnlyList<Province> provinces) =>
		c.CapitalId >= 0 && c.CapitalId < provinces.Count ? provinces[c.CapitalId]?.TradeNode : null;

	/// <summary>Whether a country can send a merchant to a node: it needs power there, or in a node next to it.</summary>
	public static bool InReach(Country c, TradeNode node, TradeReport report)
	{
		if (report?.Of(node)?.Power.GetValueOrDefault(c.Tag) > 0)
			return true;
		return node.Upstream.Concat(node.Downstream).Any(n => report?.Of(n)?.Power.GetValueOrDefault(c.Tag) > 0);
	}

	/// <summary>The month's trade, nodes in flow order.</summary>
	public static TradeReport Compute(IReadOnlyList<TradeNode> nodes, IReadOnlyList<Province> provinces, IReadOnlyDictionary<string, Country> countries, Definitions defs, IEnumerable<Army> armies = null)
	{
		var report = new TradeReport();
		var home = new Dictionary<string, TradeNode>();
		foreach (Country c in countries.Values)
		{
			if (HomeNode(c, provinces) is TradeNode h)
				home[c.Tag] = h;
		}
		foreach (TradeNode node in nodes)
		{
			var r = new NodeReport { Node = node };
			report.Nodes[node.Id] = r;
			foreach (Province p in node.Provinces)
			{
				double v = ProvinceValue(p, defs);
				if (v > 0)
					report.ProvinceValue[p.Id] = v;
				r.Local += v;
				if (p.OwnerTag != null && countries.TryGetValue(p.OwnerTag, out Country owner))
				{
					report.Production[p.OwnerTag] = report.Production.GetValueOrDefault(p.OwnerTag) + v * ProductionIncomeFactor;
					double power = ProvincePower(p, owner);
					if (power > 0)
						r.Power[p.OwnerTag] = r.Power.GetValueOrDefault(p.OwnerTag) + power;
				}
			}
		}
		// merchants add power where they work
		foreach (Country c in countries.Values)
		{
			foreach (Merchant m in c.Merchants)
			{
				if (report.Nodes.TryGetValue(m.NodeId, out NodeReport r))
					r.Power[c.Tag] = r.Power.GetValueOrDefault(c.Tag) + MerchantPower * Math.Max(0.1, 1 + TechRules.Mod(c, "trade_power") + LawRules.Mod(c, "trade_power"));
			}
		}
		// ships at sea protect the trade of the node their sea lies in
		foreach (Army f in armies ?? Enumerable.Empty<Army>())
		{
			if (!f.IsFleet || f.ProvinceId < 0 || f.ProvinceId >= provinces.Count || provinces[f.ProvinceId]?.TradeNode is not TradeNode at || !countries.TryGetValue(f.OwnerTag, out Country o))
				continue;
			if (report.Nodes.TryGetValue(at.Id, out NodeReport r))
				r.Power[f.OwnerTag] = r.Power.GetValueOrDefault(f.OwnerTag) + NavalRules.TradePower(f) * Math.Max(0.1, 1 + TechRules.Mod(o, "trade_power") + LawRules.Mod(o, "trade_power"));
		}
		// trade power ceded by treaty: half of the loser's power in every node goes to the winner
		foreach (Country c in countries.Values)
		{
			foreach (var (toTag, _) in c.TradePowerCeded)
			{
				if (!countries.ContainsKey(toTag))
					continue;
				foreach (NodeReport r in report.Nodes.Values)
				{
					if (!r.Power.TryGetValue(c.Tag, out double power) || power <= 0)
						continue;
					double moved = power * WarRules.CededTradeShare;
					r.Power[c.Tag] = power - moved;
					r.Power[toTag] = r.Power.GetValueOrDefault(toTag) + moved;
				}
			}
		}
		// value flows downstream, node by node
		foreach (TradeNode node in nodes)
		{
			NodeReport r = report.Nodes[node.Id];
			double total = r.Total, totalPower = r.TotalPower;
			bool terminal = node.End || node.Downstream.Count == 0;
			double collectPower = 0;
			var collectors = new List<string>();
			foreach (var (tag, power) in r.Power)
			{
				bool collects = terminal || home.GetValueOrDefault(tag) == node
					|| countries[tag].Merchants.Any(m => m.NodeId == node.Id && m.Task == MerchantTask.Collect);
				if (collects)
				{
					collectors.Add(tag);
					collectPower += power;
				}
			}
			double collected = totalPower > 0 ? total * collectPower / totalPower : 0;
			foreach (string tag in collectors)
			{
				double gold = collected * r.Power[tag] / collectPower;
				r.Collected[tag] = gold;
				report.Income[tag] = report.Income.GetValueOrDefault(tag) + gold * TradeIncomeFactor * Math.Max(0.1, 1 + LawRules.Mod(countries[tag], "trade_income"));
			}
			if (terminal)
				continue;
			double rest = (total - collected) * TransitShare;
			var weights = new Dictionary<string, double>();
			var steerers = new Dictionary<string, int>();
			foreach (TradeNode d in node.Downstream)
			{
				weights[d.Id] = 1;
				steerers[d.Id] = 0;
			}
			foreach (var (tag, power) in r.Power)
			{
				foreach (Merchant m in countries[tag].Merchants)
				{
					if (m.NodeId == node.Id && m.Task == MerchantTask.Steer && m.ToNodeId != null && weights.ContainsKey(m.ToNodeId))
					{
						weights[m.ToNodeId] += power;
						steerers[m.ToNodeId]++;
					}
				}
			}
			double sum = weights.Values.Sum();
			foreach (TradeNode d in node.Downstream)
			{
				double sent = rest * weights[d.Id] / sum * (1 + SteerBonus * steerers[d.Id]);
				r.Forwarded[d.Id] = sent;
				report.Nodes[d.Id].Incoming += sent;
			}
		}
		return report;
	}
}
