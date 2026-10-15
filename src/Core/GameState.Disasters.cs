using System;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Disasters: brewing while their trigger holds, striking at full progress, ending when settled.
public partial class GameState
{
	void RunDisasterStep()
	{
		foreach (Country c in SettledCountries)
		{
			foreach (Disaster d in Definitions.Disasters)
			{
				if (c.ActiveDisasters.TryGetValue(d.Id, out GameDate started))
				{
					bool settled = d.End == null || d.End.Holds(this, c);
					if (settled && (Date.Day - started.Day) / 365.2425 >= d.MinYears)
					{
						c.ActiveDisasters.Remove(d.Id);
						ApplyEffects(c, d.OnEnd);
						if (c.Tag == PlayerTag)
							Notify($"{d.Name} is over.");
						EmitSignal(SignalName.LawsChanged);
					}
					continue;
				}
				double before = DisasterRules.Progress(c, d);
				if (d.Trigger != null && d.Trigger.Holds(this, c))
				{
					double now = before + d.ProgressPerMonth;
					if (now >= DisasterRules.Full)
					{
						c.DisasterProgress.Remove(d.Id);
						c.ActiveDisasters[d.Id] = Date;
						ApplyEffects(c, d.OnStart);
						if (c.Tag == PlayerTag)
							Notify($"Disaster: {d.Name}. {d.Description}");
						else if (AreNeighbours(PlayerTag, c.Tag))
							Notify($"{c.Name} is in the grip of {d.Name.ToLowerInvariant()}.");
						EmitSignal(SignalName.LawsChanged);
					}
					else
					{
						c.DisasterProgress[d.Id] = now;
						if (c.Tag == PlayerTag && before < 50 && now >= 50)
							Notify($"{d.Name} brews: {now:0}%. {d.Description}");
					}
				}
				else if (before > 0)
				{
					double now = before - d.DeclinePerMonth;
					if (now <= 0)
						c.DisasterProgress.Remove(d.Id);
					else
						c.DisasterProgress[d.Id] = now;
				}
			}
		}
	}
}
