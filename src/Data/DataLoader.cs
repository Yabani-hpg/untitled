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
	public const string AdjacenciesPath = "res://data/adjacencies.csv";

	/// <summary>Reads provinces.csv. The result is indexed by province id; unused ids are null.</summary>
	public static Province[] LoadProvinces(string path, IReadOnlyDictionary<string, Country> countries)
	{
		var byId = new Dictionary<int, Province>();
		var usedColors = new Dictionary<Color, int>();
		int maxId = 0;

		foreach (var (where, cols) in ReadCsvRows(path, "id;color;name;terrain;owner"))
		{
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

	/// <summary>Reads adjacencies.csv and adds each link to both provinces' neighbour lists.</summary>
	public static int LoadAdjacencies(string path, Province[] provinces)
	{
		int count = 0;
		foreach (var (where, cols) in ReadCsvRows(path, "a;b;border;crossing;navigable"))
		{
			Province a = ParseProvinceRef(cols[0], provinces, where);
			Province b = ParseProvinceRef(cols[1], provinces, where);
			if (a == b)
				throw new DataException($"{where}: province {a.Id} is adjacent to itself");
			if (a.GetAdjacency(b) != null)
				throw new DataException($"{where}: duplicate adjacency {a.Id};{b.Id}");
			if (!int.TryParse(cols[2].Trim(), out int border) || border < 0)
				throw new DataException($"{where}: border '{cols[2]}' must be a non-negative integer");

			string crossing = NullIfEmpty(cols[3]);
			string navigable = NullIfEmpty(cols[4]);
			if (border == 0 && navigable == null)
				throw new DataException($"{where}: provinces with no border must be linked by a navigable river");

			var link = new Adjacency(a, b, border, crossing, navigable);
			a.AddNeighbor(link);
			b.AddNeighbor(link.Reversed());
			count++;
		}
		return count;
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

	/// <summary>Data rows of a ;-separated file with # comments and one header row (which must match).</summary>
	static IEnumerable<(string Where, string[] Cols)> ReadCsvRows(string path, string header)
	{
		int columns = header.Split(';').Length;
		bool sawHeader = false;
		string[] lines = ReadText(path).Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (line.Length == 0 || line.StartsWith('#'))
				continue;
			string where = $"{path}:{i + 1}";
			if (!sawHeader)
			{
				if (line != header)
					throw new DataException($"{where}: expected header '{header}'");
				sawHeader = true;
				continue;
			}
			string[] cols = line.Split(';');
			if (cols.Length != columns)
				throw new DataException($"{where}: expected {columns} columns ({header}), got {cols.Length}");
			yield return (where, cols);
		}
	}

	static Province ParseProvinceRef(string text, Province[] provinces, string where)
	{
		if (!int.TryParse(text.Trim(), out int id) || id <= 0 || id >= provinces.Length || provinces[id] == null)
			throw new DataException($"{where}: '{text}' is not a province id from {ProvincesPath}");
		return provinces[id];
	}

	static string NullIfEmpty(string text)
	{
		text = text.Trim();
		return text.Length == 0 ? null : text;
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
