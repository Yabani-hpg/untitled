using System.Collections.Generic;
using System.Linq;
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
	public const string LawsPath = "res://data/laws.json";

	public static void LoadLaws(Definitions defs, Dictionary<string, int> names)
	{
		using JsonDocument doc = ParseJson(LawsPath);
		defs.LawChangeCost = GetFloat(doc.RootElement, "change_cost_gold", 50f);
		defs.LawChangeCooldownYears = GetFloat(doc.RootElement, "change_cooldown_years", 5f);
		var pending = new List<(LawOption, JsonElement, string)>();
		foreach (var (where, e) in ReadJsonArray(LawsPath, "laws"))
		{
			var law = new LawDefinition
			{
				Id = GetString(e, "id", where),
				Name = GetString(e, "name", where),
				Group = GetString(e, "group", where),
				Description = GetString(e, "description", where),
			};
			if (defs.GetLaw(law.Id) != null)
				throw new DataException($"{where}: duplicate law '{law.Id}'");
			int i = 0;
			foreach (JsonElement o in e.GetProperty("options").EnumerateArray())
			{
				string w = $"{where}.options[{i++}]";
				var option = new LawOption
				{
					Law = law,
					Id = GetString(o, "id", w),
					Name = GetString(o, "name", w),
					Description = GetString(o, "description", w),
				};
				if (o.TryGetProperty("modifiers", out JsonElement mods))
				{
					foreach (JsonProperty m in mods.EnumerateObject())
						option.Modifiers[m.Name] = m.Value.GetDouble();
				}
				if (o.TryGetProperty("effects", out JsonElement effects))
				{
					foreach (JsonProperty m in effects.EnumerateObject())
					{
						if (m.Value.ValueKind == JsonValueKind.Number)
							option.Effects[m.Name] = m.Value.GetDouble();
						else
							option.Choices[m.Name] = m.Value.GetString();
					}
				}
				if (o.TryGetProperty("requires", out JsonElement req))
				{
					pending.Add((option, req.Clone(), $"{w}.requires"));
					option.Techs.AddRange(TechsNamed(req));
				}
				if (o.TryGetProperty("requires_text", out JsonElement rt))
					option.RequiresText = rt.GetString();
				law.Options.Add(option);
			}
			law.Default = law.GetOption(GetString(e, "default", where))
				?? throw new DataException($"{where}: default is not one of its options");
			defs.Laws.Add(law);
		}
		// requirements may name other laws, so they are read once all laws are known
		foreach (var (option, req, w) in pending)
		{
			if (option.Techs.FirstOrDefault(t => defs.GetTech(t) == null) is string unknown)
				throw new DataException($"{w}: unknown technology '{unknown}'");
		}
		foreach (var (option, req, w) in pending)
			option.Requires = Condition.Parse(req, w, id => defs.Areas.TryGetValue(id, out List<int> a) ? a : null,
				name => names.TryGetValue(name, out int pid) ? pid : 0);
	}

	public const string UnitsPath = "res://data/units.json";

	public static void LoadUnits(Definitions defs, Dictionary<string, int> names)
	{
		foreach (var (where, e) in ReadJsonArray(UnitsPath, "units"))
		{
			string category = GetString(e, "category", where);
			var type = new UnitType
			{
				Id = GetString(e, "id", where),
				Name = GetString(e, "name", where),
				Category = category switch
				{
					"foot" => UnitCategory.Foot,
					"mounted" => UnitCategory.Mounted,
					"siege" => UnitCategory.Siege,
					_ => throw new DataException($"{where}: category must be foot, mounted or siege"),
				},
				Description = e.TryGetProperty("description", out JsonElement d) ? d.GetString() : "",
				Tech = e.TryGetProperty("tech", out JsonElement tech)
					? defs.GetTech(tech.GetString()) ?? throw new DataException($"{where}: unknown technology '{tech.GetString()}'")
					: null,
				Attack = GetFloat(e, "attack", 1f),
				Defense = System.Math.Max(0.1, GetFloat(e, "defense", 1f)),
				Siege = GetFloat(e, "siege", 0f),
				Speed = System.Math.Max(1, GetFloat(e, "speed", 20f)),
				Cost = GetFloat(e, "cost", 0f),
				Upkeep = GetFloat(e, "upkeep", 0.1f),
				DefaultShare = (int)GetFloat(e, "share", 0f),
			};
			if (defs.GetUnitType(type.Id) != null)
				throw new DataException($"{where}: duplicate unit '{type.Id}'");
			defs.UnitTypes.Add(type);
		}
		if (!defs.UnitTypes.Exists(u => u.Category == UnitCategory.Foot && u.Tech == null))
			throw new DataException($"{UnitsPath}: needs a foot unit available from the start");
	}

	public const string TechsPath = "res://data/techs.json";

	/// <summary>Technology ids a condition names in its { "tech": id } tests, at any depth.</summary>
	static IEnumerable<string> TechsNamed(JsonElement e)
	{
		if (e.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty p in e.EnumerateObject())
			{
				if (p.Name == "tech" && p.Value.ValueKind == JsonValueKind.String)
					yield return p.Value.GetString();
				else
					foreach (string t in TechsNamed(p.Value))
						yield return t;
			}
		}
		else if (e.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement x in e.EnumerateArray())
				foreach (string t in TechsNamed(x))
					yield return t;
		}
	}

	public static void LoadTechs(Definitions defs, Dictionary<string, int> names)
	{
		var pending = new List<(Tech, List<string>, string)>();
		foreach (var (where, e) in ReadJsonArray(TechsPath, "techs"))
		{
			var tech = new Tech
			{
				Id = GetString(e, "id", where),
				Name = GetString(e, "name", where),
				Category = Tech.ParseCategory(GetString(e, "category", where), where),
				Description = e.TryGetProperty("description", out JsonElement d) ? d.GetString() : "",
				Cost = System.Math.Max(1, GetFloat(e, "cost", 100f)),
				ConditionText = e.TryGetProperty("condition_text", out JsonElement ct) ? ct.GetString() : null,
				KnownAtStart = e.TryGetProperty("known_at_start", out JsonElement ks) && ks.GetBoolean(),
			};
			if (defs.GetTech(tech.Id) != null)
				throw new DataException($"{where}: duplicate technology '{tech.Id}'");
			if (e.TryGetProperty("modifiers", out JsonElement mods))
			{
				foreach (JsonProperty m in mods.EnumerateObject())
					tech.Modifiers[m.Name] = m.Value.GetDouble();
			}
			if (e.TryGetProperty("condition", out JsonElement cond))
				tech.Condition = Condition.Parse(cond, $"{where}.condition", id => defs.Areas.TryGetValue(id, out List<int> a) ? a : null,
					name => names.TryGetValue(name, out int pid) ? pid : 0);
			pending.Add((tech, GetStringList(e, "requires", where), where));
			defs.Techs.Add(tech);
		}
		// requirements may come later in the file
		foreach (var (tech, requires, where) in pending)
		{
			foreach (string id in requires)
				tech.Requires.Add(defs.GetTech(id) ?? throw new DataException($"{where}: requires unknown technology '{id}'"));
		}
		// and must not go round in a circle
		var state = new Dictionary<Tech, int>();
		void Visit(Tech t)
		{
			if (state.GetValueOrDefault(t) == 2)
				return;
			if (state.GetValueOrDefault(t) == 1)
				throw new DataException($"{TechsPath}: technology '{t.Id}' requires itself, through its requirements");
			state[t] = 1;
			foreach (Tech r in t.Requires)
				Visit(r);
			state[t] = 2;
		}
		defs.Techs.ForEach(Visit);
	}

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
		if (e.TryGetProperty("laws", out JsonElement laws))
		{
			foreach (JsonProperty l in laws.EnumerateObject())
			{
				LawDefinition law = defs.GetLaw(l.Name) ?? throw new DataException($"{path}:laws: unknown law '{l.Name}'");
				if (law.GetOption(l.Value.GetString()) == null)
					throw new DataException($"{path}:laws: '{l.Value.GetString()}' is not an option of {l.Name}");
				def.Laws[l.Name] = l.Value.GetString();
			}
		}
		foreach (string tech in GetStringList(e, "techs", path))
		{
			if (defs.GetTech(tech) == null)
				throw new DataException($"{path}:techs: unknown technology '{tech}'");
			def.Techs.Add(tech);
		}
		country.RulerNames.AddRange(GetStringList(e, "ruler_names", path));
		if (e.TryGetProperty("ruler_name_counts", out JsonElement counts))
		{
			foreach (JsonProperty n in counts.EnumerateObject())
				country.StartRulerNameCounts[n.Name] = n.Value.GetInt32();
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
