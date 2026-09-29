using System.Collections.Generic;
using System.Linq;
using Untitled.Data;

namespace Untitled.Rules;

public enum Builder
{
	/// <summary>The province owner's government (the player or the AI), which pays for it.</summary>
	Government,
	/// <summary>The province's own people, once enough of the right occupation live there.</summary>
	Population,
}

/// <summary>Who may build or expand which production building where.</summary>
public static class BuildingRules
{
	public const int MaxLevel = 5;
	/// <summary>The population expands its own buildings only this far; beyond that it takes the government.</summary>
	public const int MaxPopulationLevel = 3;

	/// <summary>Whether the province's land and resources allow the building at all. Reason is null when they do.</summary>
	public static bool MeetsRequirements(Province p, BuildingType type, out string reason)
	{
		reason = null;
		if (p == null || p.IsWater)
		{
			reason = "Not on land";
			return false;
		}
		if (type.RequiredFeatures.Count > 0 && !AnyFeature(p, type.RequiredFeatures))
		{
			reason = $"Needs {Describe(type.RequiredFeatures)}";
			return false;
		}
		if (type.RequiredFood.Count > 0 && (p.Food == null || !type.RequiredFood.Contains(p.Food)))
		{
			reason = $"Needs {DescribeResources(type.RequiredFood)} as food";
			return false;
		}
		if (type.RequiredNonRenewable.Count > 0 && (p.NonRenewable == null || !type.RequiredNonRenewable.Contains(p.NonRenewable)))
		{
			reason = $"Needs a {DescribeResources(type.RequiredNonRenewable)} deposit";
			return false;
		}
		return true;
	}

	/// <summary>Population units of the building's occupation not yet employed by a building.</summary>
	public static int FreeWorkers(Province p, Occupation occupation)
	{
		int free = p.UnitsOf(occupation);
		foreach (Building b in p.Buildings)
		{
			if (b.Type.JobOccupation == occupation)
				free -= b.Level * b.Type.JobUnits;
		}
		return free;
	}

	/// <summary>Whether <paramref name="builder"/> may build the building, or add a level to it. Reason is null when it can.</summary>
	public static bool CanBuild(Province p, BuildingType type, Builder builder, out string reason)
	{
		if (!MeetsRequirements(p, type, out reason))
			return false;

		Building existing = p.GetBuilding(type);
		int level = existing?.Level ?? 0;
		if (builder == Builder.Government && !type.GovernmentCanBuild)
		{
			reason = "Only the population builds this";
			return false;
		}
		if (builder == Builder.Population)
		{
			if (!type.PopulationCanBuild)
			{
				reason = "Only the government builds this";
				return false;
			}
			if (level >= MaxPopulationLevel)
			{
				reason = "The population won't expand it further";
				return false;
			}
			int needed = type.PopulationBuildMinUnits * (level + 1);
			if (p.UnitsOf(type.PopulationBuildOccupation) < needed)
			{
				reason = $"Needs {needed} units of {type.PopulationBuildOccupation.Name.ToLowerInvariant()}";
				return false;
			}
		}
		if (level >= MaxLevel)
		{
			reason = "At the highest level";
			return false;
		}
		if (FreeWorkers(p, type.JobOccupation) < type.JobUnits)
		{
			reason = $"Not enough free {type.JobOccupation.Name.ToLowerInvariant()} to work it";
			return false;
		}
		reason = null;
		return true;
	}

	/// <summary>Builds the building, or adds a level. Returns false (and changes nothing) if <see cref="CanBuild"/> refuses.</summary>
	public static bool Build(Province p, BuildingType type, Builder builder, out string reason)
	{
		if (!CanBuild(p, type, builder, out reason))
			return false;
		Building existing = p.GetBuilding(type);
		if (existing != null)
			existing.Level++;
		else
			p.Buildings.Add(new Building(type, 1, builder == Builder.Population));
		return true;
	}

	/// <summary>
	/// One month of the population building for itself: in every province, the first building type (in
	/// data order) that the population may build or expand gets one level. Returns the number built.
	/// </summary>
	public static int PopulationBuildStep(IReadOnlyList<Province> provinces, IReadOnlyList<BuildingType> types)
	{
		int built = 0;
		foreach (Province p in provinces)
		{
			if (p == null || p.IsWater)
				continue;
			foreach (BuildingType type in types)
			{
				if (Build(p, type, Builder.Population, out _))
				{
					built++;
					break;
				}
			}
		}
		return built;
	}

	static bool AnyFeature(Province p, IReadOnlyList<string> features)
	{
		foreach (string f in features)
		{
			if (p.HasFeature(f))
				return true;
		}
		return false;
	}

	static string Describe(IReadOnlyList<string> items) => string.Join(" or ", items);

	static string DescribeResources(IReadOnlyList<ResourceType> items)
	{
		var names = new List<string>();
		foreach (ResourceType r in items)
			names.Add(r.Name.ToLowerInvariant());
		return string.Join(" or ", names);
	}
}
