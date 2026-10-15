using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Trade: the month's trade report, and where the player's and the game's merchants go.
public partial class GameState
{
	/// <summary>The trade report changed: a month passed or a merchant moved.</summary>
	[Signal]
	public delegate void TradeChangedEventHandler();

	/// <summary>The world's trade as last computed (each month, and whenever a merchant moves).</summary>
	public TradeReport Trade { get; private set; }

	public TradeNode HomeNode(Country c) => TradeRules.HomeNode(c, _provinces);
	public int MerchantsOf(Country c) => TradeRules.MerchantsOf(c);
	public int FreeMerchants(Country c) => MerchantsOf(c) - c.Merchants.Count;
	public TradeNode GetTradeNode(string id) => Definitions.TradeNodes.Find(n => n.Id == id);

	public void RecomputeTrade()
	{
		Trade = TradeRules.Compute(Definitions.TradeNodes, _provinces, _countries, Definitions);
		EmitSignal(SignalName.TradeChanged);
	}

	/// <summary>Why a country can't send a merchant to a node on a task, or null.</summary>
	public string MerchantObstacle(Country c, TradeNode node, MerchantTask task, TradeNode to)
	{
		if (node == null)
			return "No such trade node";
		if (task == MerchantTask.Steer && (to == null || !node.Downstream.Contains(to)))
			return "Trade can only be steered down one of the node's links";
		if (task == MerchantTask.Steer && node.End)
			return "Nothing flows out of an end node";
		if (!TradeRules.InReach(c, node, Trade))
			return "Out of our reach: we need trade power there or in a neighbouring node";
		if (!c.Merchants.Any(m => m.NodeId == node.Id) && FreeMerchants(c) <= 0)
			return "All our merchants are at work";
		return null;
	}

	/// <summary>Sends a merchant to a node (replacing one already there), or says why not.</summary>
	public bool PlaceMerchant(string tag, string nodeId, MerchantTask task, string toId, out string reason)
	{
		Country c = GetCountry(tag);
		TradeNode node = GetTradeNode(nodeId), to = toId == null ? null : GetTradeNode(toId);
		reason = c == null ? "No such country" : MerchantObstacle(c, node, task, to);
		if (reason != null)
			return false;
		c.Merchants.RemoveAll(m => m.NodeId == nodeId);
		c.Merchants.Add(new Merchant(nodeId, task, task == MerchantTask.Steer ? toId : null));
		RecomputeTrade();
		return true;
	}

	public void RecallMerchant(string tag, string nodeId)
	{
		Country c = GetCountry(tag);
		if (c == null || c.Merchants.RemoveAll(m => m.NodeId == nodeId) == 0)
			return;
		RecomputeTrade();
	}

	/// <summary>Gold a month the country makes from trade and from its production.</summary>
	public (double Trade, double Production) TradeIncomeOf(Country c) =>
		(Trade?.Income.GetValueOrDefault(c.Tag) ?? 0, Trade?.Production.GetValueOrDefault(c.Tag) ?? 0);

	/// <summary>
	/// A country run by the game sends its idle merchants where they earn most: to collect in the richest
	/// nodes it has power in besides home, else to steer the node upstream of home toward home.
	/// </summary>
	void AiTrade(Country c)
	{
		if (Trade == null || FreeMerchants(c) <= 0 && _rng.NextDouble() > 0.05)
			return;
		TradeNode home = HomeNode(c);
		if (home == null)
			return;
		// drop merchants whose node we lost all power in
		c.Merchants.RemoveAll(m => !TradeRules.InReach(c, GetTradeNode(m.NodeId), Trade));
		var best = new List<(double Gain, TradeNode Node, MerchantTask Task, TradeNode To)>();
		foreach (TradeNode node in Definitions.TradeNodes)
		{
			if (c.Merchants.Any(m => m.NodeId == node.Id) || !TradeRules.InReach(c, node, Trade))
				continue;
			NodeReport r = Trade.Of(node);
			double power = r.Power.GetValueOrDefault(c.Tag) + TradeRules.MerchantPower;
			if (node != home)
				best.Add((r.Total * power / (r.TotalPower + TradeRules.MerchantPower), node, MerchantTask.Collect, null));
			if (node.Downstream.Contains(home))
				best.Add((r.Total * 0.5 * Trade.Of(home).Share(c.Tag), node, MerchantTask.Steer, home));
		}
		foreach (var pick in best.OrderByDescending(b => b.Gain))
		{
			if (FreeMerchants(c) <= 0)
				break;
			if (pick.Gain <= 0.01)
				break;
			c.Merchants.Add(new Merchant(pick.Node.Id, pick.Task, pick.To?.Id));
		}
	}
}
