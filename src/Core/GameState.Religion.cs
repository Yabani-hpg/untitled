using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Faith: missionaries sent and recalled, their monthly work, and what the game's countries do about other gods.
public partial class GameState
{
	/// <summary>The player sends a missionary to a province of another faith.</summary>
	public bool SendMissionary(int provinceId, out string reason)
	{
		Province p = GetProvince(provinceId);
		reason = PlayerCountry == null ? "No country" : ReligionRules.MissionaryObstacle(p, PlayerCountry);
		if (reason != null)
			return false;
		PlayerCountry.Converting.Add(p.Id);
		EmitSignal(SignalName.ProvinceChanged, p.Id);
		return true;
	}

	public void RecallMissionary(int provinceId)
	{
		if (PlayerCountry != null && PlayerCountry.Converting.Remove(provinceId))
			EmitSignal(SignalName.ProvinceChanged, provinceId);
	}

	/// <summary>Faiths a fifth or more of a country's people follow, other than the state's.</summary>
	public IEnumerable<Religion> AdoptableReligions(Country c)
	{
		var owned = ProvincesOf(c.Tag).ToList();
		return Definitions.Religions.Values.Where(r => r != ReligionRules.Of(c) && ReligionRules.Share(c, owned, r) >= ReligionRules.ConvertMinShare).ToList();
	}

	/// <summary>Why the state can't adopt a faith, or null.</summary>
	public string ConvertObstacle(Country c, Religion faith)
	{
		if (c == null || faith == null)
			return "No such faith";
		if (ReligionRules.Of(c) == faith)
			return "The state's gods already";
		if (ReligionRules.Share(c, ProvincesOf(c.Tag), faith) < ReligionRules.ConvertMinShare)
			return $"Too few of our people follow them ({ReligionRules.ConvertMinShare:P0} needed)";
		if (AtWar(c.Tag))
			return "Not in wartime";
		if (c.Stability < -1)
			return "The realm is too unsettled";
		return null;
	}

	/// <summary>The player's state adopts other gods: those its people mostly follow, at a cost in stability, legitimacy and prestige.</summary>
	public bool ConvertStateReligion(string religionId, out string reason)
	{
		Country c = PlayerCountry;
		Religion faith = Definitions.Religions.GetValueOrDefault(religionId);
		reason = c == null ? "No country" : ConvertObstacle(c, faith);
		if (reason != null)
			return false;
		ConvertStateReligionOf(c, faith, forced: false);
		return true;
	}

	void ConvertStateReligionOf(Country c, Religion faith, bool forced)
	{
		c.StateReligion = faith;
		if (c.Ruler != null)
			c.Ruler.Religion = faith;
		c.Converting.Clear();
		StateRules.ChangeStability(c, -ReligionRules.ConvertStability);
		c.Legitimacy = Math.Max(0, c.Legitimacy - ReligionRules.ConvertLegitimacy);
		c.Prestige = Math.Max(-100, c.Prestige - ReligionRules.ConvertPrestige);
		if (c.Tag == PlayerTag)
			Notify(forced ? $"We are made to worship the {faith.Name}." : $"The state now worships the {faith.Name}.");
		else if (AreNeighbours(PlayerTag, c.Tag))
			Notify($"{c.Name} now worships the {faith.Name}.");
		EmitSignal(SignalName.LawsChanged);
		EmitSignal(SignalName.DiplomacyChanged);
	}

	/// <summary>Each month missionaries convert a thousand people where they work; the game's countries send theirs to their largest provinces of other gods.</summary>
	void RunReligionStep()
	{
		foreach (Country c in SettledCountries.ToList())
		{
			Religion faith = ReligionRules.Of(c);
			foreach (int id in c.Converting.ToList())
			{
				Province p = GetProvince(id);
				if (p == null || p.OwnerTag != c.Tag || faith == null)
				{
					c.Converting.Remove(id);
					continue;
				}
				if (p.OccupierTag != null)
					continue;                                  // the work waits while the enemy holds the town
				ReligionRules.ConvertStep(p, faith, ReligionRules.UnitsPerMonth);
				if (ReligionRules.Heretics(p, c) == 0)
				{
					c.Converting.Remove(id);
					if (c.Tag == PlayerTag)
						Notify($"{p.Name} now follows the {faith.Name}.");
				}
				EmitSignal(SignalName.ProvinceChanged, id);
			}
			if (c.Tag != PlayerTag && faith != null && c.Converting.Count < ReligionRules.Missionaries(c) && c.Gold > 50 && _rng.NextDouble() < 0.5)
			{
				Province pick = ProvincesOf(c.Tag).Where(p => p.IsCore && ReligionRules.MissionaryObstacle(p, c) == null)
					.OrderByDescending(p => ReligionRules.Heretics(p, c)).FirstOrDefault();
				if (pick != null)
					c.Converting.Add(pick.Id);
			}
		}
	}
}
