using System;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Forts: building them, and the zone of control they throw.
public partial class GameState
{
	/// <summary>Why the country can't raise the province's walls a level, or null.</summary>
	public string FortObstacle(Country c, Province p)
	{
		if (c == null || p == null || p.OwnerTag != c.Tag)
			return "Not our province";
		if (p.OccupierTag != null)
			return "Occupied by the enemy";
		if (p.FortLevel >= FortRules.MaxLevel)
			return "Its walls are as strong as walls can be";
		int next = p.FortLevel + 1;
		if (!c.Techs.Contains(FortRules.Techs[next]))
			return $"Needs {Definitions.GetTech(FortRules.Techs[next])?.Name}";
		if (c.Gold < FortRules.Cost[next])
			return $"{FortRules.Names[next]} cost {FortRules.Cost[next]:0} gold; we have {c.Gold:0}";
		return null;
	}

	public bool BuildFort(int provinceId, out string reason) => BuildFortFor(PlayerCountry, GetProvince(provinceId), out reason);

	bool BuildFortFor(Country c, Province p, out string reason)
	{
		reason = FortObstacle(c, p);
		if (reason != null)
			return false;
		p.FortLevel++;
		c.Gold -= FortRules.Cost[p.FortLevel];
		if (c.Tag == PlayerTag)
			Notify($"{p.Name} has {FortRules.Names[p.FortLevel].ToLowerInvariant()} now.");
		EmitSignal(SignalName.ProvinceChanged, p.Id);
		return true;
	}

	/// <summary>An unoccupied fort of the enemy: its zone of control bars the way through the provinces around it.</summary>
	bool HostileFort(Province q, string tag) =>
		q.FortLevel > 0 && q.OwnerTag != null && Enemies(tag, q.OwnerTag) && (q.OccupierTag == null || Enemies(tag, q.OccupierTag));

	/// <summary>Whether an army of a country may not march through the province (it may still stop there): a hostile fort stands beside it, and the land is not its own side's.</summary>
	public bool InHostileZone(Province p, string tag)
	{
		if (p.OwnerTag != null && SameSide(tag, p.OwnerTag) && (p.OccupierTag == null || SameSide(tag, p.OccupierTag)))
			return false;
		if (p.OccupierTag != null && SameSide(tag, p.OccupierTag))
			return false;
		return HostileFort(p, tag) || p.Neighbors.Any(l => l.SharesBorder && HostileFort(l.To, tag));
	}

	/// <summary>A country run by the game walls its capital, then its border provinces, when the treasury allows.</summary>
	void AiForts(Country c)
	{
		if (_rng.NextDouble() > 0.05)
			return;
		Province pick = GetProvince(c.CapitalId);
		if (pick == null || pick.FortLevel >= 1)
		{
			pick = ProvincesOf(c.Tag).Where(p => p.OccupierTag == null && p.Neighbors.Any(l => l.SharesBorder && l.To.OwnerTag != null && l.To.OwnerTag != c.Tag && GetCountry(l.To.OwnerTag)?.CapitalId > 0))
				.OrderBy(p => p.FortLevel).ThenByDescending(p => p.TotalUnits).FirstOrDefault() ?? pick;
		}
		if (pick == null || pick.FortLevel >= FortRules.MaxLevel)
			return;
		if (c.Gold >= FortRules.Cost[pick.FortLevel + 1] * 3)
			BuildFortFor(c, pick, out _);
	}
}
