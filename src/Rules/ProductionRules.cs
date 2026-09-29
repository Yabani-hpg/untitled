using System;
using System.Collections.Generic;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>What a production building made this month.</summary>
public sealed class BuildingOutput
{
	public Building Building { get; init; }
	/// <summary>Share of its jobs that are filled, 0..1.</summary>
	public float Staffing { get; init; }
	/// <summary>Share of its inputs it got, 0..1 (1 when it needs none).</summary>
	public float InputSupply { get; init; }
	public Dictionary<ResourceType, float> Produced { get; } = new();
	public Dictionary<ResourceType, float> Consumed { get; } = new();
	/// <summary>Why it runs below capacity, or null.</summary>
	public string Shortfall { get; init; }
}

/// <summary>A province's monthly production across its three slots.</summary>
public sealed class ProductionReport
{
	/// <summary>The non-renewable slot: its deposit, and how much of it the province's buildings extract.</summary>
	public ResourceType NonRenewable { get; init; }
	public float Extracted { get; set; }

	/// <summary>The food slot: food made by the people working the land (those not employed in buildings).</summary>
	public ResourceType Food { get; init; }
	public int FoodWorkers { get; set; }
	public float FoodOutput { get; set; }

	/// <summary>The produced slot: every building's output, and the province's net result per resource.</summary>
	public List<BuildingOutput> Buildings { get; } = new();
	public Dictionary<ResourceType, float> Net { get; } = new();
}

/// <summary>Monthly production of a province: its food slot, and its buildings turning local resources into goods.</summary>
public static class ProductionRules
{
	public static ProductionReport Compute(Province p, Definitions defs)
	{
		var report = new ProductionReport { NonRenewable = p.NonRenewable, Food = p.Food };

		// jobs are filled from each occupation's people in building order; the rest work the food slot
		var free = new Dictionary<Occupation, int>();
		foreach (PopGroup pop in p.Pops)
			free[pop.Occupation] = free.GetValueOrDefault(pop.Occupation) + pop.Units;
		var staffing = new Dictionary<Building, float>();
		foreach (Building b in p.Buildings)
		{
			int needed = b.Level * b.Type.JobUnits;
			int have = Math.Min(needed, free.GetValueOrDefault(b.Type.JobOccupation));
			free[b.Type.JobOccupation] = free.GetValueOrDefault(b.Type.JobOccupation) - have;
			staffing[b] = needed > 0 ? (float)have / needed : 1f;
		}

		if (p.Food != null)
		{
			foreach (var (occupation, units) in free)
			{
				if (p.Food.IsWorkedBy(occupation))
					report.FoodWorkers += units;
			}
			report.FoodOutput = report.FoodWorkers * p.Food.FoodYield;
		}

		// buildings with no inputs first (wood, mined ore), then those that consume what the first made
		var available = new Dictionary<ResourceType, float>();
		var ordered = new List<Building>(p.Buildings);
		ordered.Sort((a, b) => a.Type.Consumes.Count.CompareTo(b.Type.Consumes.Count));
		foreach (Building b in ordered)
		{
			float staff = staffing[b];
			float supply = 1f;
			string shortfall = staff < 1f ? $"Short of {b.Type.JobOccupation.Name.ToLowerInvariant()}" : null;
			foreach (var (id, amount) in b.Type.Consumes)
			{
				ResourceType r = defs.Resources[id];
				float need = amount * b.Level * staff;
				if (need <= 0f)
					continue;
				float got = Math.Min(need, available.GetValueOrDefault(r));
				if (got < need)
				{
					supply = Math.Min(supply, got / need);
					shortfall ??= $"Short of {r.Name.ToLowerInvariant()}";
				}
			}

			var output = new BuildingOutput { Building = b, Staffing = staff, InputSupply = supply, Shortfall = shortfall };
			float rate = b.Level * staff * supply;
			foreach (var (id, amount) in b.Type.Consumes)
			{
				ResourceType r = defs.Resources[id];
				float used = amount * rate;
				if (used <= 0f)
					continue;
				available[r] = available.GetValueOrDefault(r) - used;
				output.Consumed[r] = used;
				report.Net[r] = report.Net.GetValueOrDefault(r) - used;
			}
			foreach (var (id, amount) in b.Type.Produces)
			{
				ResourceType r = id == BuildingType.LocalDeposit ? p.NonRenewable : defs.Resources[id];
				if (r == null)
					continue;
				float made = amount * rate;
				available[r] = available.GetValueOrDefault(r) + made;
				output.Produced[r] = made;
				report.Net[r] = report.Net.GetValueOrDefault(r) + made;
				if (r == p.NonRenewable)
					report.Extracted += made;
			}
			report.Buildings.Add(output);
		}
		// keep the report in the province's building order
		report.Buildings.Sort((a, b) => p.Buildings.IndexOf(a.Building).CompareTo(p.Buildings.IndexOf(b.Building)));
		return report;
	}
}
