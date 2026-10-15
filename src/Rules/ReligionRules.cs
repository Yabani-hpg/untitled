using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Faith. Each settled country has a state religion, its ruler's; its religious unity is the share of its
/// people who follow it. Provinces of another faith are restless unless the law tolerates them. Missionaries
/// turn such provinces to the state religion, a thousand people a month. Against a neighbour of another
/// faith a country with a priesthood has a cause for holy war.
/// </summary>
public static class ReligionRules
{
	/// <summary>How much more likely an uprising is in a province whose people follow another faith.</summary>
	public const double HereticUnrest = 0.5;
	/// <summary>Gold a month a missionary costs; missionaries a country has besides what its laws add; units converted a month.</summary>
	public const double MissionaryUpkeep = 2;
	public const int BaseMissionaries = 1, UnitsPerMonth = 1;
	/// <summary>The technology that gives a cause for holy war.</summary>
	public const string HolyWarTech = "priesthood";

	/// <summary>What adopting other gods costs the state, and the share of its people that must follow them.</summary>
	public const int ConvertStability = 2, ConvertLegitimacy = 20, ConvertPrestige = 20;
	public const double ConvertMinShare = 0.2;

	public static Religion Of(Country c) => c?.StateReligion ?? c?.Ruler?.Religion;

	/// <summary>The share of a country's settled people who follow a faith.</summary>
	public static double Share(Country c, IEnumerable<Province> owned, Religion faith)
	{
		int all = 0, theirs = 0;
		foreach (Province p in owned)
		{
			foreach (PopGroup g in p.Pops)
			{
				all += g.Units;
				if (g.Religion == faith)
					theirs += g.Units;
			}
		}
		return all == 0 ? 0 : (double)theirs / all;
	}

	/// <summary>The share of a country's settled people who follow its state religion (1 with no people).</summary>
	public static double Unity(Country c, IEnumerable<Province> owned)
	{
		Religion faith = Of(c);
		int all = 0, ours = 0;
		foreach (Province p in owned)
		{
			foreach (PopGroup g in p.Pops)
			{
				all += g.Units;
				if (g.Religion == faith)
					ours += g.Units;
			}
		}
		return all == 0 ? 1 : (double)ours / all;
	}

	/// <summary>Units in a province that follow another faith than the owner's.</summary>
	public static int Heretics(Province p, Country owner)
	{
		Religion faith = Of(owner);
		return p.Pops.Where(g => g.Religion != faith).Sum(g => g.Units);
	}

	/// <summary>Multiplier on uprisings: a province of another faith is more restless, unless the law tolerates it.</summary>
	public static double UnrestFactor(Province p, Country owner)
	{
		if (owner == null || p.MainReligion == null || p.MainReligion == Of(owner))
			return 1;
		return 1 + HereticUnrest * Math.Max(0, 1 - LawRules.Mod(owner, "tolerance"));
	}

	public static int Missionaries(Country c) => BaseMissionaries + (int)LawRules.Mod(c, "missionaries");

	/// <summary>Why a missionary can't be sent to a province, or null.</summary>
	public static string MissionaryObstacle(Province p, Country c)
	{
		if (p == null || c == null || p.OwnerTag != c.Tag)
			return "Not our province";
		if (c.Converting.Contains(p.Id))
			return "A missionary is at work there";
		if (p.OccupierTag != null)
			return "Occupied by the enemy";
		if (Of(c) == null)
			return "We have no faith to spread";
		if (Heretics(p, c) == 0)
			return "They follow our gods already";
		if (c.Converting.Count >= Missionaries(c))
			return $"All our {Missionaries(c)} missionar{(Missionaries(c) == 1 ? "y is" : "ies are")} at work";
		return null;
	}

	/// <summary>A month of a missionary's work: the largest group of another faith gives up units to the state religion. Returns the units converted.</summary>
	public static int ConvertStep(Province p, Religion faith, int units)
	{
		int done = 0;
		while (done < units)
		{
			PopGroup from = p.Pops.Where(g => g.Religion != faith).OrderByDescending(g => g.Units).FirstOrDefault();
			if (from == null)
				break;
			from.Units--;
			p.AddPops(from.Culture, faith, from.Occupation, from.Sex, 1);
			p.Pops.RemoveAll(g => g.Units <= 0);
			done++;
		}
		return done;
	}

	/// <summary>Whether a country has a cause for holy war against another: a priesthood, and different gods.</summary>
	public static bool CanHolyWar(Country us, Country them) =>
		us != null && them != null && Of(us) != null && Of(them) != null && Of(us) != Of(them) && us.Techs.Contains(HolyWarTech);
}
