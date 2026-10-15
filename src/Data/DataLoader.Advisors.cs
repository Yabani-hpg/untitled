using System.Text.Json;

namespace Untitled.Data;

public static partial class DataLoader
{
	public const string AdvisorsPath = "res://data/advisors.json";

	public static void LoadAdvisors(Definitions defs)
	{
		foreach (var (where, e) in ReadJsonArray(AdvisorsPath, "types"))
		{
			string category = GetString(e, "category", where);
			if (category is not ("admin" or "diplomatic" or "military"))
				throw new DataException($"{where}: category must be admin, diplomatic or military");
			var type = new AdvisorType
			{
				Id = GetString(e, "id", where),
				Name = GetString(e, "name", where),
				Category = category,
				Description = e.TryGetProperty("description", out JsonElement d) ? d.GetString() : "",
				Modifier = GetString(e, "modifier", where),
				Value = GetFloat(e, "value", 0f),
			};
			if (defs.GetAdvisorType(type.Id) != null)
				throw new DataException($"{where}: duplicate advisor type '{type.Id}'");
			defs.AdvisorTypes.Add(type);
		}
	}
}
