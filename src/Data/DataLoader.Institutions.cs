using System.Text.Json;

namespace Untitled.Data;

public static partial class DataLoader
{
	public const string InstitutionsPath = "res://data/institutions.json";

	public static void LoadInstitutions(Definitions defs)
	{
		JsonDocument doc = JsonDocument.Parse(ReadText(InstitutionsPath));
		JsonElement root = doc.RootElement;
		defs.InstitutionPenalty = GetFloat(root, "penalty", 0.25f);
		defs.EmbraceCostPerProvince = GetFloat(root, "embrace_cost_per_province", 4f);
		defs.EmbraceCostMin = GetFloat(root, "embrace_cost_min", 50f);
		foreach (var (where, e) in ReadJsonArray(InstitutionsPath, "institutions"))
		{
			var inst = new Institution
			{
				Id = GetString(e, "id", where),
				Name = GetString(e, "name", where),
				Description = e.TryGetProperty("description", out JsonElement d) ? d.GetString() : "",
				Year = (int)GetFloat(e, "year", 0f),
				OriginTech = e.TryGetProperty("origin_tech", out JsonElement t) ? defs.GetTech(t.GetString()) ?? throw new DataException($"{where}: unknown technology '{t.GetString()}'") : null,
			};
			if (defs.GetInstitution(inst.Id) != null)
				throw new DataException($"{where}: duplicate institution '{inst.Id}'");
			defs.Institutions.Add(inst);
		}
	}
}
