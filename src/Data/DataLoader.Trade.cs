using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Untitled.Data;

public static partial class DataLoader
{
	public const string TradeNodesPath = "res://data/trade_nodes.json";
	/// <summary>Crossing a sea zone counts as this many land steps when provinces are given to the nearest node.</summary>
	const int SeaStep = 3;

	/// <summary>
	/// Loads the trade nodes and gives every land province to the nearest node by land (counted in
	/// province steps from each node's centre and listed provinces); islands are listed by name.
	/// </summary>
	public static void LoadTradeNodes(Definitions defs, IReadOnlyList<Province> provinces, Dictionary<string, int> names)
	{
		var byId = new Dictionary<string, TradeNode>();
		var downstream = new Dictionary<string, List<string>>();
		var seeds = new Dictionary<string, List<int>>();
		foreach (var (where, e) in ReadJsonArray(TradeNodesPath, "nodes"))
		{
			string id = GetString(e, "id", where);
			string center = GetString(e, "center", where);
			if (!names.TryGetValue(center, out int centerId))
				throw new DataException($"{where}: unknown centre province '{center}'");
			var node = new TradeNode
			{
				Id = id,
				Name = GetString(e, "name", where),
				CenterId = centerId,
				Color = ParseHexColor(GetString(e, "color", where), where),
				End = e.TryGetProperty("end", out JsonElement end) && end.GetBoolean(),
			};
			if (byId.ContainsKey(id))
				throw new DataException($"{where}: trade node '{id}' twice");
			byId[id] = node;
			downstream[id] = GetStringList(e, "to", where).ToList();
			var list = new List<int> { centerId };
			foreach (string name in GetStringList(e, "provinces", where))
			{
				if (!names.TryGetValue(name, out int pid))
					throw new DataException($"{where}: unknown province '{name}'");
				list.Add(pid);
			}
			seeds[id] = list;
		}
		foreach (var (id, to) in downstream)
		{
			foreach (string t in to)
			{
				if (!byId.TryGetValue(t, out TradeNode other))
					throw new DataException($"{TradeNodesPath}: node '{id}' flows to unknown node '{t}'");
				byId[id].Downstream.Add(other);
				other.Upstream.Add(byId[id]);
			}
		}
		// the flow must have no loops, or value would go round for ever
		var order = TopologicalOrder(byId.Values);
		if (order.Count != byId.Count)
			throw new DataException($"{TradeNodesPath}: the trade flow has a loop");
		defs.TradeNodes.Clear();
		defs.TradeNodes.AddRange(order);

		// nearest node from every seed at once: a step by land costs 1, a step across a sea zone SeaStep
		var dist = new Dictionary<int, int>();
		var queue = new PriorityQueue<Province, int>();
		foreach (TradeNode node in order)
		{
			foreach (int pid in seeds[node.Id])
			{
				Province p = provinces[pid];
				if (p == null || dist.ContainsKey(pid))
					continue;
				dist[pid] = 0;
				p.TradeNode = node;
				queue.Enqueue(p, 0);
			}
		}
		while (queue.TryDequeue(out Province p, out int d))
		{
			if (d > dist[p.Id])
				continue;
			foreach (Adjacency link in p.Neighbors)
			{
				Province q = link.To;
				if (q == null || !(link.SharesBorder || link.IsNavigableRiver))
					continue;
				int nd = d + (q.IsWater ? SeaStep : 1);
				if (dist.TryGetValue(q.Id, out int old) && old <= nd)
					continue;
				dist[q.Id] = nd;
				q.TradeNode = p.TradeNode;
				queue.Enqueue(q, nd);
			}
		}
		int unassigned = 0;
		foreach (Province p in provinces)
		{
			if (p == null)
				continue;
			if (p.TradeNode == null)
				unassigned += p.IsWater ? 0 : 1;
			else if (!p.IsWater)
				p.TradeNode.Provinces.Add(p);
		}
		GD.Print($"{order.Count} trade nodes; {unassigned} land provinces in none");
	}

	static List<TradeNode> TopologicalOrder(IEnumerable<TradeNode> nodes)
	{
		var left = nodes.ToDictionary(n => n.Id, n => n.Upstream.Count);
		var ready = new Queue<TradeNode>(nodes.Where(n => n.Upstream.Count == 0));
		var order = new List<TradeNode>();
		while (ready.Count > 0)
		{
			TradeNode n = ready.Dequeue();
			order.Add(n);
			foreach (TradeNode d in n.Downstream)
			{
				if (--left[d.Id] == 0)
					ready.Enqueue(d);
			}
		}
		return order;
	}
}
