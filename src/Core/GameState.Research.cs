using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Research: the three trees of technologies, the educated classes whose points drive them all, and the
// player's queue of what to research first.
public partial class GameState
{
	/// <summary>A technology was discovered, or the research queue changed.</summary>
	[Signal]
	public delegate void ResearchChangedEventHandler();

	/// <summary>
	/// The player queues a technology (with what it builds on): at the end of the queue, or at its front
	/// to research it before anything else. A stockpile of research pours into it at once if it can.
	/// </summary>
	public bool QueueResearch(string techId, bool first, out string reason)
	{
		Country c = PlayerCountry;
		Tech t = Definitions.GetTech(techId);
		reason = null;
		if (c == null || t == null)
			reason = "Unknown technology";
		else if (TechRules.Knows(c, t))
			reason = $"We know {t.Name} already";
		if (reason != null)
			return false;
		TechRules.Queue(c, t, first);
		PourStockpile(c);
		EmitSignal(SignalName.ResearchChanged);
		return true;
	}

	/// <summary>The player takes a technology (and what builds on it) out of the queue; its progress is kept.</summary>
	public void DequeueResearch(string techId)
	{
		Country c = PlayerCountry;
		Tech t = Definitions.GetTech(techId);
		if (c == null || t == null || !c.ResearchQueue.Contains(t.Id))
			return;
		TechRules.Dequeue(c, t);
		EmitSignal(SignalName.ResearchChanged);
	}

	/// <summary>The stockpile flows into the queue, as far as it can.</summary>
	void PourStockpile(Country c)
	{
		double stock = c.ResearchStockpile;
		if (stock <= 0)
			return;
		c.ResearchStockpile = 0;
		foreach (Tech t in TechRules.Spend(c, stock, TechRules.AtWar(c, _armies.Values), this))
			Discovered(c, t, fromStockpile: true);
	}

	/// <summary>A month's research points of the country (for the top bar and the research window).</summary>
	public (double Points, List<TechRules.Source> Sources) ResearchPoints(Country c) =>
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
		c.ResearchQueue.Clear();
		c.ResearchStockpile = 0;
		c.LastResearch = 0;
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
