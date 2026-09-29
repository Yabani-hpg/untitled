using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Laws: changing them, and what they do month by month (assimilation, conversion, succession).
public partial class GameState
{
	[Signal]
	public delegate void LawsChangedEventHandler();

	/// <summary>A ruler died and another took the throne (or a law changed who rules).</summary>
	[Signal]
	public delegate void RulerChangedEventHandler(string tag);

	/// <summary>The player changes a law. Returns false (with a reason) if the country can't.</summary>
	public bool ChangeLaw(string lawId, string optionId, out string reason)
	{
		Country c = PlayerCountry;
		LawDefinition law = Definitions.GetLaw(lawId);
		LawOption option = law?.GetOption(optionId);
		reason = null;
		if (c == null || option == null)
		{
			reason = "Unknown law";
			return false;
		}
		if (!LawRules.CanChange(c, law, option, this, Definitions, out reason))
			return false;
		LawRules.Change(c, law, option, Date, Definitions);
		Notify($"New law: {law.Name} is now {option.Name}.");
		RefreshFlags();
		EmitSignal(SignalName.LawsChanged);
		EmitSignal(SignalName.ProvinceChanged, c.CapitalId);
		return true;
	}

	/// <summary>The monthly work of the laws in every settled country: assimilation, conversion, and death and succession.</summary>
	void RunLawStep()
	{
		foreach (Country c in _countries.Values)
		{
			if (c.CapitalId <= 0)
				continue;
			LawRules.AssimilationStep(c, ProvincesOf(c.Tag), _rng);
			if (c.Ruler != null && _rng.NextDouble() < LawRules.DeathChance(c.Ruler.AgeOn(Date)))
				Succeed(c);
		}
	}

	/// <summary>The ruler dies; the succession law names the next.</summary>
	void Succeed(Country c)
	{
		Character old = c.Ruler;
		var (age, newDynasty) = LawRules.Successor(c, old, Date, _rng);
		Province capital = GetProvince(c.CapitalId);

		// the heir comes from the old ruler's people, or, chosen by council or priests, from the capital's leading citizens
		PopGroup people = old?.Population(GetProvince(old.ProvinceId));
		if (newDynasty || people == null)
			people = capital?.Pops.Where(g => g.IsMale && LawRules.IsCitizen(c, g)).OrderByDescending(g => g.Units).FirstOrDefault()
				?? capital?.Pops.Where(g => g.IsMale).OrderByDescending(g => g.Units).FirstOrDefault();
		Culture culture = people?.Culture ?? old?.Culture;
		string cultureId = culture?.Id ?? "";

		string name = RegnalName(c, cultureId);
		string dynasty = old?.Dynasty;
		if (newDynasty || dynasty == null)
		{
			var houses = Definitions.DynastyNames.GetValueOrDefault(cultureId);
			dynasty = houses is { Count: > 0 } ? houses[_rng.Next(houses.Count)] : $"House of {name}";
			if (dynasty == old?.Dynasty)
				dynasty = $"House of {name}";
		}
		var portraits = Definitions.GenericPortraits.GetValueOrDefault(cultureId);
		var heir = new Character
		{
			Id = _nextCharacterId++,
			Name = name,
			Dynasty = dynasty,
			Birth = Date.AddDays(-(long)(age * 365.2425) - _rng.Next(365)),
			Portrait = portraits is { Count: > 0 } ? portraits[_rng.Next(portraits.Count)] : old?.Portrait,
			ProvinceId = people != null ? (capital?.Pops.Contains(people) == true ? capital.Id : old?.ProvinceId ?? c.CapitalId) : c.CapitalId,
			Culture = culture,
			Religion = people?.Religion ?? old?.Religion,
			Occupation = people?.Occupation ?? old?.Occupation,
		};
		_characters[heir.Id] = heir;
		c.Ruler = heir;

		if (c.Tag == PlayerTag)
		{
			string how = LawRules.Choice(c, "succession") switch
			{
				"brother" => "His brother",
				"elected" => "Elected by the great men of the realm,",
				"chosen_by_priests" => "Named by the priests,",
				_ => "His son",
			};
			Notify($"{c.RulerTitle} {old?.Name} has died, aged {old?.AgeOn(Date)}. {how} {name} ({age}) takes the throne"
				+ (newDynasty ? $": a new dynasty, the {dynasty}." : "."));
		}
		RefreshFlags();
		EmitSignal(SignalName.RulerChanged, c.Tag);
	}

	/// <summary>A regnal name with its number: the country's names ("Ramesses III"), or its culture's.</summary>
	string RegnalName(Country c, string cultureId)
	{
		List<string> names = c.RulerNames.Count > 0 ? c.RulerNames : Definitions.RulerNames.GetValueOrDefault(cultureId);
		string name = names is { Count: > 0 } ? names[_rng.Next(names.Count)] : "Ruler";
		int n = c.RulerNameCounts.GetValueOrDefault(name) + 1;
		c.RulerNameCounts[name] = n;
		return n > 1 ? $"{name} {Roman(n)}" : name;
	}

	static string Roman(int n)
	{
		var sb = new System.Text.StringBuilder();
		foreach (var (value, numeral) in new[] { (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") })
		{
			while (n >= value)
			{
				sb.Append(numeral);
				n -= value;
			}
		}
		return sb.ToString();
	}
}
