using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.UI;

/// <summary>
/// The research window: for each kind of research (military, admin, science) the points coming in, the
/// stockpile and the technology being researched; and the three technology trees, where a technology
/// whose requirements are met can be chosen.
/// </summary>
public partial class ResearchPanel : PanelContainer
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }

	TabContainer _tabs;
	HBoxContainer _cards;
	Label _status;
	readonly Dictionary<TechCategory, TechTree> _trees = new();

	public static readonly Dictionary<TechCategory, Color> CategoryColors = new()
	{
		[TechCategory.Military] = new Color(0.82f, 0.42f, 0.32f),
		[TechCategory.Admin] = new Color(0.88f, 0.72f, 0.3f),
		[TechCategory.Science] = new Color(0.4f, 0.7f, 0.78f),
	};

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", HudStyle.Panel(0.97f));
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 8);
		AddChild(root);

		var header = new HBoxContainer();
		root.AddChild(header);
		var title = HudStyle.Title("Research", TitleFont, 26);
		header.AddChild(title);
		var intro = HudStyle.Body("  Our scribes, priests and scholars carry research; artisans, peasants and soldiers add a little. "
			+ "All technologies share the points: they flow into the first in our queue whose requirements are met; with none, they are stockpiled for the next.",
			BodyFont, 12, HudStyle.Muted);
		intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		intro.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		intro.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		header.AddChild(intro);
		var close = HudStyle.Button("✕", BodyFont);
		close.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		close.Pressed += () => Visible = false;
		header.AddChild(close);

		_cards = new HBoxContainer();
		_cards.AddThemeConstantOverride("separation", 8);
		root.AddChild(_cards);

		_tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		if (BodyFont != null)
			_tabs.AddThemeFontOverride("font", BodyFont);
		_tabs.AddThemeFontSizeOverride("font_size", 15);
		_tabs.AddThemeColorOverride("font_selected_color", HudStyle.Gold);
		_tabs.AddThemeColorOverride("font_unselected_color", HudStyle.Muted);
		_tabs.AddThemeStyleboxOverride("panel", new StyleBoxEmpty { ContentMarginTop = 6 });
		var tab = new StyleBoxFlat { BgColor = new Color(0.3f, 0.23f, 0.13f, 0.6f) };
		tab.SetCornerRadiusAll(4);
		tab.SetContentMarginAll(6);
		var sel = (StyleBoxFlat)tab.Duplicate();
		sel.BgColor = new Color(0.5f, 0.38f, 0.2f, 0.8f);
		_tabs.AddThemeStyleboxOverride("tab_unselected", tab);
		_tabs.AddThemeStyleboxOverride("tab_hovered", sel);
		_tabs.AddThemeStyleboxOverride("tab_selected", sel);
		root.AddChild(_tabs);
		foreach (TechCategory cat in TechRules.Categories)
		{
			var scroll = new ScrollContainer { Name = $"{cat}", SizeFlagsVertical = SizeFlags.ExpandFill };
			var tree = new TechTree { Category = cat, BodyFont = BodyFont };
			tree.Chosen += OnChosen;
			scroll.AddChild(tree);
			_tabs.AddChild(scroll);
			_trees[cat] = tree;
		}

		var legend = new HBoxContainer();
		legend.AddThemeConstantOverride("separation", 14);
		root.AddChild(legend);
		foreach (var (state, text) in new[] { (TechTree.State.Known, "Known"), (TechTree.State.Researching, "Researching"), (TechTree.State.Queued, "Queued"), (TechTree.State.Available, "Can be researched"), (TechTree.State.Locked, "Requirements not met") })
		{
			var swatch = new Panel { CustomMinimumSize = new Vector2(18, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter };
			swatch.AddThemeStyleboxOverride("panel", TechTree.Style(state, CategoryColors[TechCategory.Admin]));
			legend.AddChild(swatch);
			legend.AddChild(HudStyle.Body(text, BodyFont, 12, HudStyle.Muted));
		}
		_status = HudStyle.Body("", BodyFont, 13, HudStyle.Gold);
		_status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_status.HorizontalAlignment = HorizontalAlignment.Right;
		legend.AddChild(_status);

		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.ResearchChanged += Refresh;
		gs.MonthAdvanced += Refresh;
		gs.PhaseChanged += Hide;
	}

	public override void _ExitTree()
	{
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.ResearchChanged -= Refresh;
		gs.MonthAdvanced -= Refresh;
		gs.PhaseChanged -= Hide;
	}

	/// <summary>
	/// Opens the window (on a kind of research, or on the one being researched), or closes it if it is
	/// already showing that.
	/// </summary>
	public void Toggle(TechCategory? category = null)
	{
		GameState gs = GameState.Instance;
		TechCategory cat = category ?? (gs?.PlayerCountry is Country c && TechRules.Current(c, gs) is Tech t ? t.Category : TechRules.Categories[_tabs.CurrentTab]);
		int index = Array.IndexOf(TechRules.Categories, cat);
		Visible = !Visible || category != null && _tabs.CurrentTab != index;
		_tabs.CurrentTab = index;
		_status.Text = "";
		Refresh();
	}

	/// <summary>A click on a technology: left adds it to the queue, or if it is queued, makes it the priority; right takes it out.</summary>
	void OnChosen(string techId, bool remove)
	{
		GameState gs = GameState.Instance;
		Country c = gs.PlayerCountry;
		Tech t = gs.Definitions.GetTech(techId);
		if (remove)
		{
			if (c.ResearchQueue.Contains(techId))
			{
				gs.DequeueResearch(techId);
				_status.Text = $"{t.Name} taken out of the queue (its progress is kept)";
			}
			return;
		}
		bool queued = c.ResearchQueue.Contains(techId);
		int before = c.ResearchQueue.Count;
		if (!gs.QueueResearch(techId, first: queued, out string reason))
		{
			_status.Text = reason;
			return;
		}
		_status.Text = TechRules.Knows(c, t) ? $"{t.Name} discovered with the stockpiled research!"
			: queued ? $"{t.Name} is now our first priority"
			: c.ResearchQueue.Count - before > 1 ? $"{t.Name} queued, after {c.ResearchQueue.Count - before - 1} technologies it builds on"
			: $"{t.Name} queued";
	}

	void Refresh()
	{
		GameState gs = GameState.Instance;
		Country c = gs?.PlayerCountry;
		if (!Visible || c == null)
			return;
		foreach (Node child in _cards.GetChildren())
		{
			_cards.RemoveChild(child);
			child.QueueFree();
		}
		var (points, sources) = gs.ResearchPoints(c);
		bool war = TechRules.AtWar(c, gs.Armies.Values);
		_cards.AddChild(Summary(gs, c, points, sources, war));
		_cards.AddChild(QueueCard(gs, c, points, war));
		foreach (TechCategory cat in TechRules.Categories)
			_trees[cat].Build(gs, c);
	}

	static PanelContainer Card(Color accent, float width = 0)
	{
		var panel = new PanelContainer { SizeFlagsHorizontal = width > 0 ? SizeFlags.Fill : SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(width, 0) };
		var style = HudStyle.Section();
		style.BorderColor = accent;
		style.SetBorderWidthAll(1);
		style.BorderWidthTop = 3;
		panel.AddThemeStyleboxOverride("panel", style);
		return panel;
	}

	/// <summary>The research points, where they come from, the stockpile, and what they flow into now.</summary>
	Control Summary(GameState gs, Country c, double points, List<TechRules.Source> sources, bool war)
	{
		Tech t = TechRules.Current(c, gs);
		var panel = Card(t != null ? CategoryColors[t.Category] : HudStyle.Gold, 420);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 3);
		panel.AddChild(box);
		var head = new HBoxContainer();
		head.AddThemeConstantOverride("separation", 6);
		box.AddChild(head);
		head.AddChild(new TextureRect
		{
			Texture = HudStyle.Texture("res://gfx/icons/science.png"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = new Vector2(26, 26),
		});
		head.AddChild(HudStyle.Title("Research points", TitleFont, 17));
		var rate = HudStyle.Body($"+{points:0.0} a month", BodyFont, 14, HudStyle.Good);
		rate.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rate.HorizontalAlignment = HorizontalAlignment.Right;
		rate.MouseFilter = MouseFilterEnum.Pass;
		rate.TooltipText = "From:\n" + string.Join("\n", sources.Where(s => s.Points > 0.005)
			.Select(s => $"  {s.Name} ({s.Units:0} {(s.Name.StartsWith("Soldiers") ? "regiments" : "units")}): +{s.Points:0.00}"))
			+ $"\nResearch modifiers: {LawRules.Mod(c, "research"):+0%;-0%;+0%}"
			+ $"\nAt war (an army in the field), military technology is researched {TechRules.WarMilitaryBoost:P0} faster{(war ? ": we are at war" : "")}.";
		head.AddChild(rate);
		box.AddChild(HudStyle.Body("All technologies, military, admin and science, share these points.", BodyFont, 12, HudStyle.Muted));

		if (t == null)
			box.AddChild(HudStyle.Body(c.ResearchQueue.Count > 0 ? "Nothing in the queue can be researched yet: the points are stockpiled"
				: "Researching nothing: the points are stockpiled", BodyFont, 13, HudStyle.Bad));
		else
		{
			var row = new HBoxContainer();
			box.AddChild(row);
			var name = HudStyle.Body($"{t.Name} ({t.Category.ToString().ToLowerInvariant()})", BodyFont, 14);
			name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			row.AddChild(name);
			int? months = TechRules.MonthsLeft(c, t, war);
			row.AddChild(HudStyle.Body($"{TechRules.Progress(c, t):0} / {t.Cost:0}" + (months is int m ? $" · {Duration(m)}" : "")
				+ (t.Category == TechCategory.Military && war ? " (war +50%)" : ""), BodyFont, 12, HudStyle.Muted));
			box.AddChild(new ProgressBar
			{
				MinValue = 0, MaxValue = t.Cost, Value = TechRules.Progress(c, t), ShowPercentage = false,
				CustomMinimumSize = new Vector2(0, 8),
			});
		}
		box.AddChild(HudStyle.Body($"Stockpile: {c.ResearchStockpile:0} points", BodyFont, 12, c.ResearchStockpile >= 1 ? HudStyle.Gold : HudStyle.Muted));
		return panel;
	}

	/// <summary>The queue in order of priority, with when each should be done at this pace.</summary>
	Control QueueCard(GameState gs, Country c, double points, bool war)
	{
		var panel = Card(HudStyle.Gold);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 2);
		panel.AddChild(box);
		var head = new HBoxContainer();
		box.AddChild(head);
		head.AddChild(HudStyle.Title("Priorities", TitleFont, 17));
		var hint = HudStyle.Body("  Click a technology to queue it (with what it builds on); click a queued one to put it first; right-click to take it out.",
			BodyFont, 11, HudStyle.Muted);
		hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		head.AddChild(hint);
		if (c.ResearchQueue.Count == 0)
		{
			box.AddChild(HudStyle.Body("The queue is empty.", BodyFont, 13, HudStyle.Bad));
			return panel;
		}
		var flow = new HFlowContainer();
		flow.AddThemeConstantOverride("h_separation", 4);
		flow.AddThemeConstantOverride("v_separation", 4);
		box.AddChild(flow);
		double months = 0;
		int n = 0;
		Tech current = TechRules.Current(c, gs);
		foreach (string id in c.ResearchQueue.ToList())
		{
			Tech t = gs.Definitions.GetTech(id);
			if (t == null)
				continue;
			n++;
			double rate = TechRules.Rate(c, t, points, war);
			months += rate > 0 ? (t.Cost - TechRules.Progress(c, t)) / rate : double.PositiveInfinity;
			var item = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
			var style = new StyleBoxFlat { BgColor = new Color(0.16f, 0.12f, 0.08f, 0.95f), BorderColor = CategoryColors[t.Category] };
			style.SetBorderWidthAll(t == current ? 2 : 1);
			style.SetCornerRadiusAll(4);
			style.ContentMarginLeft = style.ContentMarginRight = 6;
			style.ContentMarginTop = style.ContentMarginBottom = 2;
			item.AddThemeStyleboxOverride("panel", style);
			var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			row.AddThemeConstantOverride("separation", 5);
			item.AddChild(row);
			bool ready = TechRules.CanResearch(c, t, gs, out string why);
			var label = HudStyle.Body($"{n}. {t.Name}", BodyFont, 13, ready ? HudStyle.Text : HudStyle.Muted);
			label.MouseFilter = MouseFilterEnum.Ignore;
			row.AddChild(label);
			var when = HudStyle.Body(double.IsInfinity(months) ? "" : Duration((int)Math.Ceiling(months)), BodyFont, 11, HudStyle.Muted);
			when.MouseFilter = MouseFilterEnum.Ignore;
			row.AddChild(when);
			var up = HudStyle.Button("▲", BodyFont, 10);
			up.TooltipText = "Research it first";
			up.Disabled = n == 1;
			up.Pressed += () => OnChosen(id, false);
			row.AddChild(up);
			var x = HudStyle.Button("✕", BodyFont, 10);
			x.TooltipText = "Take it out of the queue (with what is queued after it that builds on it); its progress is kept";
			x.Pressed += () => OnChosen(id, true);
			row.AddChild(x);
			item.TooltipText = $"{t.Name} ({t.Category}): {TechRules.Progress(c, t):0} of {t.Cost:0} points"
				+ (t == current ? "\nOur research flows into it now." : ready ? "" : $"\nWaiting: {why}. The points go to the first technology after it that can take them.");
			flow.AddChild(item);
		}
		return panel;
	}

	static string Duration(int months) => months < 24 ? $"{months} months" : $"{months / 12} years";
}

/// <summary>
/// One kind of research as a tree: technologies in columns by how many of the same kind lead to them,
/// with lines from what each builds on. Requirements of other kinds are named on the node.
/// </summary>
public partial class TechTree : Control
{
	public enum State { Known, Researching, Queued, Available, Locked }

	public TechCategory Category { get; set; }
	public Font BodyFont { get; set; }

	/// <summary>A technology was clicked (by id): left click, or right click (true).</summary>
	public event Action<string, bool> Chosen;

	const float NodeWidth = 188, NodeHeight = 66, ColumnGap = 64, RowGap = 12, Margin = 10;

	readonly Dictionary<Tech, Rect2> _rects = new();
	readonly Dictionary<Tech, State> _states = new();

	public static StyleBoxFlat Style(State state, Color accent)
	{
		var s = new StyleBoxFlat();
		s.SetCornerRadiusAll(5);
		s.SetContentMarginAll(6);
		switch (state)
		{
			case State.Known:
				s.BgColor = new Color(accent.Darkened(0.55f), 0.95f);
				s.BorderColor = accent.Darkened(0.2f);
				s.SetBorderWidthAll(1);
				break;
			case State.Researching:
				s.BgColor = new Color(0.16f, 0.24f, 0.12f, 0.97f);
				s.BorderColor = new Color(0.7f, 0.95f, 0.5f);
				s.SetBorderWidthAll(3);
				break;
			case State.Queued:
				s.BgColor = new Color(0.24f, 0.2f, 0.12f, 0.97f);
				s.BorderColor = new Color(1f, 0.9f, 0.6f);
				s.SetBorderWidthAll(2);
				s.BorderWidthLeft = 6;
				break;
			case State.Available:
				s.BgColor = new Color(0.22f, 0.17f, 0.1f, 0.97f);
				s.BorderColor = accent;
				s.SetBorderWidthAll(2);
				break;
			default:
				s.BgColor = new Color(0.1f, 0.08f, 0.06f, 0.9f);
				s.BorderColor = new Color(0.4f, 0.35f, 0.28f, 0.6f);
				s.SetBorderWidthAll(1);
				break;
		}
		return s;
	}

	/// <summary>Lays the tree out again for the country's knowledge.</summary>
	public void Build(GameState gs, Country c)
	{
		foreach (Node child in GetChildren())
		{
			RemoveChild(child);
			child.QueueFree();
		}
		_rects.Clear();
		_states.Clear();
		var techs = gs.Definitions.Techs.Where(t => t.Category == Category).ToList();

		// columns: the longest chain of the same kind leading to each technology
		var depth = new Dictionary<Tech, int>();
		int Depth(Tech t)
		{
			if (depth.TryGetValue(t, out int d))
				return d;
			d = t.Requires.Where(r => r.Category == Category).Select(r => Depth(r) + 1).DefaultIfEmpty(0).Max();
			depth[t] = d;
			return d;
		}
		techs.ForEach(t => Depth(t));
		// rows: each column in file order, nudged toward the rows of what it builds on
		var rows = new Dictionary<Tech, float>();
		foreach (var column in techs.GroupBy(t => depth[t]).OrderBy(g => g.Key))
		{
			var ordered = column.OrderBy(t => t.Requires.Where(rows.ContainsKey).Select(r => rows[r]).DefaultIfEmpty(float.MaxValue).Average())
				.ThenBy(t => techs.IndexOf(t)).ToList();
			float next = 0;
			foreach (Tech t in ordered)
			{
				float wanted = t.Requires.Where(rows.ContainsKey).Select(r => rows[r]).DefaultIfEmpty(next).Min();
				rows[t] = Math.Max(next, wanted);
				next = rows[t] + 1;
			}
		}
		Color accent = ResearchPanel.CategoryColors[Category];
		Tech current = TechRules.Current(c, gs);
		foreach (Tech t in techs)
		{
			var rect = new Rect2(Margin + depth[t] * (NodeWidth + ColumnGap), Margin + rows[t] * (NodeHeight + RowGap), NodeWidth, NodeHeight);
			_rects[t] = rect;
			State state = TechRules.Knows(c, t) ? State.Known : t == current ? State.Researching
				: c.ResearchQueue.Contains(t.Id) ? State.Queued
				: TechRules.CanResearch(c, t, gs, out _) ? State.Available : State.Locked;
			_states[t] = state;
			AddChild(Node(gs, c, t, state, rect, accent));
		}
		CustomMinimumSize = new Vector2(_rects.Values.Max(r => r.End.X) + Margin, _rects.Values.Max(r => r.End.Y) + Margin);
		QueueRedraw();
	}

	Control Node(GameState gs, Country c, Tech t, State state, Rect2 rect, Color accent)
	{
		var node = new PanelContainer { Position = rect.Position, Size = rect.Size, CustomMinimumSize = rect.Size, MouseFilter = MouseFilterEnum.Stop };
		node.AddThemeStyleboxOverride("panel", Style(state, accent));
		var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		box.AddThemeConstantOverride("separation", 0);
		node.AddChild(box);
		Color text = state == State.Locked ? HudStyle.Muted : HudStyle.Text;
		var name = HudStyle.Body(t.Name, BodyFont, 14, text);
		name.MouseFilter = MouseFilterEnum.Ignore;
		name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		box.AddChild(name);
		double progress = TechRules.Progress(c, t);
		string line = state switch
		{
			State.Known => "Known",
			State.Researching => $"{progress:0} / {t.Cost:0}" + (TechRules.MonthsLeft(c, t, TechRules.AtWar(c, gs.Armies.Values)) is int m ? $" · {(m < 24 ? $"{m} months" : $"{m / 12} years")}" : ""),
			State.Queued => $"#{c.ResearchQueue.IndexOf(t.Id) + 1} in the queue · {t.Cost:0} points" + (progress > 0 ? $" ({progress:0} done)" : ""),
			_ => $"{t.Cost:0} points" + (progress > 0 ? $" ({progress:0} done)" : ""),
		};
		var status = HudStyle.Body(line, BodyFont, 11, state == State.Known ? accent.Lightened(0.45f) : HudStyle.Muted);
		status.MouseFilter = MouseFilterEnum.Ignore;
		box.AddChild(status);
		// requirements of other kinds, which the lines don't show
		var others = t.Requires.Where(r => r.Category != t.Category).ToList();
		string needs = others.Count > 0 ? "+ " + string.Join(", ", others.Select(r => r.Name)) : t.ConditionText;
		if (needs != null && state != State.Known)
		{
			bool met = others.All(r => TechRules.Knows(c, r)) && (t.Condition == null || t.Condition.Holds(gs, c));
			var need = HudStyle.Body(needs, BodyFont, 11, met ? HudStyle.Muted : HudStyle.Bad);
			need.MouseFilter = MouseFilterEnum.Ignore;
			need.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
			box.AddChild(need);
		}
		if (state == State.Researching)
		{
			box.AddChild(new ProgressBar
			{
				MinValue = 0, MaxValue = t.Cost, Value = progress, ShowPercentage = false,
				CustomMinimumSize = new Vector2(0, 5), MouseFilter = MouseFilterEnum.Ignore,
			});
		}
		node.TooltipText = Tooltip(gs, c, t, state);
		string id = t.Id;
		node.GuiInput += e =>
		{
			if (e is InputEventMouseButton { Pressed: true } mb && mb.ButtonIndex is MouseButton.Left or MouseButton.Right)
				Chosen?.Invoke(id, mb.ButtonIndex == MouseButton.Right);
		};
		return node;
	}

	static string Tooltip(GameState gs, Country c, Tech t, State state)
	{
		var lines = new List<string> { $"{t.Name} ({t.Category}, {t.Cost:0} points)", t.Description };
		if (t.Requires.Count > 0)
			lines.Add("Builds on: " + string.Join(", ", t.Requires.Select(r => r.Name + (TechRules.Knows(c, r) ? " ✔" : " ✘"))));
		if (t.Condition != null)
			lines.Add($"Requires: {t.ConditionText}" + (t.Condition.Holds(gs, c) ? " ✔" : " ✘"));
		foreach (var (key, value) in t.Modifiers)
			lines.Add($"  {ModifierText(key, value)}");
		var unlocks = TechRules.Unlocks(t, gs.Definitions);
		if (unlocks.Count > 0)
			lines.Add("Opens: " + string.Join(", ", unlocks));
		lines.Add(state switch
		{
			State.Known => "We know it.",
			State.Researching => "Our research flows into it now. Right-click to take it out of the queue.",
			State.Queued => "Queued. Click to research it first; right-click to take it out.",
			State.Available => "Click to queue it" + (c.ResearchStockpile >= 1 ? $": our stockpile of {c.ResearchStockpile:0} points pours into the queue." : "."),
			_ => (TechRules.CanResearch(c, t, gs, out string why) ? "" : why + ". ") + "Click to queue it with the technologies it builds on.",
		});
		return string.Join("\n", lines);
	}

	static string ModifierText(string key, double v) => key switch
	{
		"tax" => $"Tax {v:+0%;-0%}",
		"research" => $"Research {v:+0%;-0%}",
		"army_attack" => $"Army strength in battle {v:+0%;-0%}",
		"army_defense" => $"Army losses reduced: defense {v:+0%;-0%}",
		"army_speed" => $"Army march speed {v:+0%;-0%}",
		"diplomats" => $"Diplomats {v:+0;-0}",
		"tribe_relations" => $"Tribes' opinion of us {v:+0;-0}",
		"levy_share" => $"Levies {v:+0%;-0%} of those who can serve",
		"years_to_core" => $"Years for new land to become a core {v:+0;-0}",
		"uprising" => $"Uprising risk {v:+0%;-0%}",
		"building_cost" => $"Building cost {v:+0%;-0%}",
		"mercenary_pay" => $"Mercenary pay {v:+0%;-0%}",
		_ => $"{key} {v:+0.##;-0.##}",
	};

	/// <summary>Lines from each technology to those of the same kind that build on it.</summary>
	public override void _Draw()
	{
		foreach (var (t, rect) in _rects)
		{
			foreach (Tech r in t.Requires.Where(_rects.ContainsKey))
			{
				Rect2 from = _rects[r];
				var a = new Vector2(from.End.X, from.Position.Y + from.Size.Y / 2);
				var b = new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y / 2);
				bool lit = _states[r] == State.Known;
				Color color = lit ? ResearchPanel.CategoryColors[Category] : new Color(0.45f, 0.4f, 0.32f, 0.6f);
				// a curve from each requirement, so every line can be followed to its end
				float bend = Math.Max(ColumnGap * 0.6f, (b.X - a.X) * 0.35f);
				var curve = new Vector2[17];
				for (int i = 0; i < curve.Length; i++)
				{
					float u = i / (float)(curve.Length - 1);
					curve[i] = a.BezierInterpolate(a + new Vector2(bend, 0), b - new Vector2(bend, 0), b, u);
				}
				DrawPolyline(curve, color, lit ? 2.5f : 1.5f, true);
				DrawCircle(b, lit ? 3.5f : 2.5f, color);
			}
		}
	}
}
