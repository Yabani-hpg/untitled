using System.Collections.Generic;
using System.Text.Json;
using Godot;
using Untitled.Core;

namespace Untitled.Data;

// Areas, hand-authored countries (data/countries/TAG.json: ruler, capital, dynamic flags), ruler names and portraits.
public static partial class DataLoader
{
	public const string AreasPath = "res://data/areas.json";
	public const string CountryDir = "res://data/countries";
	public const string NamesPath = "res://data/names.json";
	public const string PortraitsPath = "res://data/portraits.json";
	/// <summary>Flag of a country without a file of its own: gfx/flags/TAG.png, drawn by tools/draw_flags.py.</summary>
	public const string DefaultFlagPattern = "res://gfx/flags/{0}.png";

	/// <summary>Province ids by name. Names that occur more than once map to -1 (ambiguous).</summary>
	public static Dictionary<string, int> ProvinceNames(Province[] provinces)
	{
		var names = new Dictionary<string, int>();
		foreach (Province p in provinces)
		{
			if (p == null)
				continue;
			names[p.Name] = names.ContainsKey(p.Name) ? -1 : p.Id;
		}
		return names;
	}

	static int ProvinceByName(Dictionary<string, int> names, string name, string where)
	{
		if (name == null || !names.TryGetValue(name, out int id))
			throw new DataException($"{where}: no province named '{name}'");
		if (id < 0)
			throw new DataException($"{where}: more than one province is named '{name}'");
		return id;
	}

	public static void LoadAreas(Definitions defs, Dictionary<string, int> names)
	{
		using JsonDocument doc = ParseJson(AreasPath);
		if (!doc.RootElement.TryGetProperty("areas", out JsonElement areas) || areas.ValueKind != JsonValueKind.Object)
			throw new DataException($"{AreasPath}: expected an object with an 'areas' object");
		foreach (JsonProperty area in areas.EnumerateObject())
		{
			var ids = new List<int>();
			foreach (JsonElement name in area.Value.EnumerateArray())
				ids.Add(ProvinceByName(names, name.GetString(), $"{AreasPath}:{area.Name}"));
			defs.Areas[area.Name] = ids;
		}
	}

	public const string TribesPath = "res://data/tribes.json";

	public static void LoadTribes(Definitions defs, Dictionary<string, int> names)
	{
		using JsonDocument doc = ParseJson(TribesPath);
		JsonElement root = doc.RootElement;
		var taken = new HashSet<int>();
		foreach (var (where, e) in ReadJsonArray(TribesPath, "tribes"))
		{
			var ids = new List<int>();
			foreach (string name in GetStringList(e, "provinces", where))
			{
				int id = ProvinceByName(names, name, where);
				if (!taken.Add(id))
					throw new DataException($"{where}: '{name}' already belongs to another tribe");
				ids.Add(id);
			}
			defs.Tribes.Add(new TribeDefinition(GetString(e, "key", where), GetString(e, "name", where), ids));
		}
		if (root.TryGetProperty("name_syllables", out JsonElement syllables))
		{
			foreach (JsonProperty c in syllables.EnumerateObject())
			{
				string w = $"{TribesPath}:name_syllables.{c.Name}";
				var parts = new[] { GetStringList(c.Value, "start", w), GetStringList(c.Value, "middle", w), GetStringList(c.Value, "end", w) };
				if (parts[0].Count == 0 || parts[2].Count == 0)
					throw new DataException($"{w}: needs 'start' and 'end' syllables");
				if (parts[1].Count == 0)
					parts[1].Add("");
				defs.TribeSyllables[c.Name] = parts;
			}
		}
	}

	public static void LoadNamesAndPortraits(Definitions defs)
	{
		using (JsonDocument doc = ParseJson(NamesPath))
		{
			foreach (var (key, table) in new[] { ("rulers", defs.RulerNames), ("dynasties", defs.DynastyNames) })
			{
				if (!doc.RootElement.TryGetProperty(key, out JsonElement byCulture))
					throw new DataException($"{NamesPath}: missing '{key}'");
				foreach (JsonProperty c in byCulture.EnumerateObject())
					table[c.Name] = GetStringList(byCulture, c.Name, NamesPath);
			}
		}
		using (JsonDocument doc = ParseJson(PortraitsPath))
		{
			if (!doc.RootElement.TryGetProperty("generic", out JsonElement generic))
				throw new DataException($"{PortraitsPath}: missing 'generic'");
			foreach (JsonProperty c in generic.EnumerateObject())
				defs.GenericPortraits[c.Name] = GetStringList(generic, c.Name, PortraitsPath);
		}
	}

	/// <summary>
	/// Gives every country its flags: those listed in data/countries/TAG.json, else the generated
	/// gfx/flags/TAG.png. Also reads the rest of each country file (capital, ruler, start provinces).
	/// </summary>
	public static int LoadCountryFiles(Dictionary<string, Country> countries, Definitions defs, Dictionary<string, int> names)
	{
		int files = 0;
		foreach (string file in DirAccess.GetFilesAt(CountryDir))
		{
			if (!file.EndsWith(".json"))
				continue;
			string path = $"{CountryDir}/{file}";
			using JsonDocument doc = ParseJson(path);
			ParseCountryFile(doc.RootElement, path, countries, defs, names);
			files++;
		}
		foreach (Country c in countries.Values)
		{
			if (c.Flags.Count == 0)
				c.Flags.Add(new FlagDefinition("default", c.Name, string.Format(DefaultFlagPattern, c.Tag), null));
		}
		return files;
	}

	static void ParseCountryFile(JsonElement e, string path, Dictionary<string, Country> countries, Definitions defs, Dictionary<string, int> names)
	{
		string tag = GetString(e, "tag", path);
		if (!countries.TryGetValue(tag, out Country country))
			throw new DataException($"{path}: tag '{tag}' is not in {CountriesPath}");
		if (country.Definition != null)
			throw new DataException($"{path}: {tag} is defined twice");

		string Optional(string key) => e.TryGetProperty(key, out JsonElement v) ? v.GetString() : null;
		country.Name = Optional("name") ?? country.Name;
		if (e.TryGetProperty("gold", out JsonElement gold))
			country.StartingGold = gold.GetDouble();
		if (e.TryGetProperty("color", out _))
			country.MapColor = ParseRgb(e, "color", path);
		country.Adjective = Optional("adjective") ?? country.Name;
		country.Government = Optional("government") ?? country.Government;
		country.RulerTitle = Optional("ruler_title") ?? country.RulerTitle;

		var def = new CountryDefinition
		{
			Tag = tag,
			CapitalProvince = Optional("capital"),
			CapitalName = Optional("capital_name"),
			Ruler = e.TryGetProperty("ruler", out JsonElement r) ? ParseRuler(r, $"{path}:ruler", defs) : null,
		};
		if (def.CapitalProvince != null)
			ProvinceByName(names, def.CapitalProvince, $"{path}:capital");

		if (e.TryGetProperty("start_provinces", out JsonElement start))
		{
			if (start.ValueKind != JsonValueKind.Array)
				throw new DataException($"{path}: 'start_provinces' must be a list of groups");
			int i = 0;
			foreach (JsonElement group in start.EnumerateArray())
			{
				string w = $"{path}:start_provinces[{i++}]";
				string kind = group.TryGetProperty("control", out JsonElement k) ? k.GetString() : "core";
				var control = kind switch
				{
					"core" => ControlKind.Core,
					"absorbed" or "vassal" => ControlKind.Absorbed,
					"subjugated" => ControlKind.Subjugated,
					_ => throw new DataException($"{w}: control must be core, absorbed or subjugated"),
				};
				GameDate? since = group.TryGetProperty("since", out JsonElement s) ? Condition.ParseDate(s.GetString(), $"{w}.since") : null;
				int garrison = (int)GetFloat(group, "garrison", 0f);
				foreach (int id in GroupProvinces(group, w, defs, names))
				{
					if (def.StartProvinces.Exists(sp => sp.ProvinceId == id))
						throw new DataException($"{w}: province {id} is listed twice");
					def.StartProvinces.Add(new StartProvince(id, control, since, garrison));
				}
			}
		}
		if (e.TryGetProperty("allied_tribes", out JsonElement allied))
		{
			int i = 0;
			foreach (JsonElement group in allied.EnumerateArray())
			{
				string w = $"{path}:allied_tribes[{i++}]";
				GameDate since = Condition.ParseDate(GetString(group, "since", w), $"{w}.since");
				string key = GetString(group, "tribe", w);
				if (!defs.Tribes.Exists(t => t.Key == key))
					throw new DataException($"{w}: no tribe '{key}' in {TribesPath}");
				def.AlliedTribes.Add((key, since, (int)GetFloat(group, "relation", 50f)));
			}
		}

		if (e.TryGetProperty("flags", out JsonElement flags))
		{
			int i = 0;
			foreach (JsonElement f in flags.EnumerateArray())
			{
				string w = $"{path}:flags[{i++}]";
				string image = GetString(f, "image", w);
				if (!ResourceLoader.Exists(image))
					throw new DataException($"{w}: image '{image}' not found");
				Condition condition = f.TryGetProperty("condition", out JsonElement c)
					? Condition.Parse(c, $"{w}.condition", id => defs.Areas.TryGetValue(id, out List<int> a) ? a : null,
						name => names.TryGetValue(name, out int pid) ? pid : 0)
					: null;
				country.Flags.Add(new FlagDefinition(GetString(f, "id", w), GetString(f, "name", w), image, condition));
			}
			if (country.Flags.Count == 0 || country.Flags[^1].Condition != null)
				throw new DataException($"{path}: the last flag must have no condition, so there is always one to fly");
		}
		country.Definition = def;
	}

	/// <summary>The provinces of a group written as "areas" and/or "provinces" (names), minus "except".</summary>
	static List<int> GroupProvinces(JsonElement group, string where, Definitions defs, Dictionary<string, int> names)
	{
		var except = new HashSet<int>();
		foreach (string name in GetStringList(group, "except", where))
			except.Add(ProvinceByName(names, name, $"{where}.except"));
		var ids = new List<int>();
		foreach (string area in GetStringList(group, "areas", where))
		{
			if (!defs.Areas.TryGetValue(area, out List<int> areaIds))
				throw new DataException($"{where}: unknown area '{area}'");
			ids.AddRange(areaIds.FindAll(id => !except.Contains(id)));
		}
		foreach (string name in GetStringList(group, "provinces", where))
			ids.Add(ProvinceByName(names, name, where));
		return ids;
	}

	static RulerDefinition ParseRuler(JsonElement e, string where, Definitions defs)
	{
		if (!e.TryGetProperty("population", out JsonElement pop))
			throw new DataException($"{where}: missing 'population' (the province, culture, religion and occupation the ruler belongs to)");
		string culture = GetString(pop, "culture", where);
		string religion = GetString(pop, "religion", where);
		string occupation = GetString(pop, "occupation", where);
		Lookup(defs.Cultures, culture, "culture", where);
		Lookup(defs.Religions, religion, "religion", where);
		Lookup(defs.Occupations, occupation, "occupation", where);
		string portrait = e.TryGetProperty("portrait", out JsonElement p) ? p.GetString() : null;
		if (portrait != null && !ResourceLoader.Exists(portrait))
			throw new DataException($"{where}: portrait '{portrait}' not found");
		return new RulerDefinition
		{
			Name = GetString(e, "name", where),
			FullName = e.TryGetProperty("full_name", out JsonElement fn) ? fn.GetString() : null,
			Dynasty = e.TryGetProperty("dynasty", out JsonElement d) ? d.GetString() : null,
			Birth = Condition.ParseDate(GetString(e, "birth", where), $"{where}.birth"),
			Portrait = portrait,
			Province = GetString(pop, "province", where),
			Culture = culture,
			Religion = religion,
			Occupation = occupation,
		};
	}

	static JsonDocument ParseJson(string path)
	{
		try
		{
			return JsonDocument.Parse(ReadText(path));
		}
		catch (JsonException e)
		{
			throw new DataException($"{path}: {e.Message}");
		}
	}
}
