using System.Collections.Generic;
using System.Text.Json;

namespace Untitled.Data;

public static partial class DataLoader
{
	public const string WorksPath = "res://data/works.json";

	public static void LoadWorks(Definitions defs)
	{
		JsonDocument doc = JsonDocument.Parse(ReadText(WorksPath));
		JsonElement root = doc.RootElement;
		defs.WorkSlotUnits = (int)GetFloat(root, "slot_units", 6f);
		defs.MaxWorkSlots = (int)GetFloat(root, "max_slots", 8f);
		foreach (var (where, e) in ReadJsonArray(WorksPath, "works"))
		{
			var mods = new Dictionary<string, double>();
			if (e.TryGetProperty("modifiers", out JsonElement m))
				foreach (JsonProperty kv in m.EnumerateObject())
					mods[kv.Name] = kv.Value.GetDouble();
			var work = new Work
			{
				Id = GetString(e, "id", where),
				Name = GetString(e, "name", where),
				Description = e.TryGetProperty("description", out JsonElement d) ? d.GetString() : "",
				Cost = GetFloat(e, "cost", 100f),
				Tech = e.TryGetProperty("tech", out JsonElement t) ? defs.GetTech(t.GetString()) ?? throw new DataException($"{where}: unknown technology '{t.GetString()}'") : null,
				Upgrades = e.TryGetProperty("upgrades", out JsonElement u) ? defs.GetWork(u.GetString()) ?? throw new DataException($"{where}: upgrades unknown work '{u.GetString()}' (list it first)") : null,
				RequiredFeatures = e.TryGetProperty("requires", out JsonElement r) ? GetStringList(r, "features", where) : new List<string>(),
				Modifiers = mods,
			};
			if (defs.GetWork(work.Id) != null)
				throw new DataException($"{where}: duplicate work '{work.Id}'");
			defs.Works.Add(work);
		}
	}
}
