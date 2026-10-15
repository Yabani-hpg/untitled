using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Public works: what a province has room for, what they do for it, and whether a country may raise one.
/// A province's slots grow with its people; an upgrade replaces the work below it and takes no new slot.
/// </summary>
public static class WorkRules
{
	public static Definitions Defs { get; set; }

	/// <summary>How many works a province has room for.</summary>
	public static int Slots(Province p) =>
		Defs == null ? 1 : Math.Clamp(1 + p.TotalUnits / Math.Max(1, Defs.WorkSlotUnits), 1, Defs.MaxWorkSlots);

	/// <summary>The sum of a modifier over the province's works.</summary>
	public static double Mod(Province p, string key)
	{
		double sum = 0;
		foreach (Work w in p.Works)
			sum += w.Modifiers.GetValueOrDefault(key);
		return sum;
	}

	/// <summary>A modifier summed over all of a country's provinces (flat bonuses such as research).</summary>
	public static double Flat(IEnumerable<Province> owned, string key) => owned.Sum(p => Mod(p, key));

	/// <summary>Why a country can't raise a work in a province, or null.</summary>
	public static string Obstacle(Province p, Work w, Country owner)
	{
		if (p == null || p.IsWater || w == null)
			return "Not on land";
		if (owner == null || p.OwnerTag != owner.Tag)
			return "Not our province";
		if (p.OccupierTag != null)
			return "Occupied by the enemy";
		if (p.Works.Contains(w))
			return "Already built";
		if (p.Works.Any(x => Supersedes(x, w)))
			return "Something better stands here already";
		if (w.Tech != null && !owner.Techs.Contains(w.Tech.Id))
			return $"Needs {w.Tech.Name}";
		if (w.RequiredFeatures.Count > 0 && !w.RequiredFeatures.Any(p.HasFeature))
			return $"Needs {string.Join(" or ", w.RequiredFeatures)}";
		if (w.Upgrades != null && !p.Works.Contains(w.Upgrades))
			return $"Needs a {w.Upgrades.Name.ToLowerInvariant()} to build on";
		if (w.Upgrades == null && p.Works.Count >= Slots(p))
			return $"No room: {Slots(p)} slot{(Slots(p) == 1 ? "" : "s")}, all taken";
		if (owner.Gold < Cost(w, owner))
			return $"Costs {Cost(w, owner):0} gold; we have {owner.Gold:0}";
		return null;
	}

	/// <summary>Whether <paramref name="a"/> is <paramref name="b"/> or an upgrade of it, however far up.</summary>
	public static bool Supersedes(Work a, Work b)
	{
		for (Work w = a; w != null; w = w.Upgrades)
		{
			if (w == b)
				return true;
		}
		return false;
	}

	public static double Cost(Work w, Country owner) => w.Cost * Math.Max(0.2, 1 + LawRules.Mod(owner, "building_cost"));

	/// <summary>Raises the work, replacing what it upgrades.</summary>
	public static void Raise(Province p, Work w)
	{
		if (w.Upgrades != null)
			p.Works.Remove(w.Upgrades);
		p.Works.Add(w);
	}

	/// <summary>A work's effects in words.</summary>
	public static string Describe(Work w)
	{
		var parts = new List<string>();
		foreach (var (key, v) in w.Modifiers)
		{
			parts.Add(key switch
			{
				"tax" => $"tax {v:+0%;-0%}",
				"trade_power" => $"trade power {v:+0%;-0%}",
				"levies" => $"levies {v:+0%;-0%}",
				"unrest" => $"unrest {v:+0%;-0%}",
				"defense" => $"sieges {v:+0%;-0%} longer",
				"research" => $"research {v:+0.0;-0.0} a month",
				_ => $"{key} {v:+0.##;-0.##}",
			});
		}
		return string.Join(", ", parts);
	}

	/// <summary>The works a country could raise in a province now, and those it could with more gold or knowledge.</summary>
	public static IEnumerable<Work> Options(Province p, Country owner) =>
		Defs == null ? Enumerable.Empty<Work>() : Defs.Works.Where(w => !p.Works.Contains(w) && !p.Works.Any(x => Supersedes(x, w)) && (w.Upgrades == null || p.Works.Contains(w.Upgrades)));
}
