namespace Untitled.Data;

/// <summary>
/// A link between two provinces, seen from one side (<see cref="From"/>). Both provinces hold a copy
/// pointing at the other, so neighbour lists can be walked without looking up pairs.
/// </summary>
public sealed class Adjacency
{
	public Province From { get; }
	public Province To { get; }

	/// <summary>Shared border length in map pixels. 0 when the provinces touch only through a navigable river mouth.</summary>
	public int BorderLength { get; }

	/// <summary>River running along the border, or null. Armies attacking across it are penalised.</summary>
	public string CrossingRiver { get; }

	/// <summary>Navigable river connecting the two provinces, or null. Small ships can sail between them.</summary>
	public string NavigableRiver { get; }

	public bool SharesBorder => BorderLength > 0;
	public bool IsRiverCrossing => CrossingRiver != null;
	public bool IsNavigableRiver => NavigableRiver != null;

	public Adjacency(Province from, Province to, int borderLength, string crossingRiver, string navigableRiver)
	{
		From = from;
		To = to;
		BorderLength = borderLength;
		CrossingRiver = crossingRiver;
		NavigableRiver = navigableRiver;
	}

	public Adjacency Reversed() => new(To, From, BorderLength, CrossingRiver, NavigableRiver);
}
