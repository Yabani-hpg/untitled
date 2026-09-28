using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Untitled.Data;

public sealed class DataException : Exception
{
	public DataException(string message) : base(message) { }
}

/// <summary>Parses the definition files in res://data. Throws <see cref="DataException"/> with file and line on bad input.</summary>
public static class DataLoader
{
	public const string ProvincesPath = "res://data/provinces.csv";
	public const string CountriesPath = "res://data/countries.json";

	/// <summary>Reads provinces.csv. The result is indexed by province id; unused ids are null.</summary>
	public static Province[] LoadProvinces(string path, IReadOnlyDictionary<string, Country> countries)
	{
		var byId = new Dictionary<int, Province>();
		var usedColors = new Dictionary<Color, int>();
		int maxId = 0;
		bool sawHeader = false;

		string[] lines = ReadText(path).Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (line.Length == 0 || line.StartsWith('#'))
				continue;
			if (!sawHeader)
			{
				sawHeader = true;
				continue;
			}

			string where = $"{path}:{i + 1}";
			string[] cols = line.Split(';');
			if (cols.Length != 5)
				throw new DataException($"{where}: expected 5 columns (id;color;name;terrain;owner), got {cols.Length}");

			if (!int.TryParse(cols[0].Trim(), out int id) || id <= 0)
				throw new DataException($"{where}: id '{cols[0]}' must be a positive integer");
			if (byId.ContainsKey(id))
				throw new DataException($"{where}: duplicate id {id}");

			Color color = ParseHexColor(cols[1].Trim(), where);
			if (usedColors.TryGetValue(color, out int otherId))
				throw new DataException($"{where}: color {cols[1].Trim()} is already used by province {otherId}");

			string name = cols[2].Trim();
			string terrain = cols[3].Trim();
			string owner = cols[4].Trim();
			if (name.Length == 0 || terrain.Length == 0)
				throw new DataException($"{where}: name and terrain are required");
			if (owner.Length == 0)
				owner = null;
			else if (!countries.ContainsKey(owner))
				throw new DataException($"{where}: unknown owner tag '{owner}'");

			byId[id] = new Province(id, color, name, terrain, owner);
			usedColors[color] = id;
			maxId = Math.Max(maxId, id);
		}

		var provinces = new Province[maxId + 1];
		foreach (var (id, province) in byId)
			provinces[id] = province;
		return provinces;
	}

	public static Dictionary<string, Country> LoadCountries(string path)
	{
		var countries = new Dictionary<string, Country>();
		JsonDocument doc;
		try
		{
			doc = JsonDocument.Parse(ReadText(path));
		}
		catch (JsonException e)
		{
			throw new DataException($"{path}: {e.Message}");
		}

		using (doc)
		{
			if (doc.RootElement.ValueKind != JsonValueKind.Array)
				throw new DataException($"{path}: expected a JSON array of countries");

			int index = 0;
			foreach (JsonElement entry in doc.RootElement.EnumerateArray())
			{
				string where = $"{path}[{index++}]";
				string tag = GetString(entry, "tag", where);
				string name = GetString(entry, "name", where);
				Color color = ParseHexColor(GetString(entry, "color", where), where);
				if (!countries.TryAdd(tag, new Country(tag, name, color)))
					throw new DataException($"{where}: duplicate tag '{tag}'");
			}
		}
		return countries;
	}

	static string ReadText(string path)
	{
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null)
			throw new DataException($"{path}: cannot open ({FileAccess.GetOpenError()})");
		return file.GetAsText();
	}

	static string GetString(JsonElement obj, string key, string where)
	{
		if (obj.ValueKind != JsonValueKind.Object
			|| !obj.TryGetProperty(key, out JsonElement value)
			|| value.ValueKind != JsonValueKind.String
			|| string.IsNullOrWhiteSpace(value.GetString()))
			throw new DataException($"{where}: missing string field '{key}'");
		return value.GetString().Trim();
	}

	static Color ParseHexColor(string text, string where)
	{
		string hex = text.StartsWith('#') ? text[1..] : text;
		if (hex.Length != 6 || !Color.HtmlIsValid(hex))
			throw new DataException($"{where}: '{text}' is not an RGB hex color like #12ab34");
		return Color.FromHtml(hex);
	}
}
