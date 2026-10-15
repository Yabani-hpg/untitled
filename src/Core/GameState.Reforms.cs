using System;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Government reforms: progress each month, and the choice of each tier.
public partial class GameState
{
	public string ReformObstacle(Country c, Reform r)
	{
		if (c == null || r == null)
			return "No such reform";
		ReformTier next = ReformRules.NextTier(c, Definitions);
		if (next == null)
			return "Every tier is enacted";
		if (r.Tier != next)
			return r.Tier.Index < next.Index ? "That tier is enacted already" : $"First the tier before: {next.Name}";
		if (!ReformRules.Open(c, r))
			return "Not open to our form of government";
		if (c.ReformProgress < ReformRules.Cost(next.Index))
			return $"Needs {ReformRules.Cost(next.Index):0} reform progress; we have {c.ReformProgress:0}";
		return null;
	}

	public bool EnactReform(string id, out string reason) => EnactReformFor(PlayerCountry, Definitions.GetReform(id), out reason);

	bool EnactReformFor(Country c, Reform r, out string reason)
	{
		reason = ReformObstacle(c, r);
		if (reason != null)
			return false;
		c.ReformProgress -= ReformRules.Cost(r.Tier.Index);
		c.Reforms.Add(r.Id);
		if (c.Tag == PlayerTag)
			Notify($"Reform enacted: {r.Name} ({IdeaRules.Describe(r.Modifiers)}).");
		EmitSignal(SignalName.LawsChanged);
		return true;
	}

	/// <summary>A month: progress accrues; reforms a changed government no longer fits are lost; the game's countries enact the first reform open to them.</summary>
	void RunReformStep()
	{
		foreach (Country c in SettledCountries)
		{
			c.ReformProgress += ReformRules.MonthlyProgress(c);
			foreach (string id in c.Reforms.ToList())
			{
				if (Definitions.GetReform(id) is Reform r && !ReformRules.Open(c, r))
				{
					c.Reforms.Remove(id);
					if (c.Tag == PlayerTag)
						Notify($"{r.Name} no longer fits our government and lapses.");
				}
			}
			if (c.Tag == PlayerTag)
				continue;
			ReformTier next = ReformRules.NextTier(c, Definitions);
			if (next != null && c.ReformProgress >= ReformRules.Cost(next.Index))
			{
				Reform pick = next.Reforms.FirstOrDefault(r => ReformRules.Open(c, r));
				if (pick != null)
					EnactReformFor(c, pick, out _);
			}
		}
	}
}
