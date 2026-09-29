using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Untitled.Data;

// Definitions of cultures, religions, occupations, resources and buildings, and each province's starting
// populations and production (data/province_setup.json, from tools/generate_setup.py).
public static partial class DataLoader
{
	public const string ReligionsPath = "res://data/religions.json";
	public const string LanguagesPath = "res://data/languages.json";
	public const string CulturesPath = "res://data/cultures.json";
	public const string OccupationsPath = "res://data/occupations.json";
	public const string ResourcesPath = "res://data/resources.json";
	public const string BuildingsPath = "res://data/buildings.json";
	public const string ProvinceSetupPath = "res://data/province_setup.json";
	public const string CalendarsPath = "res://data/calendars.json";

	public static Definitions LoadDefinitions()
	{
		var defs = new Definitions();

		foreach (var (where, e) in ReadJsonArray(ReligionsPath, null))
		{
			var r = new Religion(GetString(e, "id", where), GetString(e, "name", where), ParseHexColor(GetString(e, "color", where), where));
			AddUnique(defs.Religions, r.Id, r, where);
		}

		foreach (var (where, e) in ReadJsonArray(LanguagesPath, "families"))
		{
			var f = new LanguageFamily(GetString(e, "id", where), GetString(e, "name", where));
			AddUnique(defs.LanguageFamilies, f.Id, f, where);
		}
		foreach (var (where, e) in ReadJsonArray(LanguagesPath, "languages"))
		{
			var l = new Language(GetString(e, "id", where), GetString(e, "name", where),
				Lookup(defs.LanguageFamilies, GetString(e, "family", where), "language family", where));
			AddUnique(defs.Languages, l.Id, l, where);
		}

		foreach (var (where, e) in ReadJsonArray(CulturesPath, null))
		{
			var c = new Culture(GetString(e, "id", where), GetString(e, "name", where), ParseHexColor(GetString(e, "color", where), where),
				Lookup(defs.Languages, GetString(e, "language", where), "language", where), GetString(e, "art_style", where));
			AddUnique(defs.Cultures, c.Id, c, where);
		}

		foreach (var (where, e) in ReadJsonArray(OccupationsPath, null))
		{
			bool nomadic = e.TryGetProperty("nomadic", out JsonElement n) && n.ValueKind == JsonValueKind.True;
			var o = new Occupation(GetString(e, "id", where), GetString(e, "name", where), nomadic, ParseHexColor(GetString(e, "color", where), where));
			AddUnique(defs.Occupations, o.Id, o, where);
		}

		foreach (var (where, e) in ReadJsonArray(ResourcesPath, "resources"))
		{
			string category = GetString(e, "category", where);
			var workedBy = new List<Occupation>();
			foreach (string id in GetStringList(e, "worked_by", where))
				workedBy.Add(Lookup(defs.Occupations, id, "occupation", where));
			var r = new ResourceType
			{
				Id = GetString(e, "id", where),
				Name = GetString(e, "name", where),
				Color = ParseHexColor(GetString(e, "color", where), where),
				Category = category switch
				{
					"non_renewable" => ResourceCategory.NonRenewable,
					"food" => ResourceCategory.Food,
					"produced" => ResourceCategory.Produced,
					_ => throw new DataException($"{where}: unknown category '{category}'"),
				},
				FoodYield = GetFloat(e, "food_yield", 0f),
				WorkedBy = workedBy,
				Origin = e.TryGetProperty("origin", out JsonElement origin) ? origin.GetString() : null,
			};
			if (r.Category == ResourceCategory.Food && (r.FoodYield <= 0f || workedBy.Count == 0))
				throw new DataException($"{where}: food resources need food_yield and worked_by");
			AddUnique(defs.Resources, r.Id, r, where);
		}

		var buildingIds = new HashSet<string>();
		foreach (var (where, e) in ReadJsonArray(BuildingsPath, "buildings"))
			defs.Buildings.Add(ParseBuilding(e, where, defs, buildingIds));

		foreach (var (where, e) in ReadJsonArray(CalendarsPath, "calendars"))
			defs.Calendars.Add(ParseCalendar(e, where, defs.Calendars));
		int defaults = defs.Calendars.FindAll(c => c.IsDefault).Count;
		if (defaults != 1 || defs.DefaultCalendar.RequiresFlag != null)
			throw new DataException($"{CalendarsPath}: exactly one calendar must be the default, and it must need no flag");

		return defs;
	}

	static Calendar ParseCalendar(JsonElement e, string where, List<Calendar> existing)
	{
		string id = GetString(e, "id", where);
		if (existing.Exists(c => c.Id == id))
			throw new DataException($"{where}: duplicate calendar '{id}'");
		string kind = GetString(e, "kind", where);
		var months = GetStringList(e, "months", where);
		if (months.Count != 0 && months.Count != 12)
			throw new DataException($"{where}: 'months' must list 12 names");
		e.TryGetProperty("adopted_by", out JsonElement adopted);
		return new Calendar
		{
			Id = id,
			Name = GetString(e, "name", where),
			Kind = kind switch
			{
				"solar" => CalendarKind.Solar,
				"hijri" => CalendarKind.Hijri,
				_ => throw new DataException($"{where}: unknown calendar kind '{kind}'"),
			},
			YearOffset = (long)GetFloat(e, "year_offset", 0f),
			Era = GetString(e, "era", where),
			EraBefore = e.TryGetProperty("era_before", out JsonElement before) ? before.GetString() : null,
			Months = months.Count == 12 ? months : Calendar.GregorianMonths,
			RequiresFlag = e.TryGetProperty("requires_flag", out JsonElement flag) ? flag.GetString() : null,
			AdoptedByReligions = GetStringList(adopted, "religions", where),
			IsDefault = e.TryGetProperty("default", out JsonElement def) && def.ValueKind == JsonValueKind.True,
		};
	}

	static BuildingType ParseBuilding(JsonElement e, string where, Definitions defs, HashSet<string> ids)
	{
		string id = GetString(e, "id", where);
		if (!ids.Add(id))
			throw new DataException($"{where}: duplicate building '{id}'");

		Dictionary<string, float> Amounts(string key)
		{
			var amounts = new Dictionary<string, float>();
			if (!e.TryGetProperty(key, out JsonElement obj))
				return amounts;
			foreach (JsonProperty p in obj.EnumerateObject())
			{
				if (p.Name != BuildingType.LocalDeposit && !defs.Resources.ContainsKey(p.Name))
					throw new DataException($"{where}: {key} has unknown resource '{p.Name}'");
				amounts[p.Name] = (float)p.Value.GetDouble();
			}
			return amounts;
		}

		if (!e.TryGetProperty("jobs", out JsonElement jobs))
			throw new DataException($"{where}: missing 'jobs'");
		e.TryGetProperty("requires", out JsonElement requires);

		List<ResourceType> Resources(string key, ResourceCategory category)
		{
			var list = new List<ResourceType>();
			foreach (string rid in GetStringList(requires, key, where))
			{
				ResourceType r = Lookup(defs.Resources, rid, "resource", where);
				if (r.Category != category)
					throw new DataException($"{where}: requires.{key} lists '{rid}', which is not a {category} resource");
				list.Add(r);
			}
			return list;
		}

		var builders = GetStringList(e, "builders", where);
		Occupation popOccupation = null;
		int popMin = 0;
		if (e.TryGetProperty("population_builds", out JsonElement pb))
		{
			popOccupation = Lookup(defs.Occupations, GetString(pb, "occupation", where), "occupation", where);
			popMin = (int)GetFloat(pb, "min_units", 1f);
		}

		return new BuildingType
		{
			Id = id,
			Name = GetString(e, "name", where),
			Model = GetString(e, "model", where),
			Produces = Amounts("produces"),
			Consumes = Amounts("consumes"),
			JobOccupation = Lookup(defs.Occupations, GetString(jobs, "occupation", where), "occupation", where),
			JobUnits = (int)GetFloat(jobs, "units", 1f),
			RequiredFeatures = GetStringList(requires, "features", where),
			RequiredFood = Resources("food", ResourceCategory.Food),
			RequiredNonRenewable = Resources("non_renewable", ResourceCategory.NonRenewable),
			GovernmentCanBuild = builders.Contains("government"),
			PopulationCanBuild = builders.Contains("population") && popOccupation != null,
			PopulationBuildOccupation = popOccupation,
			PopulationBuildMinUnits = popMin,
		};
	}

	/// <summary>Reads province_setup.json into the provinces. Provinces missing from it stay empty.</summary>
	public static int LoadProvinceSetup(string path, Province[] provinces, Definitions defs)
	{
		int count = 0;
		foreach (var (where, e) in ReadJsonArray(path, "provinces"))
		{
			if (!e.TryGetProperty("id", out JsonElement idElement) || !idElement.TryGetInt32(out int id)
				|| id <= 0 || id >= provinces.Length || provinces[id] == null)
				throw new DataException($"{where}: 'id' is not a province id from {ProvincesPath}");
			Province p = provinces[id];

			foreach (string f in GetStringList(e, "features", where))
				p.Features.Add(f);

			if (e.TryGetProperty("resources", out JsonElement res))
			{
				p.NonRenewable = OptionalResource(res, "non_renewable", ResourceCategory.NonRenewable, defs, where);
				p.Food = OptionalResource(res, "food", ResourceCategory.Food, defs, where);
			}

			if (e.TryGetProperty("pops", out JsonElement pops))
			{
				int i = 0;
				foreach (JsonElement pop in pops.EnumerateArray())
				{
					string w = $"{where}.pops[{i++}]";
					int units = (int)GetFloat(pop, "units", 0f);
					if (units <= 0)
						throw new DataException($"{w}: units must be positive");
					p.AddPops(
						Lookup(defs.Cultures, GetString(pop, "culture", w), "culture", w),
						Lookup(defs.Religions, GetString(pop, "religion", w), "religion", w),
						Lookup(defs.Occupations, GetString(pop, "occupation", w), "occupation", w),
						units);
				}
			}

			if (e.TryGetProperty("buildings", out JsonElement buildings))
			{
				int i = 0;
				foreach (JsonElement b in buildings.EnumerateArray())
				{
					string w = $"{where}.buildings[{i++}]";
					string type = GetString(b, "type", w);
					BuildingType bt = defs.GetBuilding(type) ?? throw new DataException($"{w}: unknown building '{type}'");
					if (p.GetBuilding(bt) != null)
						throw new DataException($"{w}: duplicate building '{type}'");
					p.Buildings.Add(new Building(bt, (int)GetFloat(b, "level", 1f), bt.PopulationCanBuild));
				}
			}
			count++;
		}
		return count;
	}

	static ResourceType OptionalResource(JsonElement obj, string key, ResourceCategory category, Definitions defs, string where)
	{
		if (!obj.TryGetProperty(key, out JsonElement v) || v.ValueKind == JsonValueKind.Null)
			return null;
		ResourceType r = Lookup(defs.Resources, v.GetString(), "resource", where);
		if (r.Category != category)
			throw new DataException($"{where}: '{r.Id}' is not a {category} resource");
		return r;
	}

	/// <summary>The elements of a top-level array, or of the array under <paramref name="key"/> of a top-level object.</summary>
	static List<(string Where, JsonElement Element)> ReadJsonArray(string path, string key)
	{
		JsonDocument doc;
		try
		{
			doc = JsonDocument.Parse(ReadText(path));
		}
		catch (JsonException e)
		{
			throw new DataException($"{path}: {e.Message}");
		}

		JsonElement root = doc.RootElement;
		if (key != null && (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(key, out root)))
			throw new DataException($"{path}: expected an object with a '{key}' array");
		if (root.ValueKind != JsonValueKind.Array)
			throw new DataException($"{path}: expected a JSON array{(key != null ? $" under '{key}'" : "")}");

		var list = new List<(string, JsonElement)>();
		int index = 0;
		foreach (JsonElement e in root.EnumerateArray())
			list.Add(($"{path}:{key ?? ""}[{index++}]", e.Clone()));
		doc.Dispose();
		return list;
	}

	static List<string> GetStringList(JsonElement obj, string key, string where)
	{
		var list = new List<string>();
		if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out JsonElement arr))
			return list;
		if (arr.ValueKind != JsonValueKind.Array)
			throw new DataException($"{where}: '{key}' must be an array");
		foreach (JsonElement s in arr.EnumerateArray())
			list.Add(s.GetString());
		return list;
	}

	static float GetFloat(JsonElement obj, string key, float fallback) =>
		obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number
			? (float)v.GetDouble()
			: fallback;

	static T Lookup<T>(Dictionary<string, T> table, string id, string what, string where) =>
		id != null && table.TryGetValue(id, out T value) ? value : throw new DataException($"{where}: unknown {what} '{id}'");

	static void AddUnique<T>(Dictionary<string, T> table, string id, T value, string where)
	{
		if (!table.TryAdd(id, value))
			throw new DataException($"{where}: duplicate id '{id}'");
	}
}
