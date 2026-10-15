using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Ideas, Paradox style. A country takes idea groups into its slots (one at the start, another for
/// every <see cref="TechsPerSlot"/> technologies known) and buys their ideas in order with idea points,
/// the share of its research it sets aside for them. Its own national ideas unlock one for every
/// <see cref="IdeasPerNational"/> ideas bought; its traditions hold from the start and its ambition
/// comes with the last.
/// </summary>
public static class IdeaRules
{
	public const int MaxGroups = 8, TechsPerSlot = 8, IdeasPerNational = 3;
	public const double BaseCost = 300, CostStep = 60, DefaultShare = 0.3;

	public static int Slots(Country c) => Math.Min(MaxGroups, 1 + c.Techs.Count / TechsPerSlot);
	public static double Cost(Country c) => BaseCost + CostStep * c.Ideas.Count;
	public static int NationalUnlocked(Country c) => Math.Min(7, c.Ideas.Count / IdeasPerNational);
	public static Idea NextIdea(Country c, IdeaGroup g) => g.Ideas.FirstOrDefault(i => !c.Ideas.Contains(i.Id));
	public static int Bought(Country c, IdeaGroup g) => g.Ideas.Count(i => c.Ideas.Contains(i.Id));

	/// <summary>The sum of a modifier over the ideas bought, the traditions, the national ideas unlocked and the ambition.</summary>
	public static double Mod(Country c, string key, Definitions defs)
	{
		double sum = 0;
		foreach (string id in c.Ideas)
		{
			if (defs.GetIdea(id) is Idea idea)
				sum += idea.Modifiers.GetValueOrDefault(key);
		}
		NationalIdeas national = c.Definition?.NationalIdeas;
		if (national != null)
		{
			sum += national.Traditions.GetValueOrDefault(key);
			int unlocked = NationalUnlocked(c);
			for (int i = 0; i < unlocked && i < national.Ideas.Count; i++)
				sum += national.Ideas[i].Modifiers.GetValueOrDefault(key);
			if (unlocked >= 7)
				sum += national.Ambition.GetValueOrDefault(key);
		}
		return sum;
	}

	public static string Describe(IReadOnlyDictionary<string, double> mods) =>
		string.Join(", ", mods.Select(kv => $"{kv.Key.Replace('_', ' ')} {(kv.Key is "merchants" or "diplomats" or "years_to_core" or "diplomacy_opinion" or "tribe_relations" ? kv.Value.ToString("+0;-0") : kv.Value.ToString("+0%;-0%"))}"));
}
