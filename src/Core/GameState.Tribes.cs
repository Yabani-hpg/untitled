using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Unsettled countries: the tribes and nomads of the land no settled country controls, and the player's
// dealings with them.
public partial class GameState
{
	/// <summary>Tribes, by id. Absorbed and subjugated tribes are gone from it.</summary>
	public IReadOnlyDictionary<int, Tribe> Tribes => _tribes;

	readonly Dictionary<int, Tribe> _tribes = new();
	int _nextTribeId = 1;

	/// <summary>Most provinces and people a generated tribe takes before the next one starts.</summary>
	const int MaxTribeProvinces = 4;
	const int MaxTribeUnits = 80;

	public Tribe GetTribe(int id) => id > 0 && _tribes.TryGetValue(id, out Tribe t) ? t : null;

	public Tribe TribeOf(Province p) => p == null ? null : GetTribe(p.TribeId);

	/// <summary>
	/// Gives every inhabited uncontrolled province to a tribe: first those placed in data/tribes.json,
	/// then the rest grouped into tribes of neighbouring provinces of one culture and way of life.
	/// </summary>
	void CreateTribes()
	{
		_tribes.Clear();
		_nextTribeId = 1;
		var usedNames = new HashSet<string>();
		bool Free(Province p) => p != null && !p.IsWater && p.OwnerTag == null && p.TribeId == 0 && p.Inhabitants != Inhabitants.Empty;

		foreach (TribeDefinition def in Definitions.Tribes)
		{
			var lands = def.Provinces.Select(id => _provinces[id]).Where(Free).ToList();
			if (lands.Count > 0)
				usedNames.Add(NewTribe(lands, def.Name, def.Key).Name);
		}

		foreach (Province start in _provinces)
		{
			if (!Free(start))
				continue;
			// grow a tribe over neighbouring land of the same people
			Culture culture = start.MainCulture;
			Inhabitants way = start.Inhabitants;
			var lands = new List<Province> { start };
			var queue = new Queue<Province>();
			queue.Enqueue(start);
			var seen = new HashSet<int> { start.Id };
			int units = start.TotalUnits;
			while (queue.Count > 0 && lands.Count < MaxTribeProvinces && units < MaxTribeUnits)
			{
				foreach (Adjacency link in queue.Dequeue().Neighbors)
				{
					Province q = link.To;
					if (!link.SharesBorder || !seen.Add(q.Id) || !Free(q) || q.MainCulture != culture || q.Inhabitants != way)
						continue;
					if (lands.Count >= MaxTribeProvinces || units >= MaxTribeUnits)
						break;
					lands.Add(q);
					units += q.TotalUnits;
					queue.Enqueue(q);
				}
			}
			Tribe t = NewTribe(lands, null, null);
			t.Name = TribeName(t, usedNames);
		}
	}

	Tribe NewTribe(List<Province> lands, string name, string key)
	{
		var pops = lands.SelectMany(p => p.Pops).ToList();
		int nomads = pops.Where(g => g.Occupation.Nomadic).Sum(g => g.Units);
		var tribe = new Tribe
		{
			Id = _nextTribeId++,
			Key = key,
			Name = name,
			Kind = 2 * nomads >= pops.Sum(g => g.Units) ? TribeKind.Nomadic : TribeKind.Tribal,
			Culture = pops.GroupBy(g => g.Culture).OrderByDescending(g => g.Sum(x => x.Units)).Select(g => g.Key).FirstOrDefault(),
			Religion = pops.GroupBy(g => g.Religion).OrderByDescending(g => g.Sum(x => x.Units)).Select(g => g.Key).FirstOrDefault(),
			CampProvinceId = lands.OrderByDescending(p => p.TotalUnits).First().Id,
		};
		foreach (Province p in lands)
		{
			p.TribeId = tribe.Id;
			tribe.Provinces.Add(p.Id);
		}
		tribe.Chief = GenerateChief(tribe);
		_tribes[tribe.Id] = tribe;
		return tribe;
	}

	/// <summary>A chief from the tribe's own people, with a name and portrait of its culture.</summary>
	Character GenerateChief(Tribe t)
	{
		Province camp = GetProvince(t.CampProvinceId);
		PopGroup pop = camp.Pops.OrderByDescending(g => g.Units).FirstOrDefault();
		var rng = new Random(StableHash($"chief {t.Key ?? t.CampProvinceId.ToString()}"));
		string Pick(Dictionary<string, List<string>> table) =>
			t.Culture != null && table.TryGetValue(t.Culture.Id, out List<string> list) && list.Count > 0 ? list[rng.Next(list.Count)] : null;
		int age = 25 + rng.Next(35);
		var chief = new Character
		{
			Id = _nextCharacterId++,
			Name = Pick(Definitions.RulerNames) ?? "Chief",
			Birth = Date.AddDays(-(long)(age * 365.2425) - rng.Next(365)),
			Portrait = Pick(Definitions.GenericPortraits),
			ProvinceId = camp.Id,
			Culture = pop?.Culture ?? t.Culture,
			Religion = pop?.Religion ?? t.Religion,
			Occupation = pop?.Occupation,
		};
		_characters[chief.Id] = chief;
		return chief;
	}

	/// <summary>A made-up tribal name from the culture's syllables ("Kamaru"), unique in the world.</summary>
	string TribeName(Tribe t, HashSet<string> used)
	{
		var rng = new Random(StableHash($"tribe {t.CampProvinceId}"));
		if (t.Culture == null || !Definitions.TribeSyllables.TryGetValue(t.Culture.Id, out List<string>[] s))
			return $"Tribe {t.Id}";
		for (int attempt = 0; attempt < 50; attempt++)
		{
			var sb = new StringBuilder(s[0][rng.Next(s[0].Count)]);
			sb.Append(s[1][rng.Next(s[1].Count)]);
			sb.Append(s[2][rng.Next(s[2].Count)]);
			string name = sb.ToString();
			if (used.Add(name))
				return name;
		}
		return $"{s[0][0]}{t.Id}";
	}

	/// <summary>Relations of every tribe with every settled country, from kinship; then the country files' allies.</summary>
	void SetUpTribeRelations()
	{
		var settled = _countries.Values.Where(c => c.CapitalId > 0).ToList();
		foreach (Tribe t in _tribes.Values)
		{
			foreach (Country c in settled)
				t.Relations[c.Tag] = TribeRules.BaseRelation(t, c);
		}
		foreach (Country c in settled)
		{
			foreach (var (key, since, relation) in c.Definition?.AlliedTribes ?? new())
			{
				Tribe t = _tribes.Values.FirstOrDefault(x => x.Key == key);
				if (t == null)
					continue;
				t.AlliedTag = c.Tag;
				t.AlliedSince = since;
				t.Relations[c.Tag] = relation;
			}
		}
	}

	/// <summary>Rebels who threw a country out of a province form a tribe of their own, hostile to it.</summary>
	Tribe FormRebelTribe(Province p, string formerOwner)
	{
		Tribe neighbour = p.Neighbors.Select(l => TribeOf(l.To))
			.FirstOrDefault(t => t != null && t.Culture == p.MainCulture && (t.Kind == TribeKind.Nomadic) == (p.Inhabitants == Inhabitants.Nomads));
		Tribe t;
		if (neighbour != null)
		{
			// they join their kin next door
			p.TribeId = neighbour.Id;
			neighbour.Provinces.Add(p.Id);
			t = neighbour;
		}
		else
		{
			t = NewTribe(new List<Province> { p }, null, null);
			t.Name = TribeName(t, _tribes.Values.Select(x => x.Name).Where(n => n != null).ToHashSet());
			foreach (Country c in _countries.Values.Where(c => c.CapitalId > 0))
				t.Relations[c.Tag] = TribeRules.BaseRelation(t, c);
		}
		TribeRules.ChangeRelation(t, formerOwner, -50);
		return t;
	}

	// ------------------------------------------------------------------------------ player actions

	/// <summary>Sends one of the player's diplomats to court a tribe, or calls them back.</summary>
	public bool SetImprovingRelations(int tribeId, bool on)
	{
		Country c = PlayerCountry;
		Tribe t = GetTribe(tribeId);
		if (c == null || t == null)
			return false;
		if (on && !c.ImprovingRelations.Contains(tribeId) && c.ImprovingRelations.Count >= TribeRules.Diplomats)
			return false;
		if (on)
			c.ImprovingRelations.Add(tribeId);
		else
			c.ImprovingRelations.Remove(tribeId);
		EmitSignal(SignalName.TribesChanged);
		return true;
	}

	public bool ProposeAlliance(int tribeId)
	{
		Country c = PlayerCountry;
		Tribe t = GetTribe(tribeId);
		if (c == null || t == null || !TribeRules.CanProposeAlliance(t, c, _provinces, Date, out _))
			return false;
		bool accepted = TribeRules.ProposeAlliance(t, c, _provinces, Date, _rng);
		Notify(accepted
			? $"{Capital(t.TheName)} accept an alliance with {c.Name}. Their warriors may fight for us, and in time they may join us."
			: $"{Capital(t.TheName)} turn our envoys away. We may ask again in {TribeRules.YearsBeforeAskingAgain} years.");
		EmitSignal(SignalName.TribesChanged);
		return accepted;
	}

	/// <summary>Sends a tribe gifts of gold, which they barter for food; they think better of us.</summary>
	public bool SendGifts(int tribeId)
	{
		Country c = PlayerCountry;
		Tribe t = GetTribe(tribeId);
		if (c == null || t == null || c.Gold < EconomyRules.GiftGold)
			return false;
		c.Gold -= EconomyRules.GiftGold;
		t.Food += EconomyRules.GiftGold * EconomyRules.FoodPerGold;
		TribeRules.ChangeRelation(t, c.Tag, EconomyRules.GiftRelations);
		EmitSignal(SignalName.TribesChanged);
		return true;
	}

	public bool HireMercenaries(int tribeId, int regiments)
	{
		Country c = PlayerCountry;
		Tribe t = GetTribe(tribeId);
		if (c == null || t == null || !TribeRules.CanHire(t, c, _provinces, regiments, out _))
			return false;
		TribeRules.Hire(t, regiments);
		Notify($"{(regiments == 1 ? "A regiment" : $"{regiments} regiments")} of {t.Name} warriors join our army as mercenaries.");
		EmitSignal(SignalName.TribesChanged);
		return true;
	}

	public void DismissMercenaries(int tribeId)
	{
		Tribe t = GetTribe(tribeId);
		if (t == null || t.AlliedTag != PlayerTag)
			return;
		TribeRules.Dismiss(t);
		EmitSignal(SignalName.TribesChanged);
	}

	public bool AbsorbTribe(int tribeId)
	{
		Country c = PlayerCountry;
		Tribe t = GetTribe(tribeId);
		if (c == null || t == null || !TribeRules.CanAbsorb(t, c, _provinces, Date))
			return false;
		int lands = t.Provinces.Count;
		TribeRules.Absorb(t, c, _provinces, Date);
		_tribes.Remove(t.Id);
		c.ImprovingRelations.Remove(t.Id);
		Notify($"{Capital(t.TheName)} join {c.Name}: {lands} provinces come under our rule. Held for {ControlRules.YearsToCore} years, they will be ours for good.");
		AfterTribeLandChange();
		return true;
	}

	public bool SubjugateTribe(int tribeId, int regiments, int mercenaries)
	{
		Country c = PlayerCountry;
		Tribe t = GetTribe(tribeId);
		if (c == null || t == null || !TribeRules.CanSubjugate(t, c, _tribes.Values, _provinces, regiments, mercenaries, out _))
			return false;
		int lands = t.Provinces.Count;
		var r = TribeRules.Subjugate(t, c, _tribes.Values, _provinces, regiments, mercenaries, Date, _rng);
		if (r.AttackerWon)
		{
			_tribes.Remove(t.Id);
			c.ImprovingRelations.Remove(t.Id);
			Notify($"Our army breaks {t.TheName}, losing {r.AttackerLosses} of {regiments + mercenaries} regiments. Their {lands} provinces are subjugated; the survivors stay as garrisons.");
			AfterTribeLandChange();
		}
		else
		{
			Notify($"{Capital(t.TheName)} beat our army: {r.AttackerLosses} of {regiments + mercenaries} regiments lost.");
			EmitSignal(SignalName.TribesChanged);
		}
		return r.AttackerWon;
	}

	void AfterTribeLandChange()
	{
		RefreshFlags();
		EmitSignal(SignalName.TribesChanged);
		EmitSignal(SignalName.OwnershipChanged);
	}

	/// <summary>The monthly tribal step, and messages about the player's allies.</summary>
	void RunTribeStep()
	{
		foreach (var e in TribeRules.MonthlyStep(_tribes.Values, _countries, _provinces, _rng))
		{
			if (e.Kind == TribeRules.EventKind.AllianceEnded && e.Tag == PlayerTag)
				Notify($"{Capital(e.Tribe.TheName)} end their alliance with us.");
		}
		EmitSignal(SignalName.TribesChanged);
	}

	static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
