using System.Text.Json;

namespace Untitled.Data;

public static partial class DataLoader
{
	public const string IdeasPath = "res://data/ideas.json";

	public static void LoadIdeas(Definitions defs)
	{
		foreach (var (where, e) in ReadJsonArray(IdeasPath, "groups"))
		{
			string category = GetString(e, "category", where);
			var group = new IdeaGroup
			{
				Id = GetString(e, "id", where),
				Name = GetString(e, "name", where),
				Category = category switch { "military" => TechCategory.Military, "science" => TechCategory.Science, _ => TechCategory.Admin },
				Description = e.TryGetProperty("description", out JsonElement d) ? d.GetString() : "",
			};
			int i = 0;
			foreach (JsonElement ie in e.GetProperty("ideas").EnumerateArray())
			{
				string w = $"{where}.ideas[{i++}]";
				var idea = new Idea { Id = GetString(ie, "id", w), Name = GetString(ie, "name", w), Description = ie.TryGetProperty("description", out JsonElement id) ? id.GetString() : "", Group = group };
				ReadModifiers(ie, idea.Modifiers);
				group.Ideas.Add(idea);
			}
			if (group.Ideas.Count != 7)
				throw new DataException($"{where}: an idea group has seven ideas");
			if (defs.GetIdeaGroup(group.Id) != null)
				throw new DataException($"{where}: duplicate idea group '{group.Id}'");
			defs.IdeaGroups.Add(group);
		}
	}

	static void ReadModifiers(JsonElement e, System.Collections.Generic.Dictionary<string, double> into)
	{
		if (e.TryGetProperty("modifiers", out JsonElement mods))
			foreach (JsonProperty m in mods.EnumerateObject())
				into[m.Name] = m.Value.GetDouble();
	}

	/// <summary>A country file's "national_ideas": traditions, seven ideas, an ambition.</summary>
	static NationalIdeas ParseNationalIdeas(JsonElement e, string path, string tag)
	{
		if (!e.TryGetProperty("national_ideas", out JsonElement n))
			return null;
		var ideas = new NationalIdeas();
		if (n.TryGetProperty("traditions", out JsonElement t))
			foreach (JsonProperty m in t.EnumerateObject()) ideas.Traditions[m.Name] = m.Value.GetDouble();
		if (n.TryGetProperty("ambition", out JsonElement a))
			foreach (JsonProperty m in a.EnumerateObject()) ideas.Ambition[m.Name] = m.Value.GetDouble();
		int i = 0;
		foreach (JsonElement ie in n.GetProperty("ideas").EnumerateArray())
		{
			string w = $"{path}:national_ideas.ideas[{i}]";
			var idea = new Idea { Id = $"{tag}_national_{i + 1}", Name = GetString(ie, "name", w), Description = ie.TryGetProperty("description", out JsonElement d) ? d.GetString() : "" };
			ReadModifiers(ie, idea.Modifiers);
			ideas.Ideas.Add(idea);
			i++;
		}
		if (ideas.Ideas.Count != 7)
			throw new DataException($"{path}: national_ideas needs seven ideas");
		return ideas;
	}
}
