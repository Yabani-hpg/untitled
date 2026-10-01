using System;
using System.Collections.Generic;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>Kinship between cultures, and the migration of nomadic populations.</summary>
public static class PopulationRules
{
	// --- Culture affinity ---

	public const float SameCultureAffinity = 1.0f;
	public const float SameLanguageAffinity = 0.75f;
	public const float SameFamilyAffinity = 0.4f;

	/// <summary>
	/// How much populations of two cultures feel they have in common, 0..1: they share the culture, or at
	/// least the language, or at least the language family.
	/// </summary>
	public static float Affinity(Culture a, Culture b)
	{
		if (a == null || b == null)
			return 0f;
		if (a == b)
			return SameCultureAffinity;
		if (a.Language == b.Language)
			return SameLanguageAffinity;
		if (a.Language.Family == b.Language.Family)
			return SameFamilyAffinity;
		return 0f;
	}

	/// <summary>"Same culture", "Same language", "Same language family" or "Foreign".</summary>
	public static string AffinityName(Culture a, Culture b) => Affinity(a, b) switch
	{
		>= SameCultureAffinity => "Same culture",
		>= SameLanguageAffinity => "Same language",
		>= SameFamilyAffinity => "Same language family",
		_ => "Foreign",
	};

	// --- Pasture and migration ---

	/// <summary>Share of a province's nomads that may leave in one month.</summary>
	public const float MigrationShare = 0.1f;
	/// <summary>A neighbour's pasture must be this much better (per nomad) before anyone moves.</summary>
	public const float MigrationThreshold = 1.25f;

	/// <summary>Grazing a province offers nomads, from its terrain (data/terrain.json pasture), rivers and its food slot (herds graze better than fields).</summary>
	public static float PastureQuality(Province p)
	{
		if (p == null || p.IsWater)
			return 0f;
		float q = (float)TerrainRules.Pasture(p);
		if (p.HasFeature("river")) q += 0.25f;
		if (p.Food != null && p.Food.WorkedBy.Count > 0 && p.Food.WorkedBy[0].Nomadic)
			q += 0.3f;                        // cattle, sheep, horses: good grazing
		return q;
	}

	/// <summary>Pasture left for each nomad: the province's pasture shared among the nomads already there (plus one unit, the newcomer).</summary>
	public static float PasturePerNomad(Province p, int extraUnits = 1)
	{
		int nomads = 0;
		foreach (PopGroup pop in p.Pops)
		{
			if (pop.Occupation.Nomadic)
				nomads += pop.Units;
		}
		// capacity scales with the settled population the land already feeds, so big fertile provinces take more herds
		float capacity = 4f + 0.25f * p.TotalUnits;
		return PastureQuality(p) * capacity / (capacity + nomads + extraUnits);
	}

	public readonly record struct Migration(Province From, Province To, PopGroup Group, int Units);

	/// <summary>
	/// One month of nomad migration. Nomads cross land borders freely (ownership does not matter) toward
	/// the neighbour with the best pasture per nomad, if it is clearly better than home. Moves are decided
	/// on the state at the start of the month and then applied, so the order of provinces doesn't matter.
	/// </summary>
	public static List<Migration> MigrateNomads(IReadOnlyList<Province> provinces)
	{
		var moves = new List<Migration>();
		foreach (Province p in provinces)
		{
			if (p == null || p.IsWater)
				continue;
			foreach (PopGroup group in p.Pops)
			{
				if (!group.Occupation.Nomadic || group.Units <= 0)
					continue;
				float home = PasturePerNomad(p, 0);
				Province best = null;
				float bestScore = home * MigrationThreshold;
				foreach (Adjacency link in p.Neighbors)
				{
					if (!link.To.IsPassable || link.BorderLength <= 0)
						continue;
					float score = PasturePerNomad(link.To);
					if (score > bestScore)
					{
						best = link.To;
						bestScore = score;
					}
				}
				if (best == null)
					continue;
				int units = Math.Max(1, (int)(group.Units * MigrationShare));
				moves.Add(new Migration(p, best, group, units));
			}
		}

		foreach (Migration m in moves)
		{
			int units = Math.Min(m.Units, m.Group.Units);
			if (units <= 0)
				continue;
			m.Group.Units -= units;
			m.To.AddPops(m.Group.Culture, m.Group.Religion, m.Group.Occupation, m.Group.Sex, units);
		}
		foreach (Province p in provinces)
			p?.Pops.RemoveAll(g => g.Units <= 0);
		return moves;
	}
}
