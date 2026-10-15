using System.Collections.Generic;
using System.Text.Json;

namespace Untitled.Data;

public static partial class DataLoader
{
	public const string DisastersPath = "res://data/disasters.json";

	public static void LoadDisasters(Definitions defs, Dictionary<string, int> names)
	{
		foreach (var (where, e) in ReadJsonArray(DisastersPath, "disasters"))
		{
			var d = new Disaster
			{
				Id = GetString(e, "id", where),
				Name = GetString(e, "name", where),
				Description = e.TryGetProperty("description", out JsonElement ds) ? ds.GetString() : "",
				Trigger = ParseCondition(e.GetProperty("trigger"), $"{where}.trigger", defs, names),
				End = ParseCondition(e.GetProperty("end"), $"{where}.end", defs, names),
				ProgressPerMonth = GetFloat(e, "progress_per_month", 2f),
				DeclinePerMonth = GetFloat(e, "decline_per_month", 1f),
				MinYears = (int)GetFloat(e, "min_years", 1f),
				OnStart = e.TryGetProperty("on_start", out JsonElement os) ? ParseEffects(os, $"{where}.on_start", defs, names) : new Effects(),
				OnEnd = e.TryGetProperty("on_end", out JsonElement oe) ? ParseEffects(oe, $"{where}.on_end", defs, names) : new Effects(),
			};
			ReadModifiers(e, d.Modifiers);
			if (defs.GetDisaster(d.Id) != null)
				throw new DataException($"{where}: duplicate disaster '{d.Id}'");
			defs.Disasters.Add(d);
		}
	}
}
