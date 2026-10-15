using System.Collections.Generic;
using System.Linq;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>Disasters: their modifiers hold while they run.</summary>
public static class DisasterRules
{
	public const double Full = 100;

	public static bool IsActive(Country c, Disaster d) => c.ActiveDisasters.ContainsKey(d.Id);
	public static double Progress(Country c, Disaster d) => c.DisasterProgress.GetValueOrDefault(d.Id);

	public static double Mod(Country c, string key, Definitions defs)
	{
		double sum = 0;
		foreach (string id in c.ActiveDisasters.Keys)
		{
			if (defs.GetDisaster(id) is Disaster d)
				sum += d.Modifiers.GetValueOrDefault(key);
		}
		return sum;
	}
}
