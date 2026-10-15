using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Public works: the government raises temples, markets, barracks and the like in its provinces for gold.
public partial class GameState
{
	/// <summary>The player raises a work in a province.</summary>
	public bool BuildWork(int provinceId, string workId, out string reason)
	{
		Province p = GetProvince(provinceId);
		Work w = Definitions.GetWork(workId);
		reason = PlayerCountry == null ? "No country" : WorkRules.Obstacle(p, w, PlayerCountry);
		if (reason != null)
			return false;
		BuildWorkFor(PlayerCountry, p, w);
		return true;
	}

	void BuildWorkFor(Country c, Province p, Work w)
	{
		c.Gold -= WorkRules.Cost(w, c);
		WorkRules.Raise(p, w);
		if (c.Tag == PlayerTag)
			Notify($"{w.Name} raised in {p.Name}.");
		EmitSignal(SignalName.ProvinceChanged, p.Id);
	}

	/// <summary>The player improves a province's land: a thousand more peasants settle there.</summary>
	public bool DevelopProvince(int provinceId, out string reason)
	{
		Province p = GetProvince(provinceId);
		reason = PlayerCountry == null ? "No country" : WorkRules.DevelopObstacle(p, PlayerCountry);
		if (reason != null)
			return false;
		DevelopProvinceFor(PlayerCountry, p);
		return true;
	}

	void DevelopProvinceFor(Country c, Province p)
	{
		c.Gold -= WorkRules.DevelopCost(p, c);
		PopGroup like = p.Pops.Where(g => g.Occupation.Id == "peasants").OrderByDescending(g => g.Units).FirstOrDefault()
			?? p.Pops.OrderByDescending(g => g.Units).First();
		p.AddPeople(like.Culture, like.Religion, Definitions.Occupations.GetValueOrDefault("peasants") ?? like.Occupation, 1, true);
		if (c.Tag == PlayerTag)
			Notify($"{p.Name}: the land is improved and {PopGroup.PeoplePerUnit:N0} more people settle there.");
		EmitSignal(SignalName.ProvinceChanged, p.Id);
	}

	/// <summary>A country run by the game spends what it has beyond a reserve on works: its richest provinces first, the cheapest work first.</summary>
	void AiWorks(Country c)
	{
		double reserve = Math.Max(200, c.LastLedger.Income * 6);
		if (c.Gold < reserve || AtWar(c.Tag) && c.Gold < reserve * 2)
			return;
		var provinces = ProvincesOf(c.Tag).Where(p => p.OccupierTag == null).OrderByDescending(p => EconomyRules.Tax(p, Date, c)).ToList();
		int built = 0;
		foreach (Province p in provinces)
		{
			foreach (Work w in Definitions.Works.OrderBy(w => w.Cost))
			{
				if (c.Gold - WorkRules.Cost(w, c) < reserve)
					return;
				if (WorkRules.Obstacle(p, w, c) != null)
					continue;
				BuildWorkFor(c, p, w);
				if (++built >= 2)
					return;
				break;
			}
		}
		// nothing left to build: the land itself is improved, where the people are
		foreach (Province p in provinces)
		{
			if (built >= 2 || c.Gold - WorkRules.DevelopCost(p, c) < reserve * 2)
				return;
			if (WorkRules.DevelopObstacle(p, c) != null)
				continue;
			DevelopProvinceFor(c, p);
			built++;
		}
	}
}
