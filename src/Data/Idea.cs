using System.Collections.Generic;

namespace Untitled.Data;

/// <summary>One idea of a group: a modifier the country gains when it buys it.</summary>
public sealed class Idea
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	public Dictionary<string, double> Modifiers { get; } = new();
	public IdeaGroup Group { get; set; }
}

/// <summary>An idea group (data/ideas.json): seven ideas bought in order.</summary>
public sealed class IdeaGroup
{
	public string Id { get; init; }
	public string Name { get; init; }
	public TechCategory Category { get; init; }
	public string Description { get; init; }
	public List<Idea> Ideas { get; } = new();
}

/// <summary>A country's own ideas (its country file): traditions from the start, seven ideas unlocked as it buys ideas, and an ambition at the end.</summary>
public sealed class NationalIdeas
{
	public Dictionary<string, double> Traditions { get; } = new();
	public List<Idea> Ideas { get; } = new();
	public Dictionary<string, double> Ambition { get; } = new();
}
