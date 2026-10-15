using System.Collections.Generic;
using Godot;

namespace Untitled.Data;

/// <summary>A privilege the crown may grant an estate: it pleases them, strengthens them, and bends the realm's rules.</summary>
public sealed class Privilege
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	public double Influence { get; init; }
	public double Loyalty { get; init; }
	public Dictionary<string, double> Modifiers { get; } = new();
	public Estate Estate { get; set; }
}

/// <summary>An estate of the realm (data/estates.json): one of the great orders with its own influence and loyalty.</summary>
public sealed class Estate
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	/// <summary>The people it is drawn from, or null for the landed nobility.</summary>
	public Occupation Occupation { get; init; }
	public double BaseInfluence { get; init; }
	public double PerShare { get; init; }
	public Color Color { get; init; }
	public Dictionary<string, double> Loyal { get; } = new();
	public Dictionary<string, double> Disloyal { get; } = new();
	public List<Privilege> Privileges { get; } = new();
}
