using System.Text.Json;

namespace Untitled.Data;

public static partial class DataLoader
{
	public const string EstatesPath = "res://data/estates.json";

	public static void LoadEstates(Definitions defs)
	{
		foreach (var (where, e) in ReadJsonArray(EstatesPath, "estates"))
		{
			var estate = new Estate
			{
				Id = GetString(e, "id", where),
				Name = GetString(e, "name", where),
				Description = e.TryGetProperty("description", out JsonElement d) ? d.GetString() : "",
				Occupation = e.TryGetProperty("occupation", out JsonElement oc) && oc.ValueKind == JsonValueKind.String
					? Lookup(defs.Occupations, oc.GetString(), "occupation", where) : null,
				BaseInfluence = GetFloat(e, "base_influence", 10f),
				PerShare = GetFloat(e, "per_share", 0f),
				Color = ParseHexColor(GetString(e, "color", where), where),
			};
			foreach (var (key, target) in new[] { ("loyal", estate.Loyal), ("disloyal", estate.Disloyal) })
			{
				if (e.TryGetProperty(key, out JsonElement mods))
					foreach (JsonProperty m in mods.EnumerateObject())
						target[m.Name] = m.Value.GetDouble();
			}
			if (e.TryGetProperty("privileges", out JsonElement privileges))
			{
				int i = 0;
				foreach (JsonElement p in privileges.EnumerateArray())
				{
					string w = $"{where}.privileges[{i++}]";
					var privilege = new Privilege
					{
						Id = GetString(p, "id", w),
						Name = GetString(p, "name", w),
						Description = p.TryGetProperty("description", out JsonElement pd) ? pd.GetString() : "",
						Influence = GetFloat(p, "influence", 5f),
						Loyalty = GetFloat(p, "loyalty", 10f),
						Estate = estate,
					};
					if (p.TryGetProperty("modifiers", out JsonElement mods))
						foreach (JsonProperty m in mods.EnumerateObject())
							privilege.Modifiers[m.Name] = m.Value.GetDouble();
					if (defs.GetPrivilege(privilege.Id) != null)
						throw new DataException($"{w}: duplicate privilege '{privilege.Id}'");
					estate.Privileges.Add(privilege);
				}
			}
			if (defs.GetEstate(estate.Id) != null)
				throw new DataException($"{where}: duplicate estate '{estate.Id}'");
			defs.Estates.Add(estate);
		}
	}
}
