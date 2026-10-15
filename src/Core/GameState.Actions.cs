using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Diplomatic actions beyond alliance and war: fabricating claims, guarantees and royal marriages.
public partial class GameState
{
	// ------------------------------------------------------------------------------------ claims

	/// <summary>Provinces of another country a diplomat could fabricate a claim on: those along our border.</summary>
	public IEnumerable<Province> FabricableProvinces(Country us, Country them) =>
		_provinces.Where(p => p?.OwnerTag == them.Tag && !us.Claims.Contains(p.Id) && p.Neighbors.Any(l => (l.SharesBorder || l.IsNavigableRiver) && l.To.OwnerTag == us.Tag));

	public string FabricateObstacle(Country us, Province p)
	{
		if (us == null || p?.OwnerTag == null || p.OwnerTag == us.Tag)
			return "Not another country's province";
		if (us.Claims.Contains(p.Id))
			return "We have a claim on it already";
		if (us.Fabricating.ContainsKey(p.Id))
			return "A diplomat is at work on it";
		if (!p.Neighbors.Any(l => (l.SharesBorder || l.IsNavigableRiver) && l.To.OwnerTag == us.Tag))
			return "Only along our border";
		if (GetCountry(p.OwnerTag) is Country them && (us.Allies.Contains(them.Tag) || them.OverlordTag == us.Tag || us.OverlordTag == them.Tag))
			return "Not against an ally, overlord or vassal";
		if (FreeDiplomats(us) <= 0)
			return "All our diplomats are busy";
		return null;
	}

	/// <summary>A diplomat begins forging a claim on the province; it takes months, and the owner learns of it when it is done.</summary>
	public bool FabricateClaim(int provinceId, out string reason) => FabricateClaimFor(PlayerCountry, GetProvince(provinceId), out reason);

	bool FabricateClaimFor(Country c, Province p, out string reason)
	{
		reason = FabricateObstacle(c, p);
		if (reason != null)
			return false;
		c.Fabricating[p.Id] = 0;
		EmitSignal(SignalName.DiplomacyChanged);
		return true;
	}

	public void StopFabricating(int provinceId)
	{
		if (PlayerCountry?.Fabricating.Remove(provinceId) == true)
			EmitSignal(SignalName.DiplomacyChanged);
	}

	void RunFabricationStep()
	{
		foreach (Country c in SettledCountries)
		{
			foreach (int id in c.Fabricating.Keys.ToList())
			{
				Province p = GetProvince(id);
				if (p?.OwnerTag == null || p.OwnerTag == c.Tag || c.Claims.Contains(id))
				{
					c.Fabricating.Remove(id);
					continue;
				}
				c.Fabricating[id] += DiplomacyRules.FabricationPerMonth * (1 + LawRules.Mod(c, "claim_speed"));
				if (c.Fabricating[id] < 100)
					continue;
				c.Fabricating.Remove(id);
				c.Claims.Add(id);
				if (GetCountry(p.OwnerTag) is Country owner)
				{
					DiplomacyRules.AddModifier(owner, c.Tag, "Fabricated claims on our land", DiplomacyRules.FabricationOpinion, Date, 10);
					if (owner.Tag == PlayerTag)
						Notify($"{c.Name} has fabricated a claim on {p.Name}.");
				}
				if (c.Tag == PlayerTag)
					Notify($"Our diplomat has found us a claim on {p.Name}: a cause for war, and cheaper to take.");
			}
			// marriages end with the years
			foreach (string tag in c.Marriages.Where(kv => kv.Value <= Date).Select(kv => kv.Key).ToList())
				c.Marriages.Remove(tag);
		}
	}

	// ------------------------------------------------------------------------------------ guarantees

	public string GuaranteeObstacle(Country us, Country them)
	{
		if (them == null || them == us || them.CapitalId <= 0)
			return "No such country";
		if (us.Guarantees.Contains(them.Tag))
			return "Already guaranteed";
		if (us.OverlordTag != null || them.OverlordTag != null)
			return "Not for vassals";
		if (AtWarWith(us.Tag, them.Tag))
			return "We are at war with them";
		double ours = WarRules.Strength(us, _provinces, _armies.Values, Date), theirs = WarRules.Strength(them, _provinces, _armies.Values, Date);
		if (theirs > ours)
			return "Only a weaker country can be guaranteed";
		return null;
	}

	/// <summary>We promise to defend them: we join their side whenever they are attacked, and they think better of us.</summary>
	public bool Guarantee(string tag, out string reason)
	{
		Country us = PlayerCountry, them = GetCountry(tag);
		reason = us == null ? "No country" : GuaranteeObstacle(us, them);
		if (reason != null)
			return false;
		GuaranteeFor(us, them);
		return true;
	}

	void GuaranteeFor(Country us, Country them)
	{
		us.Guarantees.Add(them.Tag);
		DiplomacyRules.AddModifier(them, us.Tag, "Guarantees our independence", DiplomacyRules.GuaranteeOpinion, Date, 10);
		if (them.Tag == PlayerTag)
			Notify($"{us.Name} guarantees our independence.");
		else if (us.Tag == PlayerTag)
			Notify($"We guarantee {them.Name}: an attack on them is war with us.");
		EmitSignal(SignalName.DiplomacyChanged);
	}

	public void WithdrawGuarantee(string tag)
	{
		Country us = PlayerCountry, them = GetCountry(tag);
		if (us == null || them == null || !us.Guarantees.Remove(tag))
			return;
		them.OpinionModifiers.GetValueOrDefault(us.Tag)?.RemoveAll(m => m.Reason == "Guarantees our independence");
		DiplomacyRules.AddModifier(them, us.Tag, "Withdrew their guarantee", -15, Date, 5);
		EmitSignal(SignalName.DiplomacyChanged);
	}

	// ------------------------------------------------------------------------------------ royal marriages

	public string MarriageObstacle(Country us, Country them)
	{
		if (them == null || them == us || them.CapitalId <= 0)
			return "No such country";
		if (us.Marriages.ContainsKey(them.Tag))
			return "Our houses are joined already";
		if (LawRules.OptionId(us, "government") == "republic" || LawRules.OptionId(them, "government") == "republic")
			return "A republic has no royal house to marry";
		if (us.Rivals.Contains(them.Tag) || them.Rivals.Contains(us.Tag))
			return "Not between rivals";
		if (AtWarWith(us.Tag, them.Tag))
			return "We are at war with them";
		if (DiplomacyRules.Opinion(them, us.Tag) < DiplomacyRules.MarriageOpinionNeeded)
			return $"They must think {DiplomacyRules.MarriageOpinionNeeded} or better of us (now {DiplomacyRules.Opinion(them, us.Tag)})";
		return null;
	}

	/// <summary>A daughter or son wed into their house: opinion rises both ways, and the marriage holds a generation.</summary>
	public bool RoyalMarriage(string tag, out string reason)
	{
		Country us = PlayerCountry, them = GetCountry(tag);
		reason = us == null ? "No country" : MarriageObstacle(us, them);
		if (reason != null)
			return false;
		MarryFor(us, them);
		return true;
	}

	void MarryFor(Country us, Country them)
	{
		GameDate until = Date.AddDays((long)(DiplomacyRules.MarriageYears * 365.2425));
		us.Marriages[them.Tag] = until;
		them.Marriages[us.Tag] = until;
		DiplomacyRules.AddModifier(them, us.Tag, "Royal marriage", DiplomacyRules.MarriageOpinion, Date, DiplomacyRules.MarriageYears);
		DiplomacyRules.AddModifier(us, them.Tag, "Royal marriage", DiplomacyRules.MarriageOpinion, Date, DiplomacyRules.MarriageYears);
		us.Legitimacy = Math.Min(100, us.Legitimacy + 5);
		them.Legitimacy = Math.Min(100, them.Legitimacy + 5);
		if (us.Tag == PlayerTag || them.Tag == PlayerTag)
			Notify($"The houses of {us.Name} and {them.Name} are joined in marriage.");
		EmitSignal(SignalName.DiplomacyChanged);
	}

	public void BreakMarriage(string tag)
	{
		Country us = PlayerCountry, them = GetCountry(tag);
		if (us == null || them == null || !us.Marriages.Remove(tag))
			return;
		them.Marriages.Remove(us.Tag);
		foreach (var (a, b) in new[] { (us, them), (them, us) })
			a.OpinionModifiers.GetValueOrDefault(b.Tag)?.RemoveAll(m => m.Reason == "Royal marriage");
		DiplomacyRules.AddModifier(them, us.Tag, "Repudiated our marriage", -30, Date, 10);
		us.Prestige = Math.Clamp(us.Prestige - 10, -100, 100);
		EmitSignal(SignalName.DiplomacyChanged);
	}

	// ------------------------------------------------------------------------------------ the game's countries

	/// <summary>A country run by the game sets a spare diplomat to forge claims on a rival or a weaker neighbour, guarantees small friends, and marries into friendly houses.</summary>
	void AiActions(Country c)
	{
		if (_rng.NextDouble() > 0.1)
			return;
		var neighbours = SettledCountries.Where(o => o != c && AreNeighbours(c.Tag, o.Tag)).ToList();
		if (FreeDiplomats(c) > 0 && c.Fabricating.Count == 0)
		{
			double ours = WarRules.Strength(c, _provinces, _armies.Values, Date);
			Country mark = neighbours.Where(o => !c.Allies.Contains(o.Tag) && o.OverlordTag != c.Tag && c.OverlordTag != o.Tag && !c.Claims.Any(id => _provinces[id]?.OwnerTag == o.Tag))
				.OrderByDescending(o => c.Rivals.Contains(o.Tag) ? 2 : 0).ThenBy(o => WarRules.Strength(o, _provinces, _armies.Values, Date))
				.FirstOrDefault(o => WarRules.Strength(o, _provinces, _armies.Values, Date) <= ours * 1.2);
			Province target = mark == null ? null : FabricableProvinces(c, mark).OrderByDescending(p => p.TotalUnits).FirstOrDefault();
			if (target != null)
				FabricateClaimFor(c, target, out _);
		}
		foreach (Country o in neighbours)
		{
			if (!c.Guarantees.Contains(o.Tag) && c.Guarantees.Count < 2 && DiplomacyRules.Opinion(c, o.Tag) >= 40 && GuaranteeObstacle(c, o) == null && _rng.NextDouble() < 0.3)
				GuaranteeFor(c, o);
			if (o.Tag != PlayerTag && !c.Marriages.ContainsKey(o.Tag) && c.Marriages.Count < 3 && DiplomacyRules.Opinion(c, o.Tag) >= 20 && MarriageObstacle(c, o) == null && _rng.NextDouble() < 0.3)
				MarryFor(c, o);
		}
	}
}
