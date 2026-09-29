using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Untitled.Data;

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
				p.AddPops(culture, religion, occupation, pop[3].GetInt32());
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

		foreach (Country c in _countries.Values)
			c.CurrentFlag = null;
		RefreshFlags(emit: false);
		PlayerTag = save.GetProperty("player").GetString();
		FocusedCountryTag = PlayerTag;
		Phase = PlayerTag != null ? GamePhase.Playing : GamePhase.CountrySelection;
		foreach (string warning in warnings.GetRange(0, Math.Min(warnings.Count, 20)))
			GD.PushWarning($"Loading save: {warning}");
	}
}
