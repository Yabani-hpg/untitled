using System.Collections.Generic;
using System.Text.Json;

namespace Untitled.Data;

// Events, decisions and missions.
public static partial class DataLoader
{
	public const string EventsPath = "res://data/events.json";
	public const string DecisionsPath = "res://data/decisions.json";

	static Condition ParseCondition(JsonElement e, string where, Definitions defs, Dictionary<string, int> names) =>
		Condition.Parse(e, where, id => defs.Areas.TryGetValue(id, out List<int> a) ? a : null, name => names.TryGetValue(name, out int pid) ? pid : 0);

	public static Effects ParseEffects(JsonElement e, string where, Definitions defs, Dictionary<string, int> names)
	{
		var fx = new Effects
		{
			Gold = GetFloat(e, "gold", 0f),
			Stability = (int)GetFloat(e, "stability", 0f),
			Prestige = GetFloat(e, "prestige", 0f),
			Legitimacy = GetFloat(e, "legitimacy", 0f),
			WarExhaustion = GetFloat(e, "war_exhaustion", 0f),
			Research = GetFloat(e, "research", 0f),
			CapitalPops = (int)GetFloat(e, "capital_pops", 0f),
			Levies = (int)GetFloat(e, "levies", 0f),
			RulerDies = e.TryGetProperty("ruler_dies", out JsonElement rd) && rd.GetBoolean(),
			Tech = e.TryGetProperty("tech", out JsonElement t) ? t.GetString() : null,
			Message = e.TryGetProperty("message", out JsonElement m) ? m.GetString() : null,
		};
		if (fx.Tech != null && defs.GetTech(fx.Tech) == null)
			throw new DataException($"{where}: unknown technology '{fx.Tech}'");
		fx.SetFlags.AddRange(GetStringList(e, "set_flag", where));
		fx.ClearFlags.AddRange(GetStringList(e, "clear_flag", where));
		fx.SetWorldFlags.AddRange(GetStringList(e, "set_world_flag", where));
		foreach (string name in GetStringList(e, "claims", where))
			fx.Claims.Add(ProvinceByName(names, name, $"{where}.claims"));
		if (e.TryGetProperty("opinion", out JsonElement op))
		{
			foreach (JsonProperty o in op.EnumerateObject())
				fx.Opinion[o.Name] = o.Value.GetInt32();
		}
		return fx;
	}

	public static void LoadEvents(Definitions defs, Dictionary<string, int> names)
	{
		foreach (var (where, e) in ReadJsonArray(EventsPath, "events"))
		{
			var ev = new GameEvent
			{
				Id = GetString(e, "id", where),
				Title = GetString(e, "title", where),
				Text = GetString(e, "text", where),
				MeanTimeYears = GetFloat(e, "mean_time_years", 5f),
				Once = !e.TryGetProperty("once", out JsonElement once) || once.GetBoolean(),
			};
			if (defs.GetEvent(ev.Id) != null)
				throw new DataException($"{where}: duplicate event '{ev.Id}'");
			ev.Trigger = e.TryGetProperty("trigger", out JsonElement trig) ? ParseCondition(trig, $"{where}.trigger", defs, names) : null;
			int i = 0;
			foreach (JsonElement o in e.GetProperty("options").EnumerateArray())
			{
				string w = $"{where}.options[{i++}]";
				ev.Options.Add(new EventOption
				{
					Text = GetString(o, "text", w),
					Effects = o.TryGetProperty("effects", out JsonElement fx) ? ParseEffects(fx, w, defs, names) : new Effects(),
					AiWeight = GetFloat(o, "ai_weight", 1f),
				});
			}
			if (ev.Options.Count == 0)
				throw new DataException($"{where}: an event needs options");
			defs.Events.Add(ev);
		}
	}

	public static void LoadDecisions(Definitions defs, Dictionary<string, int> names)
	{
		foreach (var (where, e) in ReadJsonArray(DecisionsPath, "decisions"))
		{
			var d = new Decision
			{
				Id = GetString(e, "id", where),
				Title = GetString(e, "title", where),
				Text = GetString(e, "text", where),
				RequiresText = e.TryGetProperty("requires_text", out JsonElement rt) ? rt.GetString() : null,
				Effects = e.TryGetProperty("effects", out JsonElement fx) ? ParseEffects(fx, where, defs, names) : new Effects(),
				Once = !e.TryGetProperty("once", out JsonElement once) || once.GetBoolean(),
			};
			if (defs.GetDecision(d.Id) != null)
				throw new DataException($"{where}: duplicate decision '{d.Id}'");
			d.Visible = e.TryGetProperty("visible", out JsonElement vis) ? ParseCondition(vis, $"{where}.visible", defs, names) : null;
			d.Requires = e.TryGetProperty("requires", out JsonElement req) ? ParseCondition(req, $"{where}.requires", defs, names) : null;
			defs.Decisions.Add(d);
		}
	}

	/// <summary>The "missions" of a country file.</summary>
	static List<Mission> ParseMissions(JsonElement e, string path, Definitions defs, Dictionary<string, int> names)
	{
		var list = new List<Mission>();
		if (!e.TryGetProperty("missions", out JsonElement missions))
			return list;
		int i = 0;
		foreach (JsonElement m in missions.EnumerateArray())
		{
			string w = $"{path}:missions[{i++}]";
			list.Add(new Mission
			{
				Id = GetString(m, "id", w),
				Title = GetString(m, "title", w),
				Text = GetString(m, "text", w),
				RequiresText = m.TryGetProperty("requires_text", out JsonElement rt) ? rt.GetString() : null,
				Requires = ParseCondition(m.GetProperty("requires"), $"{w}.requires", defs, names),
				Reward = m.TryGetProperty("reward", out JsonElement rw) ? ParseEffects(rw, w, defs, names) : new Effects(),
				After = m.TryGetProperty("after", out JsonElement af) ? af.GetString() : null,
			});
		}
		return list;
	}
}
