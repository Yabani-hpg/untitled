namespace Untitled.Data;

/// <summary>An institution (data/institutions.json): it appears in its year and spreads; lacking it makes technology dearer.</summary>
public sealed class Institution
{
	public string Id { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	/// <summary>The Holocene year it appears.</summary>
	public int Year { get; init; }
	public Tech OriginTech { get; init; }
}
