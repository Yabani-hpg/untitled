using System.Text.Json;

namespace Untitled.Data;

public static partial class DataLoader
{
	public const string ReformsPath = "res://data/reforms.json";

	public static void LoadReforms(Definitions defs)
	{
		int index = 0;
		foreach (var (where, e) in ReadJsonArray(ReformsPath, "tiers"))
		{
			var tier = new ReformTier { Index = index++, Id = GetString(e, "id", where), Name = GetString(e, "name", where) };
			int i = 0;
			foreach (JsonElement re in e.GetProperty("reforms").EnumerateArray())
			{
				string w = $"{where}.reforms[{i++}]";
				var reform = new Reform { Id = GetString(re, "id", w), Name = GetString(re, "name", w), Description = re.TryGetProperty("description", out JsonElement d) ? d.GetString() : "", Tier = tier };
				ReadModifiers(re, reform.Modifiers);
				if (re.TryGetProperty("requires_government", out JsonElement rg))
					foreach (JsonElement g in rg.EnumerateArray()) reform.RequiresGovernment.Add(g.GetString());
				if (defs.GetReform(reform.Id) != null)
					throw new DataException($"{w}: duplicate reform '{reform.Id}'");
				tier.Reforms.Add(reform);
			}
			defs.ReformTiers.Add(tier);
		}
	}
}
