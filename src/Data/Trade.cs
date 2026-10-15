using System.Collections.Generic;
using Godot;

namespace Untitled.Data;

/// <summary>
/// A trade node: a region whose provinces sell their goods in one market, Paradox style. Value made in the
/// node's provinces, plus what flows in from upstream nodes, is collected by the countries with trade power
/// there, and the rest flows downstream; an end node keeps everything.
/// </summary>
public sealed class TradeNode
{
	public string Id { get; init; }
	public string Name { get; init; }
	/// <summary>The province at the node's heart, from which its reach is measured.</summary>
	public int CenterId { get; init; }
	public Color Color { get; init; }
	/// <summary>An end node: value stops here and is shared out by trade power.</summary>
	public bool End { get; init; }
	public List<TradeNode> Downstream { get; } = new();
	public List<TradeNode> Upstream { get; } = new();
	/// <summary>The provinces whose goods are sold here.</summary>
	public List<Province> Provinces { get; } = new();
}

public enum MerchantTask
{
	/// <summary>Takes the country's share of the node's value home as gold.</summary>
	Collect,
	/// <summary>Pushes the node's uncollected value down one chosen link.</summary>
	Steer,
}

/// <summary>A country's merchant at work in a node.</summary>
public sealed record Merchant(string NodeId, MerchantTask Task, string ToNodeId);
