using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Research: the three trees of technologies, the educated classes whose points drive them, and the
// player's choices of what to research.
public partial class GameState
{
	/// <summary>A technology was discovered, or research was started or stopped.</summary>
	[Signal]
	public delegate void ResearchChangedEventHandler();

	/// <summary>The player puts a kind of research into a technology; the stockpile pours into it.</summary>
	public bool SelectResearch(string techId, out string reason)
	{
		Country c = PlayerCountry;
		Tech t = Definitions.GetTech(techId);
		reason = null;
		if (c == null || t == null)
		{
			reason = "Unknown technology";
			return false;
		}
		if (!TechRules.Select(c, t, this, out bool learned, out reason))
			return false;
		if (learned)
			Discovered(c, t, fromStockpile: true);
		EmitSignal(SignalName.ResearchChanged);
		return true;
	}

	/// <summary>The player stops a kind of research: its points go to the stockpile, and the progress made is kept.</summary>
	public void StopResearch(TechCategory category)
	{
		Country c = PlayerCountry;
		if (c == null || !c.Researching.Remove(category))
			return;
		EmitSignal(SignalName.ResearchChanged);
	}

	/// <summary>A month's research points of the country (for the top bar and the research window).</summary>
	public (Dictionary<TechCategory, double> Points, List<TechRules.Source> Sources) ResearchPoints(Country c) =>
		TechRules.MonthlyPoints(c, ProvincesOf(c.Tag).ToList(), _armies.Values);

	void Discovered(Country c, Tech t, bool fromStockpile = false)
	{
		if (c.Tag != PlayerTag)
			return;
		var unlocks = TechRules.Unlocks(t, Definitions);
		Notify($"Our {(t.Category == TechCategory.Science ? "scholars" : t.Category == TechCategory.Admin ? "scribes" : "captains")} discover {t.Name}"
			+ (fromStockpile ? ", with the research we had stored" : "") + "."
			+ (unlocks.Count > 0 ? $" It opens: {string.Join(", ", unlocks)}." : ""));
	}

	/// <summary>The monthly research step for every settled country.</summary>
	void RunResearchStep()
	{
		bool any = false;
		foreach (Country c in _countries.Values)
		{
			if (c.CapitalId <= 0)
				continue;
			foreach (Tech t in TechRules.MonthlyStep(c, ProvincesOf(c.Tag).ToList(), _armies.Values, this, c.Tag == PlayerTag))
			{
				Discovered(c, t);
				any = true;
			}
		}
		if (any)
			RefreshFlags();
		EmitSignal(SignalName.ResearchChanged);
	}

	/// <summary>
	/// A new game's knowledge: what every settled country knows, and what its country file adds (with the
	/// technologies those build on).
	/// </summary>
	void SetUpTechs(Country c)
	{
		c.Techs.Clear();
		c.ResearchProgress.Clear();
		c.Researching.Clear();
		c.ResearchStockpile.Clear();
		c.LastResearch.Clear();
		void Know(Tech t)
		{
			if (!c.Techs.Add(t.Id))
				return;
			t.Requires.ForEach(Know);
		}
		foreach (Tech t in Definitions.Techs.Where(t => t.KnownAtStart))
			Know(t);
		foreach (string id in c.Definition?.Techs ?? new())
			Know(Definitions.GetTech(id));
	}

	/// <summary>
	/// The educated classes and artisans of the settled countries at the start: each occupation's start
	/// share of the peasants of their core provinces takes up that work, the largest provinces (the cities)
	/// first; the capital always has scribes.
	/// </summary>
	void SeedOccupations(Country c)
	{
		var cores = ProvincesOf(c.Tag).Where(p => p.IsCore).OrderByDescending(p => p.TotalUnits).ToList();
		if (cores.Count == 0 || !Definitions.Occupations.TryGetValue("peasants", out Occupation peasants))
			return;
		foreach (Occupation o in Definitions.Occupations.Values.Where(o => o.StartShare > 0))
		{
			// the share is drawn by the size of each province, towns giving more than villages
			double owed = 0;
			foreach (Province p in cores)
			{
				owed += p.UnitsOf(peasants) * o.StartShare * Math.Min(2, 0.5 + p.TotalUnits / 20.0);
				while (owed >= 1 && Convert(p, peasants, o))
					owed--;
			}
		}
		// the state's records are kept in the capital
		if (Definitions.Occupations.TryGetValue("scribes", out Occupation scribes) && GetProvince(c.CapitalId) is Province capital
			&& capital.UnitsOf(scribes) == 0)
			Convert(capital, peasants, scribes);
	}

	/// <summary>A unit of <paramref name="from"/> takes up the work of <paramref name="to"/> (from the largest group of them).</summary>
	static bool Convert(Province p, Occupation from, Occupation to)
	{
		PopGroup g = p.Pops.Where(x => x.Occupation == from).OrderByDescending(x => x.Units).FirstOrDefault();
		if (g == null || g.Units <= 1)
			return false;
		g.Units--;
		p.AddPops(g.Culture, g.Religion, to, g.Sex, 1);
		return true;
	}
}
