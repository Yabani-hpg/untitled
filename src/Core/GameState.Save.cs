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
			w.WriteStartArray("techs");
			foreach (string t in c.Techs)
				w.WriteStringValue(t);
			w.WriteEndArray();
			w.WriteStartObject("research_progress");
			foreach (var (t, points) in c.ResearchProgress)
				w.WriteNumber(t, Math.Round(points, 3));
			w.WriteEndObject();
			w.WriteStartArray("research_queue");
			foreach (string t in c.ResearchQueue)
				w.WriteStringValue(t);
			w.WriteEndArray();
			w.WriteNumber("research_stockpile", Math.Round(c.ResearchStockpile, 3));
			w.WriteStartArray("idea_groups");
			foreach (string id in c.IdeaGroups) w.WriteStringValue(id);
			w.WriteEndArray();
			w.WriteStartArray("ideas");
			foreach (string id in c.Ideas) w.WriteStringValue(id);
			w.WriteEndArray();
			w.WriteNumber("idea_points", Math.Round(c.IdeaPoints, 2));
			w.WriteNumber("idea_share", Math.Round(c.IdeaShare, 3));
			w.WriteNumber("next_army", c.NextArmyNumber);
			w.WriteNumber("prestige", Math.Round(c.Prestige, 2));
			w.WriteNumber("stability", c.Stability);
			w.WriteStartArray("loans");
			foreach (Loan l in c.Loans)
			{
				w.WriteStartArray();
				w.WriteNumberValue(Math.Round(l.Amount, 2));
				w.WriteNumberValue(Math.Round(l.Rate, 4));
				w.WriteNumberValue(l.Due.Day);
				w.WriteEndArray();
			}
			w.WriteEndArray();
			if (c.BankruptUntil.Day > 0) w.WriteNumber("bankrupt_until", c.BankruptUntil.Day);
			w.WriteStartObject("estates");
			foreach (var (id, loyalty) in c.EstateLoyalty) w.WriteNumber(id, Math.Round(loyalty, 1));
			w.WriteEndObject();
			w.WriteStartArray("privileges");
			foreach (string id in c.Privileges) w.WriteStringValue(id);
			w.WriteEndArray();
			w.WriteNumber("crownland", Math.Round(c.Crownland, 1));
			if (c.OverlordTag != null)
			{
				w.WriteString("overlord", c.OverlordTag);
				w.WriteString("subject", c.Subject.ToString().ToLowerInvariant());
				w.WriteNumber("vassal_since", c.VassalSince.Day);
			}
			w.WriteNumber("legitimacy", Math.Round(c.Legitimacy, 2));
			w.WriteNumber("war_exhaustion", Math.Round(c.WarExhaustion, 2));
			w.WriteStartArray("event_flags");
			foreach (string f in c.EventFlags) w.WriteStringValue(f);
			w.WriteEndArray();
			w.WriteStartArray("done");
			foreach (string f in c.Done) w.WriteStringValue(f);
			w.WriteEndArray();
			foreach (var (key, list) in new[] { ("advisors", c.Advisors), ("candidates", c.Candidates) })
			{
				w.WriteStartArray(key);
				foreach (Advisor a in list)
				{
					w.WriteStartObject();
					w.WriteNumber("id", a.Id);
					w.WriteString("name", a.Name);
					if (a.Portrait != null) w.WriteString("portrait", a.Portrait);
					w.WriteString("type", a.Type.Id);
					w.WriteNumber("skill", a.Skill);
					w.WriteNumber("birth", a.Birth.Day);
					w.WriteEndObject();
				}
				w.WriteEndArray();
			}
			w.WriteStartArray("institutions");
			foreach (string id in c.Institutions) w.WriteStringValue(id);
			w.WriteEndArray();
			w.WriteStartObject("institution_progress");
			foreach (var (id, p) in c.InstitutionProgress) w.WriteNumber(id, Math.Round(p, 2));
			w.WriteEndObject();
			w.WriteStartObject("disaster_progress");
			foreach (var (id, p) in c.DisasterProgress) w.WriteNumber(id, Math.Round(p, 2));
			w.WriteEndObject();
			w.WriteStartObject("disasters");
			foreach (var (id, since) in c.ActiveDisasters) w.WriteNumber(id, since.Day);
			w.WriteEndObject();
			w.WriteStartArray("reforms");
			foreach (string id in c.Reforms) w.WriteStringValue(id);
			w.WriteEndArray();
			w.WriteNumber("reform_progress", Math.Round(c.ReformProgress, 2));
			w.WriteStartObject("reparations");
			foreach (var (t, until) in c.Reparations) w.WriteNumber(t, until.Day);
			w.WriteEndObject();
			w.WriteStartObject("trade_power_ceded");
			foreach (var (t, until) in c.TradePowerCeded) w.WriteNumber(t, until.Day);
			w.WriteEndObject();
			w.WriteStartObject("fabricating");
			foreach (var (id, progress) in c.Fabricating) w.WriteNumber(id.ToString(), Math.Round(progress, 2));
			w.WriteEndObject();
			w.WriteStartArray("guarantees");
			foreach (string t in c.Guarantees) w.WriteStringValue(t);
			w.WriteEndArray();
			w.WriteStartObject("marriages");
			foreach (var (t, until) in c.Marriages) w.WriteNumber(t, until.Day);
			w.WriteEndObject();
			w.WriteStartArray("rivals");
			foreach (string t in c.Rivals) w.WriteStringValue(t);
			w.WriteEndArray();
			w.WriteStartObject("aggression");
			foreach (var (t, v) in c.Aggression) w.WriteNumber(t, Math.Round(v, 2));
			w.WriteEndObject();
			if (c.CoalitionAgainst != null) w.WriteString("coalition", c.CoalitionAgainst);
			w.WriteStartArray("merchants");
			foreach (Merchant m in c.Merchants)
			{
				w.WriteStartObject();
				w.WriteString("node", m.NodeId);
				w.WriteString("task", m.Task.ToString());
				if (m.ToNodeId != null) w.WriteString("to", m.ToNodeId);
				w.WriteEndObject();
			}
			w.WriteEndArray();
			w.WriteStartObject("opinions");
			foreach (var (tag, o) in c.Opinions)
				w.WriteNumber(tag, o);
			w.WriteEndObject();
			w.WriteStartArray("opinion_modifiers");
			foreach (var (tag, list) in c.OpinionModifiers)
			{
				foreach (OpinionModifier m in list)
				{
					w.WriteStartArray();
					w.WriteStringValue(tag);
					w.WriteStringValue(m.Reason);
					w.WriteNumberValue(m.Amount);
					w.WriteNumberValue(m.Until.Day);
					w.WriteEndArray();
				}
			}
			w.WriteEndArray();
			w.WriteStartArray("allies");
			foreach (string t in c.Allies)
				w.WriteStringValue(t);
			w.WriteEndArray();
			w.WriteStartObject("truces");
			foreach (var (tag, until) in c.Truces)
				w.WriteNumber(tag, until.Day);
			w.WriteEndObject();
			w.WriteStartArray("claims");
			foreach (int id in c.Claims)
				w.WriteNumberValue(id);
			w.WriteEndArray();
			w.WriteStartArray("courting");
			foreach (string t in c.ImprovingCountryRelations)
				w.WriteStringValue(t);
			w.WriteEndArray();
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
			if (a.AboardFleetId != 0) w.WriteNumber("aboard", a.AboardFleetId);
			WriteRegiments(w, "regiments", a.Regiments);
			w.WriteEndObject();
		}
		w.WriteEndArray();

		w.WriteStartArray("pending_events");
		foreach (GameEvent ev in _pendingEvents) w.WriteStringValue(ev.Id);
		w.WriteEndArray();
		w.WriteNumber("next_war", _nextWarId);
		w.WriteStartArray("wars");
		foreach (War war in _wars.Values)
		{
			w.WriteStartObject();
			w.WriteNumber("id", war.Id);
			w.WriteString("attacker", war.AttackerTag);
			w.WriteString("defender", war.DefenderTag);
			w.WriteString("cb", war.CasusBelli.ToString().ToLowerInvariant());
			w.WriteNumber("started", war.Started.Day);
			w.WriteNumber("battle_score", Math.Round(war.BattleScore, 3));
			w.WriteNumber("goal", war.GoalProvinceId);
			w.WriteNumber("ticking", Math.Round(war.TickingScore, 3));
			w.WriteStartArray("attackers");
			foreach (string t in war.Attackers) w.WriteStringValue(t);
			w.WriteEndArray();
			w.WriteStartArray("defenders");
			foreach (string t in war.Defenders) w.WriteStringValue(t);
			w.WriteEndArray();
			w.WriteStartArray("battles");
			foreach (BattleRecord b in war.Battles)
			{
				w.WriteStartArray();
				w.WriteNumberValue(b.Date.Day); w.WriteNumberValue(b.ProvinceId); w.WriteStringValue(b.AttackerTag); w.WriteStringValue(b.DefenderTag);
				w.WriteBooleanValue(b.AttackerWon); w.WriteNumberValue(b.AttackerMen); w.WriteNumberValue(b.DefenderMen); w.WriteNumberValue(b.AttackerLost); w.WriteNumberValue(b.DefenderLost);
				w.WriteEndArray();
			}
			w.WriteEndArray();
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
			if (p.OccupierTag != null) w.WriteString("occupier", p.OccupierTag);
			if (p.SiegerTag != null)
			{
				w.WriteString("sieger", p.SiegerTag);
				w.WriteNumber("siege", Math.Round(p.SiegeProgress, 2));
			}
			if (p.FortLevel > 0) w.WriteNumber("fort", p.FortLevel);
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
			if (e.TryGetProperty("opinions", out JsonElement opinions))
			{
				c.Prestige = e.TryGetProperty("prestige", out JsonElement pr) ? pr.GetDouble() : 0;
				c.Stability = e.TryGetProperty("stability", out JsonElement st) ? st.GetInt32() : 0;
				c.Loans.Clear();
				if (e.TryGetProperty("loans", out JsonElement loans))
					foreach (JsonElement l in loans.EnumerateArray()) c.Loans.Add(new Loan(l[0].GetDouble(), l[1].GetDouble(), new GameDate(l[2].GetInt64())));
				c.BankruptUntil = e.TryGetProperty("bankrupt_until", out JsonElement bu) ? new GameDate(bu.GetInt64()) : default;
				c.EstateLoyalty.Clear();
				if (e.TryGetProperty("estates", out JsonElement est))
					foreach (JsonProperty kv in est.EnumerateObject()) c.EstateLoyalty[kv.Name] = kv.Value.GetDouble();
				c.Privileges.Clear();
				if (e.TryGetProperty("privileges", out JsonElement pv))
					foreach (JsonElement id in pv.EnumerateArray()) c.Privileges.Add(id.GetString());
				c.Crownland = e.TryGetProperty("crownland", out JsonElement cl) ? cl.GetDouble() : EstateRules.StartCrownland;
				c.OverlordTag = e.TryGetProperty("overlord", out JsonElement ov) && _countries.ContainsKey(ov.GetString()) ? ov.GetString() : null;
				c.VassalSince = e.TryGetProperty("vassal_since", out JsonElement vs) ? new GameDate(vs.GetInt64()) : default;
				c.Subject = e.TryGetProperty("subject", out JsonElement sk) && Enum.TryParse(sk.GetString(), true, out SubjectKind kind) ? kind : SubjectKind.Vassal;
				c.Legitimacy = e.TryGetProperty("legitimacy", out JsonElement lg) ? lg.GetDouble() : 100;
				c.WarExhaustion = e.TryGetProperty("war_exhaustion", out JsonElement we) ? we.GetDouble() : 0;
				c.EventFlags.Clear();
				if (e.TryGetProperty("event_flags", out JsonElement ef))
					foreach (JsonElement f in ef.EnumerateArray()) c.EventFlags.Add(f.GetString());
				c.Done.Clear();
				if (e.TryGetProperty("done", out JsonElement dn))
					foreach (JsonElement f in dn.EnumerateArray()) c.Done.Add(f.GetString());
				foreach (var (key, list) in new[] { ("advisors", c.Advisors), ("candidates", c.Candidates) })
				{
					list.Clear();
					if (!e.TryGetProperty(key, out JsonElement arr))
						continue;
					foreach (JsonElement a in arr.EnumerateArray())
					{
						AdvisorType type = Definitions.GetAdvisorType(a.GetProperty("type").GetString());
						if (type == null)
							continue;
						var advisor = new Advisor
						{
							Id = a.GetProperty("id").GetInt32(),
							Name = a.GetProperty("name").GetString(),
							Portrait = a.TryGetProperty("portrait", out JsonElement po) ? po.GetString() : null,
							Type = type,
							Skill = a.GetProperty("skill").GetInt32(),
							Birth = new GameDate(a.GetProperty("birth").GetInt64()),
						};
						list.Add(advisor);
						_nextAdvisorId = Math.Max(_nextAdvisorId, advisor.Id + 1);
					}
				}
				c.Institutions.Clear();
				if (e.TryGetProperty("institutions", out JsonElement ins))
					foreach (JsonElement id in ins.EnumerateArray()) c.Institutions.Add(id.GetString());
				c.InstitutionProgress.Clear();
				if (e.TryGetProperty("institution_progress", out JsonElement ipr))
					foreach (JsonProperty kv in ipr.EnumerateObject()) c.InstitutionProgress[kv.Name] = kv.Value.GetDouble();
				c.DisasterProgress.Clear();
				if (e.TryGetProperty("disaster_progress", out JsonElement dp))
					foreach (JsonProperty kv in dp.EnumerateObject()) c.DisasterProgress[kv.Name] = kv.Value.GetDouble();
				c.ActiveDisasters.Clear();
				if (e.TryGetProperty("disasters", out JsonElement da))
					foreach (JsonProperty kv in da.EnumerateObject()) c.ActiveDisasters[kv.Name] = new GameDate(kv.Value.GetInt64());
				c.Reforms.Clear();
				if (e.TryGetProperty("reforms", out JsonElement rf))
					foreach (JsonElement id in rf.EnumerateArray()) c.Reforms.Add(id.GetString());
				c.ReformProgress = e.TryGetProperty("reform_progress", out JsonElement rp) ? rp.GetDouble() : 0;
				c.Reparations.Clear();
				if (e.TryGetProperty("reparations", out JsonElement rep))
					foreach (JsonProperty kv in rep.EnumerateObject()) c.Reparations[kv.Name] = new GameDate(kv.Value.GetInt64());
				c.TradePowerCeded.Clear();
				if (e.TryGetProperty("trade_power_ceded", out JsonElement tpc))
					foreach (JsonProperty kv in tpc.EnumerateObject()) c.TradePowerCeded[kv.Name] = new GameDate(kv.Value.GetInt64());
				c.Fabricating.Clear();
				if (e.TryGetProperty("fabricating", out JsonElement fb))
					foreach (JsonProperty kv in fb.EnumerateObject()) c.Fabricating[int.Parse(kv.Name)] = kv.Value.GetDouble();
				c.Guarantees.Clear();
				if (e.TryGetProperty("guarantees", out JsonElement gu))
					foreach (JsonElement t in gu.EnumerateArray()) c.Guarantees.Add(t.GetString());
				c.Marriages.Clear();
				if (e.TryGetProperty("marriages", out JsonElement ma))
					foreach (JsonProperty kv in ma.EnumerateObject()) c.Marriages[kv.Name] = new GameDate(kv.Value.GetInt64());
				c.Rivals.Clear();
				if (e.TryGetProperty("rivals", out JsonElement rv))
					foreach (JsonElement t in rv.EnumerateArray()) c.Rivals.Add(t.GetString());
				c.Aggression.Clear();
				if (e.TryGetProperty("aggression", out JsonElement ag))
					foreach (JsonProperty kv in ag.EnumerateObject()) c.Aggression[kv.Name] = kv.Value.GetDouble();
				c.CoalitionAgainst = e.TryGetProperty("coalition", out JsonElement co) ? co.GetString() : null;
				c.Merchants.Clear();
				if (e.TryGetProperty("merchants", out JsonElement ms))
				{
					foreach (JsonElement m in ms.EnumerateArray())
					{
						string node = m.GetProperty("node").GetString();
						if (Definitions.TradeNodes.Exists(n => n.Id == node))
							c.Merchants.Add(new Merchant(node, Enum.Parse<MerchantTask>(m.GetProperty("task").GetString()), m.TryGetProperty("to", out JsonElement to) ? to.GetString() : null));
					}
				}
				c.Opinions.Clear();
				foreach (JsonProperty o in opinions.EnumerateObject())
					c.Opinions[o.Name] = o.Value.GetInt32();
				c.OpinionModifiers.Clear();
				foreach (JsonElement m in e.GetProperty("opinion_modifiers").EnumerateArray())
				{
					if (!c.OpinionModifiers.TryGetValue(m[0].GetString(), out List<OpinionModifier> list))
						c.OpinionModifiers[m[0].GetString()] = list = new List<OpinionModifier>();
					list.Add(new OpinionModifier(m[1].GetString(), m[2].GetInt32(), new GameDate(m[3].GetInt64())));
				}
				c.Allies.Clear();
				foreach (JsonElement t in e.GetProperty("allies").EnumerateArray())
					c.Allies.Add(t.GetString());
				c.Truces.Clear();
				foreach (JsonProperty t in e.GetProperty("truces").EnumerateObject())
					c.Truces[t.Name] = new GameDate(t.Value.GetInt64());
				c.Claims.Clear();
				foreach (JsonElement id in e.GetProperty("claims").EnumerateArray())
					c.Claims.Add(id.GetInt32());
				c.ImprovingCountryRelations.Clear();
				foreach (JsonElement t in e.GetProperty("courting").EnumerateArray())
					c.ImprovingCountryRelations.Add(t.GetString());
			}
			if (e.TryGetProperty("techs", out JsonElement techs))
			{
				// saves from before research keep the new game's knowledge
				c.Techs.Clear();
				c.ResearchProgress.Clear();
				c.ResearchQueue.Clear();
				c.ResearchStockpile = 0;
				foreach (JsonElement t in techs.EnumerateArray())
				{
					if (Definitions.GetTech(t.GetString()) != null)
						c.Techs.Add(t.GetString());
					else
						warnings.Add($"unknown technology '{t.GetString()}'");
				}
				foreach (JsonProperty t in e.GetProperty("research_progress").EnumerateObject())
				{
					if (Definitions.GetTech(t.Name) != null)
						c.ResearchProgress[t.Name] = t.Value.GetDouble();
				}
				c.IdeaGroups.Clear();
				if (e.TryGetProperty("idea_groups", out JsonElement ig))
					foreach (JsonElement id in ig.EnumerateArray()) c.IdeaGroups.Add(id.GetString());
				c.Ideas.Clear();
				if (e.TryGetProperty("ideas", out JsonElement ids))
					foreach (JsonElement id in ids.EnumerateArray()) c.Ideas.Add(id.GetString());
				c.IdeaPoints = e.TryGetProperty("idea_points", out JsonElement ip) ? ip.GetDouble() : 0;
				c.IdeaShare = e.TryGetProperty("idea_share", out JsonElement ish) ? ish.GetDouble() : IdeaRules.DefaultShare;
				if (e.TryGetProperty("research_queue", out JsonElement queue))
				{
					foreach (JsonElement t in queue.EnumerateArray())
					{
						if (Definitions.GetTech(t.GetString()) != null && !c.Techs.Contains(t.GetString()))
							c.ResearchQueue.Add(t.GetString());
					}
				}
				else if (e.TryGetProperty("researching", out JsonElement researching))
				{
					// saves from when each kind of research ran on its own: what each was researching, queued
					foreach (JsonProperty rs in researching.EnumerateObject())
					{
						if (Definitions.GetTech(rs.Value.GetString()) != null)
							c.ResearchQueue.Add(rs.Value.GetString());
					}
				}
				if (e.TryGetProperty("research_stockpile", out JsonElement stock))
					c.ResearchStockpile = stock.ValueKind == JsonValueKind.Number ? stock.GetDouble()
						: stock.EnumerateObject().Sum(st => st.Value.GetDouble());
			}
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
			p.OccupierTag = e.TryGetProperty("occupier", out JsonElement oc) && _countries.ContainsKey(oc.GetString()) ? oc.GetString() : null;
			p.SiegerTag = e.TryGetProperty("sieger", out JsonElement sg) && _countries.ContainsKey(sg.GetString()) ? sg.GetString() : null;
			p.SiegeProgress = p.SiegerTag != null && e.TryGetProperty("siege", out JsonElement sp) ? sp.GetDouble() : 0;
			p.FortLevel = e.TryGetProperty("fort", out JsonElement ft) ? ft.GetInt32() : 0;
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
					AboardFleetId = e.TryGetProperty("aboard", out JsonElement aboard) ? aboard.GetInt32() : 0,
				};
				foreach (JsonElement id in e.GetProperty("path").EnumerateArray())
				{
					if (GetProvince(id.GetInt32()) is Province step)
						a.Path.Add(step.Id);
				}
				a.Regiments.AddRange(ReadRegiments(e.GetProperty("regiments"), warnings));
				if (a.Regiments.Count > 0)
					_armies[a.Id] = a;
			}
			_nextArmyId = save.GetProperty("next_army").GetInt32();
		}
		_pendingEvents.Clear();
		if (save.TryGetProperty("pending_events", out JsonElement pending))
		{
			foreach (JsonElement id in pending.EnumerateArray())
			{
				if (Definitions.GetEvent(id.GetString()) is GameEvent ev)
					_pendingEvents.Add(ev);
			}
		}
		_wars.Clear();
		if (save.TryGetProperty("wars", out JsonElement wars))
		{
			foreach (JsonElement e in wars.EnumerateArray())
			{
				var war = new War
				{
					Id = e.GetProperty("id").GetInt32(),
					AttackerTag = e.GetProperty("attacker").GetString(),
					DefenderTag = e.GetProperty("defender").GetString(),
					CasusBelli = Enum.TryParse(e.GetProperty("cb").GetString(), true, out CasusBelli cb) ? cb : CasusBelli.Conquest,
					Started = new GameDate(e.GetProperty("started").GetInt64()),
					BattleScore = e.GetProperty("battle_score").GetDouble(),
					GoalProvinceId = e.TryGetProperty("goal", out JsonElement goal) ? goal.GetInt32() : 0,
					TickingScore = e.TryGetProperty("ticking", out JsonElement tick) ? tick.GetDouble() : 0,
				};
				if (GetCountry(war.AttackerTag) == null || GetCountry(war.DefenderTag) == null)
					continue;
				foreach (JsonElement t in e.GetProperty("attackers").EnumerateArray()) war.Attackers.Add(t.GetString());
				foreach (JsonElement t in e.GetProperty("defenders").EnumerateArray()) war.Defenders.Add(t.GetString());
				foreach (JsonElement b in e.GetProperty("battles").EnumerateArray())
					war.Battles.Add(new BattleRecord(new GameDate(b[0].GetInt64()), b[1].GetInt32(), b[2].GetString(), b[3].GetString(), b[4].GetBoolean(), b[5].GetInt32(), b[6].GetInt32(), b[7].GetInt32(), b[8].GetInt32()));
				_wars[war.Id] = war;
			}
			_nextWarId = save.GetProperty("next_war").GetInt32();
			foreach (War w in _wars.Values)
				WarRules.UpdateScore(w, _countries, _provinces);
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
			c.LastResearch = TechRules.MonthlyPoints(c, ProvincesOf(c.Tag).ToList(), _armies.Values).Points;
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
