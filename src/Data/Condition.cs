using System;
using System.Collections.Generic;
using System.Text.Json;
using Untitled.Core;

namespace Untitled.Data;

/// <summary>What conditions can look at. Implemented by GameState.</summary>
public interface IWorld
{
	IReadOnlyList<Province> Provinces { get; }
	GameDate Date { get; }
	IReadOnlySet<string> WorldFlags { get; }
	/// <summary>Province ids of a named area from data/areas.json, or null.</summary>
	IReadOnlyList<int> GetArea(string id);
}

/// <summary>
/// A condition on a country, written in data as JSON, e.g.
/// <c>{ "all": [ { "owns_area": { "area": "nubia", "min_share": 0.5 } }, { "not": { "ruler_religion": "islam" } } ] }</c>.
/// </summary>
public abstract class Condition
{
	public abstract bool Holds(IWorld world, Country country);

	/// <summary>
	/// Parses one condition object. Its single key names the test:
	/// all / any (array), not (condition), owns_area {area, min_share = 1}, owns_province (name),
	/// province_count {min, max}, ruler_culture, ruler_religion, world_flag, law ("law:option"), tech (a technology id),
	/// owns_feature (a province feature: river, coast, desert...), date_from / date_before ("year.month.day", Holocene).
	/// </summary>
	public static Condition Parse(JsonElement e, string where, Func<string, IReadOnlyList<int>> areas, Func<string, int> provinceByName)
	{
		if (e.ValueKind != JsonValueKind.Object)
			throw new DataException($"{where}: a condition must be an object");
		var list = new List<Condition>();
		foreach (JsonProperty p in e.EnumerateObject())
		{
			string w = $"{where}.{p.Name}";
			JsonElement v = p.Value;
			list.Add(p.Name switch
			{
				"all" => new All(ParseList(v, w, areas, provinceByName)),
				"any" => new Any(ParseList(v, w, areas, provinceByName)),
				"not" => new Not(Parse(v, w, areas, provinceByName)),
				"owns_area" => ParseOwnsArea(v, w, areas),
				"owns_province" => new OwnsProvince(provinceByName(v.GetString()) is var id && id > 0
					? id : throw new DataException($"{w}: no province named '{v.GetString()}'")),
				"province_count" => new ProvinceCount(
					v.TryGetProperty("min", out JsonElement min) ? min.GetInt32() : 0,
					v.TryGetProperty("max", out JsonElement max) ? max.GetInt32() : int.MaxValue),
				"ruler_culture" => RulerCulture(v.GetString()),
				"ruler_religion" => RulerReligion(v.GetString()),
				"world_flag" => new WorldFlag(v.GetString()),
				"law" => HasLaw(v.GetString(), w),
				"tech" => KnowsTech(v.GetString()),
				"owns_feature" => new OwnsFeature(v.GetString()),
				"date_from" => new DateTest(ParseDate(v.GetString(), w), from: true),
				"date_before" => new DateTest(ParseDate(v.GetString(), w), from: false),
				_ => throw new DataException($"{w}: unknown condition '{p.Name}'"),
			});
		}
		return list.Count == 1 ? list[0] : new All(list);
	}

	/// <summary>A Holocene date written "year.month.day", Paradox style: "8801.1.1" is 1 January 1200 BC.</summary>
	public static GameDate ParseDate(string text, string where)
	{
		string[] parts = (text ?? "").Split('.');
		if (parts.Length != 3 || !int.TryParse(parts[0], out int y) || !int.TryParse(parts[1], out int m) || !int.TryParse(parts[2], out int d)
			|| m < 1 || m > 12 || d < 1 || d > GameDate.DaysInMonth(y - GameDate.HoloceneOffset, m))
			throw new DataException($"{where}: '{text}' is not a Holocene date like 8801.1.1");
		return GameDate.FromHolocene(y, m, d);
	}

	// the parsed JSON is gone by the time conditions are tested, so values are read out here
	/// <summary>"slavery:outlawed": the country has that law option in force.</summary>
	static Condition HasLaw(string text, string where)
	{
		string[] parts = (text ?? "").Split(':');
		if (parts.Length != 2)
			throw new DataException($"{where}: write a law condition as \"law:option\"");
		string law = parts[0], option = parts[1];
		return new Test(c => c.Laws.TryGetValue(law, out string o) && o == option);
	}

	static Condition KnowsTech(string id) => new Test(c => c.Techs.Contains(id));

	static Condition RulerCulture(string id) => new Test(c => c.Ruler?.Culture?.Id == id);
	static Condition RulerReligion(string id) => new Test(c => c.Ruler?.Religion?.Id == id);

	static List<Condition> ParseList(JsonElement v, string where, Func<string, IReadOnlyList<int>> areas, Func<string, int> provinceByName)
	{
		if (v.ValueKind != JsonValueKind.Array)
			throw new DataException($"{where}: expected an array of conditions");
		var list = new List<Condition>();
		int i = 0;
		foreach (JsonElement c in v.EnumerateArray())
			list.Add(Parse(c, $"{where}[{i++}]", areas, provinceByName));
		return list;
	}

	static Condition ParseOwnsArea(JsonElement v, string where, Func<string, IReadOnlyList<int>> areas)
	{
		string id = v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetProperty("area").GetString();
		double share = v.ValueKind == JsonValueKind.Object && v.TryGetProperty("min_share", out JsonElement s) ? s.GetDouble() : 1.0;
		IReadOnlyList<int> provinces = areas(id) ?? throw new DataException($"{where}: unknown area '{id}' (see data/areas.json)");
		return new OwnsArea(provinces, share);
	}

	sealed class All(List<Condition> items) : Condition
	{
		public override bool Holds(IWorld w, Country c) => items.TrueForAll(i => i.Holds(w, c));
	}

	sealed class Any(List<Condition> items) : Condition
	{
		public override bool Holds(IWorld w, Country c) => items.Exists(i => i.Holds(w, c));
	}

	sealed class Not(Condition inner) : Condition
	{
		public override bool Holds(IWorld w, Country c) => !inner.Holds(w, c);
	}

	sealed class OwnsArea(IReadOnlyList<int> provinces, double minShare) : Condition
	{
		public override bool Holds(IWorld w, Country c)
		{
			if (provinces.Count == 0)
				return false;
			int owned = 0;
			foreach (int id in provinces)
			{
				if (w.Provinces[id]?.OwnerTag == c.Tag)
					owned++;
			}
			return owned >= minShare * provinces.Count - 1e-9 && owned > 0;
		}
	}

	sealed class OwnsProvince(int id) : Condition
	{
		public override bool Holds(IWorld w, Country c) => w.Provinces[id]?.OwnerTag == c.Tag;
	}

	sealed class ProvinceCount(int min, int max) : Condition
	{
		public override bool Holds(IWorld w, Country c)
		{
			int n = 0;
			foreach (Province p in w.Provinces)
			{
				if (p != null && p.OwnerTag == c.Tag)
					n++;
			}
			return n >= min && n <= max;
		}
	}

	sealed class Test(Func<Country, bool> test) : Condition
	{
		public override bool Holds(IWorld w, Country c) => test(c);
	}

	sealed class WorldFlag(string flag) : Condition
	{
		public override bool Holds(IWorld w, Country c) => w.WorldFlags.Contains(flag);
	}

	sealed class DateTest(GameDate date, bool from) : Condition
	{
		public override bool Holds(IWorld w, Country c) => from ? w.Date >= date : w.Date < date;
	}

	sealed class OwnsFeature(string feature) : Condition
	{
		public override bool Holds(IWorld w, Country c)
		{
			foreach (Province p in w.Provinces)
			{
				if (p != null && p.OwnerTag == c.Tag && p.HasFeature(feature))
					return true;
			}
			return false;
		}
	}
}
