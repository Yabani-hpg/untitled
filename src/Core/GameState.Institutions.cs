using System;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Institutions: appearing, spreading and being embraced.
public partial class GameState
{
	public string EmbraceObstacle(Country c, Institution i)
	{
		if (c == null || i == null)
			return "No such institution";
		if (!InstitutionRules.Exists(i, Date))
			return "Not yet known in the world";
		if (InstitutionRules.Has(c, i))
			return "Embraced already";
		if (InstitutionRules.Progress(c, i) < InstitutionRules.Full)
			return $"It must spread through the realm first ({InstitutionRules.Progress(c, i):0}%)";
		double cost = InstitutionRules.EmbraceCost(c, Definitions, ProvincesOf(c.Tag).Count());
		if (c.Gold < cost)
			return $"Embracing it costs {cost:0} gold; we have {c.Gold:0}";
		return null;
	}

	public bool EmbraceInstitution(string id, out string reason) => EmbraceFor(PlayerCountry, Definitions.GetInstitution(id), out reason);

	bool EmbraceFor(Country c, Institution i, out string reason)
	{
		reason = EmbraceObstacle(c, i);
		if (reason != null)
			return false;
		c.Gold -= InstitutionRules.EmbraceCost(c, Definitions, ProvincesOf(c.Tag).Count());
		c.Institutions.Add(i.Id);
		c.InstitutionProgress.Remove(i.Id);
		if (c.Tag == PlayerTag)
			Notify($"We embrace {i.Name.ToLowerInvariant()}: technology costs us less.");
		EmitSignal(SignalName.ResearchChanged);
		return true;
	}

	/// <summary>A month: new institutions appear with those who know their origin; the rest spread; the game's countries embrace what they can.</summary>
	void RunInstitutionStep()
	{
		var settled = SettledCountries.ToList();
		foreach (Institution i in Definitions.Institutions)
		{
			if (!InstitutionRules.Exists(i, Date))
				continue;
			bool born = !_worldFlags.Contains($"institution:{i.Id}");
			if (born)
			{
				SetWorldFlag($"institution:{i.Id}");
				foreach (Country c in settled.Where(c => i.OriginTech != null && c.Techs.Contains(i.OriginTech.Id)))
					c.Institutions.Add(i.Id);
				Notify($"{i.Name} appears in the world: {i.Description} Technology grows dearer for those without it.");
			}
			foreach (Country c in settled)
			{
				if (InstitutionRules.Has(c, i))
					continue;
				bool near = settled.Any(o => o != c && InstitutionRules.Has(o, i) && AreNeighbours(c.Tag, o.Tag));
				double before = InstitutionRules.Progress(c, i);
				double now = Math.Min(InstitutionRules.Full, before + InstitutionRules.MonthlySpread(c, i, near));
				c.InstitutionProgress[i.Id] = now;
				if (c.Tag == PlayerTag && before < InstitutionRules.Full && now >= InstitutionRules.Full)
					Notify($"{i.Name} has spread through the realm: we may embrace it.");
				if (c.Tag != PlayerTag && now >= InstitutionRules.Full && c.Gold >= InstitutionRules.EmbraceCost(c, Definitions, ProvincesOf(c.Tag).Count()) + 50)
					EmbraceFor(c, i, out _);
			}
		}
	}
}
