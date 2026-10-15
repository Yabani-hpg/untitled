using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.UI;

/// <summary>An event put to the player: its title and tale, and the choices; the game waits paused until one is taken.</summary>
public partial class EventPanel : PanelContainer
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }

	VBoxContainer _box;

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", HudStyle.Panel(0.98f));
		MouseFilter = MouseFilterEnum.Stop;
		CustomMinimumSize = new Vector2(520, 0);
		Visible = false;
		_box = new VBoxContainer();
		_box.AddThemeConstantOverride("separation", 8);
		AddChild(_box);
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.EventPending += Refresh;
		gs.PhaseChanged += Refresh;
		Refresh();
	}

	public override void _ExitTree()
	{
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.EventPending -= Refresh;
		gs.PhaseChanged -= Refresh;
	}

	void Refresh()
	{
		GameState gs = GameState.Instance;
		GameEvent ev = gs.PendingEvents.FirstOrDefault();
		Visible = ev != null && gs.Phase == GamePhase.Playing;
		foreach (Node child in _box.GetChildren())
		{
			_box.RemoveChild(child);
			child.QueueFree();
		}
		if (!Visible)
			return;
		var head = new HBoxContainer();
		_box.AddChild(head);
		var title = HudStyle.Title(ev.Title, TitleFont, 22);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		head.AddChild(title);
		head.AddChild(HudStyle.Body(gs.DateText + (gs.PendingEvents.Count > 1 ? $" · {gs.PendingEvents.Count - 1} more" : ""), BodyFont, 12, HudStyle.Muted));
		var text = HudStyle.Body(ev.Text, BodyFont, 14);
		text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		text.CustomMinimumSize = new Vector2(500, 0);
		_box.AddChild(text);
		for (int i = 0; i < ev.Options.Count; i++)
		{
			EventOption o = ev.Options[i];
			var b = HudStyle.Button(o.Text, BodyFont, 14);
			b.Alignment = HorizontalAlignment.Left;
			b.TooltipText = Describe(o.Effects, gs);
			int index = i;
			b.Pressed += () => gs.AnswerEvent(index);
			_box.AddChild(b);
		}
	}

	/// <summary>An option's effects in words, for its tooltip.</summary>
	public static string Describe(Effects fx, GameState gs)
	{
		var parts = new System.Collections.Generic.List<string>();
		if (fx.Gold != 0) parts.Add($"Gold {fx.Gold:+0;-0}");
		if (fx.Stability != 0) parts.Add($"Stability {fx.Stability:+0;-0}");
		if (fx.Prestige != 0) parts.Add($"Prestige {fx.Prestige:+0;-0}");
		if (fx.Legitimacy != 0) parts.Add($"Legitimacy {fx.Legitimacy:+0;-0}");
		if (fx.WarExhaustion != 0) parts.Add($"War exhaustion {fx.WarExhaustion:+0;-0}");
		if (fx.Research != 0) parts.Add($"Research {fx.Research:+0;-0} points");
		if (fx.CapitalPops != 0) parts.Add($"{fx.CapitalPops * PopGroup.PeoplePerUnit:+#,0;-#,0} people in the capital");
		if (fx.Levies != 0) parts.Add($"{fx.Levies} regiments of levies");
		if (fx.Tech != null) parts.Add($"Learn {gs.Definitions.GetTech(fx.Tech)?.Name}");
		if (fx.Claims.Count > 0) parts.Add("Claims on " + string.Join(", ", fx.Claims.Select(id => gs.GetProvince(id)?.BaseName)));
		foreach (var (who, amount) in fx.Opinion)
			parts.Add($"Opinion of {(who == "neighbours" ? "our neighbours" : who == "all" ? "everyone" : gs.GetCountry(who)?.Name)} {amount:+0;-0}");
		if (fx.RulerDies) parts.Add("The ruler dies");
		return parts.Count > 0 ? string.Join("\n", parts) : "No effect";
	}
}
