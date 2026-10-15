using System;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Estates: privileges granted and revoked, land seized, and the game's countries managing theirs.
public partial class GameState
{
	public double EstateInfluence(Country c, Estate e) => EstateRules.Influence(c, e, ProvincesOf(c.Tag).ToList());
	public double EstatesInfluence(Country c) => Definitions.Estates.Sum(e => EstateInfluence(c, e));

	public string GrantObstacle(Country c, Privilege p)
	{
		if (c == null || p == null)
			return "No such privilege";
		if (c.Privileges.Contains(p.Id))
			return "Already granted";
		return null;
	}

	public bool GrantPrivilege(string id, out string reason) => GrantPrivilegeFor(PlayerCountry, Definitions.GetPrivilege(id), out reason);

	bool GrantPrivilegeFor(Country c, Privilege p, out string reason)
	{
		reason = GrantObstacle(c, p);
		if (reason != null)
			return false;
		c.Privileges.Add(p.Id);
		c.EstateLoyalty[p.Estate.Id] = Math.Min(100, EstateRules.Loyalty(c, p.Estate) + p.Loyalty);
		if (c.Tag == PlayerTag)
			Notify($"{p.Estate.Name} are granted {p.Name.ToLowerInvariant()}.");
		EmitSignal(SignalName.LawsChanged);
		return true;
	}

	public bool RevokePrivilege(string id, out string reason)
	{
		Country c = PlayerCountry;
		Privilege p = Definitions.GetPrivilege(id);
		reason = c == null || p == null ? "No such privilege" : !c.Privileges.Contains(p.Id) ? "Not granted" : null;
		if (reason != null)
			return false;
		c.Privileges.Remove(p.Id);
		c.EstateLoyalty[p.Estate.Id] = Math.Max(0, EstateRules.Loyalty(c, p.Estate) + EstateRules.RevokeLoyalty);
		Notify($"{p.Name} is revoked: {p.Estate.Name.ToLowerInvariant()} are angered.");
		EmitSignal(SignalName.LawsChanged);
		return true;
	}

	public string SeizeObstacle(Country c)
	{
		if (c == null)
			return "No country";
		if (c.Crownland >= 100)
			return "The crown holds all the land already";
		var angry = Definitions.Estates.Where(e => EstateRules.Loyalty(c, e) + EstateRules.SeizeLoyalty < EstateRules.DisloyalBelow).ToList();
		if (angry.Count > 0)
			return $"It would drive {string.Join(" and ", angry.Select(e => e.Name.ToLowerInvariant()))} to disloyalty";
		return null;
	}

	/// <summary>The crown takes land back from the estates: more crown land, and every estate the angrier for it.</summary>
	public bool SeizeLand(out string reason) => SeizeLandFor(PlayerCountry, out reason);

	bool SeizeLandFor(Country c, out string reason)
	{
		reason = SeizeObstacle(c);
		if (reason != null)
			return false;
		c.Crownland = Math.Min(100, c.Crownland + EstateRules.SeizeCrownland);
		foreach (Estate e in Definitions.Estates)
			c.EstateLoyalty[e.Id] = Math.Max(0, EstateRules.Loyalty(c, e) + EstateRules.SeizeLoyalty);
		if (c.Tag == PlayerTag)
			Notify($"The crown seizes land: it now holds {c.Crownland:0}% of the realm; the estates grumble.");
		EmitSignal(SignalName.LawsChanged);
		return true;
	}

	void RunEstateStep()
	{
		foreach (Country c in SettledCountries)
		{
			EstateRules.MonthlyStep(c, Definitions);
			if (c.Tag == PlayerTag)
				continue;
			// the game's countries keep their estates content with privileges, and take land back when they can
			if (_rng.NextDouble() < 0.08)
			{
				Estate unhappy = Definitions.Estates.Where(e => EstateRules.Loyalty(c, e) < 45).OrderBy(e => EstateRules.Loyalty(c, e)).FirstOrDefault();
				Privilege give = unhappy?.Privileges.FirstOrDefault(p => !c.Privileges.Contains(p.Id));
				if (give != null)
					GrantPrivilegeFor(c, give, out _);
				else if (c.Crownland < 60 && Definitions.Estates.All(e => EstateRules.Loyalty(c, e) >= 60) && _rng.NextDouble() < 0.3)
					SeizeLandFor(c, out _);
			}
		}
	}
}
