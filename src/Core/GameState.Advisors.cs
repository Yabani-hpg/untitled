using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Advisors: candidates offered each year, hired and dismissed, dying of age.
public partial class GameState
{
	int _nextAdvisorId = 1;

	Advisor NewCandidate(Country c, AdvisorType type)
	{
		Province capital = GetProvince(c.CapitalId);
		PopGroup pop = capital?.Pops.OrderByDescending(g => g.Units).FirstOrDefault();
		string cultureId = pop?.Culture?.Id;
		List<string> names = c.RulerNames.Count > 0 ? c.RulerNames
			: cultureId != null && Definitions.RulerNames.TryGetValue(cultureId, out List<string> list) && list.Count > 0 ? list : null;
		List<string> portraits = cultureId != null && Definitions.GenericPortraits.TryGetValue(cultureId, out List<string> pl) && pl.Count > 0 ? pl : null;
		// skilled advisors are rarer
		double r = _rng.NextDouble();
		int skill = r < 0.55 ? 1 : r < 0.9 ? 2 : 3;
		int age = 30 + _rng.Next(30);
		return new Advisor
		{
			Id = _nextAdvisorId++,
			Name = names != null ? names[_rng.Next(names.Count)] : $"{c.Adjective} advisor",
			Portrait = portraits?[_rng.Next(portraits.Count)],
			Type = type,
			Skill = skill,
			Birth = Date.AddDays(-(long)(age * 365.2425) - _rng.Next(365)),
		};
	}

	/// <summary>Fills the country's pool of candidates: two of each category, of kinds drawn at random.</summary>
	void RefreshCandidates(Country c, bool all)
	{
		foreach (string category in AdvisorRules.Categories)
		{
			var kinds = Definitions.AdvisorTypes.Where(t => t.Category == category).ToList();
			if (kinds.Count == 0)
				continue;
			// each year one candidate of each category moves on
			if (all)
				c.Candidates.RemoveAll(a => a.Type.Category == category);
			else if (c.Candidates.Any(a => a.Type.Category == category))
				c.Candidates.Remove(c.Candidates.First(a => a.Type.Category == category));
			while (c.Candidates.Count(a => a.Type.Category == category) < AdvisorRules.CandidatesPerCategory)
				c.Candidates.Add(NewCandidate(c, kinds[_rng.Next(kinds.Count)]));
		}
	}

	public string HireObstacle(Country c, Advisor a)
	{
		if (c == null || a == null || !c.Candidates.Contains(a))
			return "No such candidate";
		if (AdvisorRules.Hired(c, a.Type.Category) != null)
			return $"A {a.Type.Category} advisor serves already: dismiss them first";
		if (c.Gold < AdvisorRules.Salary(a) * 3)
			return $"We should have {AdvisorRules.Salary(a) * 3:0} gold in hand to pay {AdvisorRules.Salary(a):0} a month";
		return null;
	}

	public bool HireAdvisor(int id, out string reason) => HireAdvisorFor(PlayerCountry, PlayerCountry?.Candidates.FirstOrDefault(a => a.Id == id), out reason);

	bool HireAdvisorFor(Country c, Advisor a, out string reason)
	{
		reason = HireObstacle(c, a);
		if (reason != null)
			return false;
		c.Candidates.Remove(a);
		c.Advisors.Add(a);
		if (c.Tag == PlayerTag)
			Notify($"{a.Name} joins the court as {a.Type.Name.ToLowerInvariant()}: {AdvisorRules.Describe(a)}.");
		EmitSignal(SignalName.LawsChanged);
		return true;
	}

	public void DismissAdvisor(int id)
	{
		Country c = PlayerCountry;
		Advisor a = c?.Advisors.FirstOrDefault(x => x.Id == id);
		if (a == null)
			return;
		c.Advisors.Remove(a);
		EmitSignal(SignalName.LawsChanged);
	}

	/// <summary>A month: advisors age and may die; each January new candidates present themselves; the game's countries hire.</summary>
	void RunAdvisorStep()
	{
		foreach (Country c in SettledCountries)
		{
			foreach (Advisor a in c.Advisors.ToList())
			{
				if (_rng.NextDouble() < LawRules.DeathChance(a.AgeOn(Date)))
				{
					c.Advisors.Remove(a);
					if (c.Tag == PlayerTag)
						Notify($"{a.Name}, our {a.Type.Name.ToLowerInvariant()}, has died.");
				}
			}
			if (Date.Holocene.Month == 1 || c.Candidates.Count == 0)
				RefreshCandidates(c, c.Candidates.Count == 0);
			if (c.Tag == PlayerTag || _rng.NextDouble() > 0.2)
				continue;
			// the game's countries hire the best they can afford into a free slot
			foreach (string category in AdvisorRules.Categories)
			{
				if (AdvisorRules.Hired(c, category) != null)
					continue;
				Advisor best = c.Candidates.Where(a => a.Type.Category == category && c.LastLedger.Income > AdvisorRules.Salary(a) * 4).OrderByDescending(a => a.Skill).FirstOrDefault();
				if (best != null)
					HireAdvisorFor(c, best, out _);
			}
		}
	}
}
