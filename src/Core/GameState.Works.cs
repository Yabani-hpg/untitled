using System;
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
	}
}
