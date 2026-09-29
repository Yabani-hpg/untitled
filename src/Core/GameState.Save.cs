using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// What a save file holds, and loading it back. The files themselves are handled by SaveGames.
public partial class GameState
{
	/// <summary>Writes the whole game: date, player, world flags, characters, countries and provinces.</summary>
	public void WriteSave(Utf8JsonWriter w)
	{
		w.WriteStartObject();
		w.WriteNumber("date", Date.Day);
		w.WriteNumber("speed", Speed);
		w.WriteString("player", PlayerTag);
		w.WriteNumber("next_character", _nextCharacterId);

		w.WriteStartArray("world_flags");
		foreach (string f in _worldFlags)
			w.WriteStringValue(f);
		w.WriteEndArray();

		w.WriteStartArray("characters");
		foreach (Character c in _characters.Values)
		{
			w.WriteStartObject();
			w.WriteNumber("id", c.Id);
			w.WriteString("name", c.Name);
			if (c.FullName != null) w.WriteString("full_name", c.FullName);
			if (c.Dynasty != null) w.WriteString("dynasty", c.Dynasty);
			w.WriteNumber("birth", c.Birth.Day);
			if (c.Portrait != null) w.WriteString("portrait", c.Portrait);
			w.WriteNumber("province", c.ProvinceId);
			w.WriteString("culture", c.Culture?.Id);
			w.WriteString("religion", c.Religion?.Id);
			w.WriteString("occupation", c.Occupation?.Id);
			if (c.Sex == Sex.Female) w.WriteString("sex", "female");
			w.WriteEndObject();
		}
		w.WriteEndArray();

		w.WriteStartArray("countries");
		foreach (Country c in _countries.Values)
		{
			w.WriteStartObject();
			w.WriteString("tag", c.Tag);
			w.WriteNumber("capital", c.CapitalId);
			if (c.CapitalName != null) w.WriteString("capital_name", c.CapitalName);
			if (c.Ruler != null) w.WriteNumber("ruler", c.Ruler.Id);
			w.WriteNumber("next_army", c.NextArmyNumber);
			w.WriteStartObject("levy_template");
			foreach (var (unit, share) in c.LevyTemplate)
				w.WriteNumber(unit, share);
			w.WriteEndObject();
			w.WriteNumber("gold", c.Gold);
			w.WriteStartObject("laws");
			foreach (var (law, option) in c.Laws)
				w.WriteString(law, option);
			w.WriteEndObject();
			w.WriteStartObject("laws_changed");
			foreach (var (law, date) in c.LawChanged)
				w.WriteNumber(law, date.Day);
			w.WriteEndObject();
			w.WriteStartObject("ruler_names");
			foreach (var (name, n) in c.RulerNameCounts)
				w.WriteNumber(name, n);
			w.WriteEndObject();
			w.WriteStartArray("improving_relations");
			foreach (int id in c.ImprovingRelations)
				w.WriteNumberValue(id);
			w.WriteEndArray();
			w.WriteEndObject();
		}
		w.WriteEndArray();

		w.WriteNumber("next_army", _nextArmyId);
		w.WriteStartArray("armies");
		foreach (Army a in _armies.Values)
		{
			w.WriteStartObject();
			w.WriteNumber("id", a.Id);
			w.WriteString("owner", a.OwnerTag);
			w.WriteString("name", a.Name);
			w.WriteNumber("province", a.ProvinceId);
			w.WriteStartArray("path");
			foreach (int id in a.Path)
				w.WriteNumberValue(id);
			w.WriteEndArray();
			w.WriteNumber("marched", a.DaysMarched);
			w.WriteNumber("step_days", a.StepDays);
			WriteRegiments(w, "regiments", a.Regiments);
			w.WriteEndObject();
		}
		w.WriteEndArray();

		w.WriteNumber("next_tribe", _nextTribeId);
		w.WriteStartArray("tribes");
		foreach (Tribe t in _tribes.Values)
		{
			w.WriteStartObject();
			w.WriteNumber("id", t.Id);
			if (t.Key != null) w.WriteString("key", t.Key);
			w.WriteString("name", t.Name);
			w.WriteString("kind", t.Kind == TribeKind.Nomadic ? "nomadic" : "tribal");
			w.WriteString("culture", t.Culture?.Id);
			w.WriteString("religion", t.Religion?.Id);
			if (t.Chief != null) w.WriteNumber("chief", t.Chief.Id);
			w.WriteNumber("camp", t.CampProvinceId);
			w.WriteStartArray("provinces");
			foreach (int id in t.Provinces)
				w.WriteNumberValue(id);
			w.WriteEndArray();
			w.WriteStartObject("relations");
			foreach (var (tag, r) in t.Relations)
				w.WriteNumber(tag, r);
			w.WriteEndObject();
			if (t.AlliedTag != null)
			{
				w.WriteString("allied", t.AlliedTag);
				w.WriteNumber("allied_since", t.AlliedSince.Day);
			}
			if (t.RefusedTag != null)
			{
				w.WriteString("refused", t.RefusedTag);
				w.WriteNumber("refused_until", t.RefusedUntil.Day);
			}
			w.WriteNumber("hired", t.HiredRegiments);
			w.WriteNumber("food", t.Food);
			w.WriteEndObject();
		}
		w.WriteEndArray();

		w.WriteStartArray("provinces");
		foreach (Province p in _provinces)
		{
			if (p == null || p.IsWater)
				continue;
			w.WriteStartObject();
			w.WriteNumber("id", p.Id);
			if (p.OwnerTag != null) w.WriteString("owner", p.OwnerTag);
			if (p.Control != null)
			{
				w.WriteStartObject("control");
				w.WriteString("kind", p.Control.Kind.ToString().ToLowerInvariant());
				w.WriteNumber("since", p.Control.Since.Day);
				WriteRegiments(w, "garrison", p.Control.Garrison);
				w.WriteEndObject();
			}
			if (p.TribeId != 0) w.WriteNumber("tribe", p.TribeId);
			w.WriteStartArray("features");
			foreach (string f in p.Features)
				w.WriteStringValue(f);
			w.WriteEndArray();
			if (p.NonRenewable != null) w.WriteString("non_renewable", p.NonRenewable.Id);
			if (p.Food != null) w.WriteString("food", p.Food.Id);
			w.WriteStartArray("pops");
			foreach (PopGroup pop in p.Pops)
			{
				w.WriteStartArray();
				w.WriteStringValue(pop.Culture.Id);
				w.WriteStringValue(pop.Religion.Id);
				w.WriteStringValue(pop.Occupation.Id);
				w.WriteNumberValue(pop.Units);
				w.WriteStringValue(pop.IsMale ? "m" : "f");
				w.WriteEndArray();
			}
			w.WriteEndArray();
			w.WriteStartArray("buildings");
			foreach (Building b in p.Buildings)
			{
				w.WriteStartArray();
				w.WriteStringValue(b.Type.Id);
				w.WriteNumberValue(b.Level);
				w.WriteBooleanValue(b.BuiltByPopulation);
				w.WriteEndArray();
			}
			w.WriteEndArray();
			w.WriteEndObject();
		}
		w.WriteEndArray();
		w.WriteEndObject();
	}

	/// <summary>
	/// Replaces the game with a saved one and enters the playing phase. Anything the save names that the
	/// data no longer has (a removed culture, building or province) is skipped with a warning.
	/// </summary>
	public void ReadSave(JsonElement save)
	{
		NewGame();
		var warnings = new List<string>();
		var pendingGarrisons = new List<(Province, int)>();
		T Def<T>(Dictionary<string, T> table, JsonElement e, string key) where T : class
		{
			if (!e.TryGetProperty(key, out JsonElement v) || v.ValueKind != JsonValueKind.String)
				return null;
			if (table.TryGetValue(v.GetString(), out T value))
				return value;
			warnings.Add($"unknown {key} '{v.GetString()}'");
			return null;
		}

		Date = new GameDate(save.GetProperty("date").GetInt64());
		Speed = Math.Clamp(save.GetProperty("speed").GetInt32(), MinSpeed, MaxSpeed);
		foreach (JsonElement f in save.GetProperty("world_flags").EnumerateArray())
			_worldFlags.Add(f.GetString());

		_characters.Clear();
		foreach (JsonElement e in save.GetProperty("characters").EnumerateArray())
		{
			var c = new Character
			{
				Id = e.GetProperty("id").GetInt32(),
				Name = e.GetProperty("name").GetString(),
				FullName = e.TryGetProperty("full_name", out JsonElement fn) ? fn.GetString() : null,
				Dynasty = e.TryGetProperty("dynasty", out JsonElement dy) ? dy.GetString() : null,
				Birth = new GameDate(e.GetProperty("birth").GetInt64()),
				Portrait = e.TryGetProperty("portrait", out JsonElement po) ? po.GetString() : null,
				ProvinceId = e.GetProperty("province").GetInt32(),
				Culture = Def(Definitions.Cultures, e, "culture"),
				Religion = Def(Definitions.Religions, e, "religion"),
				Occupation = Def(Definitions.Occupations, e, "occupation"),
				Sex = e.TryGetProperty("sex", out JsonElement sx) && sx.GetString() == "female" ? Sex.Female : Sex.Male,
			};
			_characters[c.Id] = c;
		}
		_nextCharacterId = save.GetProperty("next_character").GetInt32();

		foreach (JsonElement e in save.GetProperty("countries").EnumerateArray())
		{
			Country c = GetCountry(e.GetProperty("tag").GetString());
			if (c == null)
			{
				warnings.Add($"unknown country '{e.GetProperty("tag").GetString()}'");
				continue;
			}
			c.CapitalId = e.GetProperty("capital").GetInt32();
			c.CapitalName = e.TryGetProperty("capital_name", out JsonElement cn) ? cn.GetString() : null;
			c.Ruler = e.TryGetProperty("ruler", out JsonElement r) && _characters.TryGetValue(r.GetInt32(), out Character ruler) ? ruler : null;
			c.NextArmyNumber = e.TryGetProperty("next_army", out JsonElement na) ? na.GetInt32() : 1;
			c.LevyTemplate.Clear();
			if (e.TryGetProperty("levy_template", out JsonElement lt))
			{
				foreach (JsonProperty u in lt.EnumerateObject())
				{
					if (Definitions.GetUnitType(u.Name) != null)
						c.LevyTemplate[u.Name] = u.Value.GetInt32();
				}
			}
			c.Gold = e.TryGetProperty("gold", out JsonElement gd) ? gd.GetDouble() : c.StartingGold;
			if (e.TryGetProperty("laws", out JsonElement laws))
			{
				foreach (JsonProperty l in laws.EnumerateObject())
				{
					if (Definitions.GetLaw(l.Name)?.GetOption(l.Value.GetString()) != null)
						c.Laws[l.Name] = l.Value.GetString();
					else
						warnings.Add($"unknown law {l.Name}: {l.Value.GetString()}");
				}
			}
			c.LawChanged.Clear();
			if (e.TryGetProperty("laws_changed", out JsonElement changed))
			{
				foreach (JsonProperty l in changed.EnumerateObject())
					c.LawChanged[l.Name] = new GameDate(l.Value.GetInt64());
			}
			if (e.TryGetProperty("ruler_names", out JsonElement rn))
			{
				c.RulerNameCounts.Clear();
				foreach (JsonProperty n in rn.EnumerateObject())
					c.RulerNameCounts[n.Name] = n.Value.GetInt32();
			}
			c.ImprovingRelations.Clear();
			if (e.TryGetProperty("improving_relations", out JsonElement ir))
			{
				foreach (JsonElement id in ir.EnumerateArray())
					c.ImprovingRelations.Add(id.GetInt32());
			}
		}

		foreach (JsonElement e in save.GetProperty("provinces").EnumerateArray())
		{
			Province p = GetProvince(e.GetProperty("id").GetInt32());
			if (p == null)
			{
				warnings.Add($"unknown province {e.GetProperty("id").GetInt32()}");
				continue;
			}
			string owner = e.TryGetProperty("owner", out JsonElement o) ? o.GetString() : null;
			p.OwnerTag = owner != null && _countries.ContainsKey(owner) ? owner : null;
			p.Control = null;
			if (p.OwnerTag != null)
			{
				// saves from before control existed: what a country held was its core
				p.Control = new ProvinceControl { Kind = ControlKind.Core, Since = StartDate };
				if (e.TryGetProperty("control", out JsonElement ctl))
				{
					p.Control.Kind = Enum.TryParse(ctl.GetProperty("kind").GetString(), true, out ControlKind kind) ? kind : ControlKind.Core;
					p.Control.Since = new GameDate(ctl.GetProperty("since").GetInt64());
					JsonElement garrison = ctl.GetProperty("garrison");
					if (garrison.ValueKind == JsonValueKind.Array)
						p.Control.Garrison.AddRange(ReadRegiments(garrison, warnings));
					else
						pendingGarrisons.Add((p, garrison.GetInt32()));   // a save from before regiments: raised again below
				}
			}
			if (save.TryGetProperty("tribes", out _))
				p.TribeId = e.TryGetProperty("tribe", out JsonElement tr) ? tr.GetInt32() : 0;
			p.Features.Clear();
			foreach (JsonElement f in e.GetProperty("features").EnumerateArray())
				p.Features.Add(f.GetString());
			p.NonRenewable = Def(Definitions.Resources, e, "non_renewable");
			p.Food = Def(Definitions.Resources, e, "food");
			p.Pops.Clear();
			foreach (JsonElement pop in e.GetProperty("pops").EnumerateArray())
			{
				if (!Definitions.Cultures.TryGetValue(pop[0].GetString(), out Culture culture)
					|| !Definitions.Religions.TryGetValue(pop[1].GetString(), out Religion religion)
					|| !Definitions.Occupations.TryGetValue(pop[2].GetString(), out Occupation occupation))
				{
					warnings.Add($"province {p.Id}: unknown population {pop}");
					continue;
				}
				// saves from before men and women were counted apart: half and half
				if (pop.GetArrayLength() > 4)
					p.AddPops(culture, religion, occupation, pop[4].GetString() == "f" ? Sex.Female : Sex.Male, pop[3].GetInt32());
				else
					p.AddPeople(culture, religion, occupation, pop[3].GetInt32(), (p.Id + p.Pops.Count) % 2 == 0);
			}
			p.Buildings.Clear();
			foreach (JsonElement b in e.GetProperty("buildings").EnumerateArray())
			{
				BuildingType type = Definitions.GetBuilding(b[0].GetString());
				if (type == null)
				{
					warnings.Add($"province {p.Id}: unknown building '{b[0].GetString()}'");
					continue;
				}
				p.Buildings.Add(new Building(type, b[1].GetInt32(), b[2].GetBoolean()));
			}
		}

		if (save.TryGetProperty("tribes", out JsonElement tribes))
		{
			_tribes.Clear();
			foreach (JsonElement e in tribes.EnumerateArray())
			{
				var t = new Tribe
				{
					Id = e.GetProperty("id").GetInt32(),
					Key = e.TryGetProperty("key", out JsonElement k) ? k.GetString() : null,
					Name = e.GetProperty("name").GetString(),
					Kind = e.GetProperty("kind").GetString() == "nomadic" ? TribeKind.Nomadic : TribeKind.Tribal,
					Culture = Def(Definitions.Cultures, e, "culture"),
					Religion = Def(Definitions.Religions, e, "religion"),
					Chief = e.TryGetProperty("chief", out JsonElement ch) && _characters.TryGetValue(ch.GetInt32(), out Character chief) ? chief : null,
					CampProvinceId = e.GetProperty("camp").GetInt32(),
					AlliedTag = e.TryGetProperty("allied", out JsonElement al) ? al.GetString() : null,
					AlliedSince = e.TryGetProperty("allied_since", out JsonElement als) ? new GameDate(als.GetInt64()) : default,
					RefusedTag = e.TryGetProperty("refused", out JsonElement rf) ? rf.GetString() : null,
					RefusedUntil = e.TryGetProperty("refused_until", out JsonElement rfu) ? new GameDate(rfu.GetInt64()) : default,
					HiredRegiments = e.GetProperty("hired").GetInt32(),
					Food = e.TryGetProperty("food", out JsonElement fd) ? fd.GetDouble() : 0,
				};
				foreach (JsonElement id in e.GetProperty("provinces").EnumerateArray())
					t.Provinces.Add(id.GetInt32());
				foreach (JsonProperty r in e.GetProperty("relations").EnumerateObject())
					t.Relations[r.Name] = r.Value.GetInt32();
				_tribes[t.Id] = t;
			}
			_nextTribeId = save.GetProperty("next_tribe").GetInt32();
		}
		else
		{
			// a save from before tribes: keep the new game's tribes, minus land a country holds
			foreach (Tribe t in _tribes.Values)
				t.Provinces.RemoveAll(id => _provinces[id].OwnerTag != null);
			foreach (Province p in _provinces)
			{
				if (p != null && p.OwnerTag != null)
					p.TribeId = 0;
			}
		}

		_armies.Clear();
		if (save.TryGetProperty("armies", out JsonElement armies))
		{
			foreach (JsonElement e in armies.EnumerateArray())
			{
				string owner = e.GetProperty("owner").GetString();
				Province at = GetProvince(e.GetProperty("province").GetInt32());
				if (GetCountry(owner) == null || at == null)
				{
					warnings.Add($"army of unknown country '{owner}' or in an unknown province");
					continue;
				}
				var a = new Army
				{
					Id = e.GetProperty("id").GetInt32(),
					OwnerTag = owner,
					Name = e.GetProperty("name").GetString(),
					ProvinceId = at.Id,
					DaysMarched = e.GetProperty("marched").GetInt32(),
					StepDays = e.GetProperty("step_days").GetInt32(),
				};
				foreach (JsonElement id in e.GetProperty("path").EnumerateArray())
				{
					if (GetProvince(id.GetInt32()) is Province step && !step.IsWater)
						a.Path.Add(step.Id);
				}
				a.Regiments.AddRange(ReadRegiments(e.GetProperty("regiments"), warnings));
				if (a.Regiments.Count > 0)
					_armies[a.Id] = a;
			}
			_nextArmyId = save.GetProperty("next_army").GetInt32();
		}
		foreach (var (p, n) in pendingGarrisons)
		{
			Country owner = GetCountry(p.OwnerTag);
			var types = Enumerable.Repeat(MilitaryRules.BasicFoot(Definitions), n).ToList();
			p.Control.Garrison.AddRange(MilitaryRules.Levy(owner, ProvincesOf(owner.Tag).ToList(), types, Date));
		}

		foreach (Country c in _countries.Values)
		{
			c.CurrentFlag = null;
			c.LastLedger = EconomyRules.MonthlyLedger(c, ProvincesOf(c.Tag), _tribes.Values, _armies.Values, Date);
		}
		foreach (Tribe t in _tribes.Values)
			t.LastFood = EconomyRules.MonthlyFood(t, _provinces, Definitions);
		RefreshFlags(emit: false);
		PlayerTag = save.GetProperty("player").GetString();
		FocusedCountryTag = PlayerTag;
		Phase = PlayerTag != null ? GamePhase.Playing : GamePhase.CountrySelection;
		foreach (string warning in warnings.GetRange(0, Math.Min(warnings.Count, 20)))
			GD.PushWarning($"Loading save: {warning}");
	}

	static void WriteRegiments(Utf8JsonWriter w, string key, IEnumerable<Regiment> regiments)
	{
		w.WriteStartArray(key);
		foreach (Regiment r in regiments)
		{
			w.WriteStartArray();
			w.WriteStringValue(r.Type.Id);
			w.WriteNumberValue(Math.Round(r.Strength, 4));
			w.WriteNumberValue(r.HomeProvinceId);
			w.WriteStringValue(r.Culture?.Id);
			w.WriteStringValue(r.Religion?.Id);
			w.WriteStringValue(r.Occupation?.Id);
			w.WriteStringValue(r.Sex == Sex.Female ? "f" : "m");
			w.WriteEndArray();
		}
		w.WriteEndArray();
	}

	/// <summary>Regiments as <see cref="WriteRegiments"/> wrote them; those of a unit type the data no longer has are armed as basic foot.</summary>
	List<Regiment> ReadRegiments(JsonElement array, List<string> warnings)
	{
		var list = new List<Regiment>();
		foreach (JsonElement e in array.EnumerateArray())
		{
			UnitType type = Definitions.GetUnitType(e[0].GetString());
			if (type == null)
			{
				warnings.Add($"unknown unit type '{e[0].GetString()}'");
				type = MilitaryRules.BasicFoot(Definitions);
			}
			list.Add(new Regiment
			{
				Type = type,
				Strength = e[1].GetDouble(),
				HomeProvinceId = e[2].GetInt32(),
				Culture = e[3].GetString() is string cu && Definitions.Cultures.TryGetValue(cu, out Culture culture) ? culture : null,
				Religion = e[4].GetString() is string re && Definitions.Religions.TryGetValue(re, out Religion religion) ? religion : null,
				Occupation = e[5].GetString() is string oc && Definitions.Occupations.TryGetValue(oc, out Occupation occupation) ? occupation : null,
				Sex = e[6].GetString() == "f" ? Sex.Female : Sex.Male,
			});
		}
		return list;
	}
}
