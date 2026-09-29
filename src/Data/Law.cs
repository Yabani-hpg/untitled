using System.Collections.Generic;

namespace Untitled.Data;

/// <summary>A law of a settled country (succession, citizenship, religious law...): one of its options is in force.</summary>
public sealed class LawDefinition
{
	public string Id { get; init; }
	public string Name { get; init; }
	/// <summary>Heading the law is listed under ("Crown", "People"...).</summary>
	public string Group { get; init; }
	public string Description { get; init; }
	public List<LawOption> Options { get; } = new();
	public LawOption Default { get; set; }

	public LawOption GetOption(string id) => Options.Find(o => o.Id == id);
}

public sealed class LawOption
{
	public LawDefinition Law { get; init; }
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	/// <summary>Numbers the option changes (tax, uprising...), see data/laws.json.</summary>
	public Dictionary<string, double> Modifiers { get; } = new();
	/// <summary>Ongoing effects: numbers (assimilation, conversion) and choices (succession, citizens).</summary>
	public Dictionary<string, double> Effects { get; } = new();
	public Dictionary<string, string> Choices { get; } = new();
	/// <summary>What the country must meet to adopt it, or null.</summary>
	public Condition Requires { get; set; }
	public string RequiresText { get; set; }
	/// <summary>Technologies its requirement names (for showing what a technology opens).</summary>
	public List<string> Techs { get; } = new();
}
