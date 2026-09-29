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
			+ "Points flow into the technology chosen once its requirements are met; with nothing chosen, they are stockpiled for the next.",
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
		foreach (var (state, text) in new[] { (TechTree.State.Known, "Known"), (TechTree.State.Researching, "Researching"), (TechTree.State.Available, "Can be researched: click"), (TechTree.State.Locked, "Requirements not met") })
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

	/// <summary>Opens the window on a kind of research, or closes it if it is already showing that one.</summary>
	public void Toggle(TechCategory category)
	{
		int index = Array.IndexOf(TechRules.Categories, category);
		Visible = !Visible || _tabs.CurrentTab != index;
		_tabs.CurrentTab = index;
		_status.Text = "";
		Refresh();
	}

	void OnChosen(string techId)
	{
		GameState gs = GameState.Instance;
		Tech t = gs.Definitions.GetTech(techId);
		_status.Text = gs.SelectResearch(techId, out string reason)
			? TechRules.Knows(gs.PlayerCountry, t) ? $"{t.Name} discovered with the stockpiled research!" : $"Researching {t.Name}"
			: reason;
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
		foreach (TechCategory cat in TechRules.Categories)
		{
			_cards.AddChild(Card(gs, c, cat, points[cat], sources, war));
			_trees[cat].Build(gs, c);
		}
	}

	/// <summary>A kind of research: its points, its stockpile and what it flows into.</summary>
	Control Card(GameState gs, Country c, TechCategory cat, double points, List<TechRules.Source> sources, bool war)
	{
		var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		var style = HudStyle.Section();
		style.BorderColor = CategoryColors[cat];
		style.SetBorderWidthAll(1);
		style.BorderWidthTop = 3;
		panel.AddThemeStyleboxOverride("panel", style);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 3);
		panel.AddChild(box);
		var head = new HBoxContainer();
		head.AddThemeConstantOverride("separation", 6);
		box.AddChild(head);
		head.AddChild(new TextureRect
		{
			Texture = HudStyle.Texture($"res://gfx/icons/{cat.ToString().ToLowerInvariant()}.png"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = new Vector2(26, 26),
		});
		head.AddChild(HudStyle.Title($"{cat}", TitleFont, 17));
		var rate = HudStyle.Body($"+{points:0.0} a month" + (cat == TechCategory.Military && war ? " (war +50%)" : ""), BodyFont, 14, HudStyle.Good);
		rate.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rate.HorizontalAlignment = HorizontalAlignment.Right;
		rate.MouseFilter = MouseFilterEnum.Pass;
		rate.TooltipText = "From:\n" + string.Join("\n", sources.Where(s => s.Points[cat] > 0.005)
			.Select(s => $"  {s.Name} ({s.Units:0} {(s.Name.StartsWith("Soldiers") ? "regiments" : "units")}): +{s.Points[cat]:0.00}"))
			+ $"\nResearch modifiers: {LawRules.Mod(c, "research"):+0%;-0%;+0%}"
			+ (cat == TechCategory.Military ? $"\nAt war (an army in the field): +{TechRules.WarMilitaryBoost:P0}{(war ? " now" : "")}" : "");
		head.AddChild(rate);

		Tech t = TechRules.Current(c, cat);
		double stock = c.ResearchStockpile.GetValueOrDefault(cat);
		if (t == null)
			box.AddChild(HudStyle.Body("Researching nothing: the points are stockpiled", BodyFont, 13, HudStyle.Bad));
		else
		{
			var row = new HBoxContainer();
			box.AddChild(row);
			var name = HudStyle.Body(t.Name, BodyFont, 14);
			name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			row.AddChild(name);
			int? months = TechRules.MonthsLeft(c, t);
			row.AddChild(HudStyle.Body($"{TechRules.Progress(c, t):0} / {t.Cost:0}" + (months is int m ? $" · {Duration(m)}" : ""), BodyFont, 12, HudStyle.Muted));
			var stop = HudStyle.Button("Stop", BodyFont, 11);
			stop.TooltipText = "Stop researching it: the points go to the stockpile, and the progress is kept";
			stop.Pressed += () => gs.StopResearch(cat);
			row.AddChild(stop);
			box.AddChild(new ProgressBar
			{
				MinValue = 0, MaxValue = t.Cost, Value = TechRules.Progress(c, t), ShowPercentage = false,
				CustomMinimumSize = new Vector2(0, 8),
			});
		}
		box.AddChild(HudStyle.Body($"Stockpile: {stock:0} points", BodyFont, 12, stock >= 1 ? HudStyle.Gold : HudStyle.Muted));
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
	public enum State { Known, Researching, Available, Locked }

	public TechCategory Category { get; set; }
	public Font BodyFont { get; set; }

	/// <summary>A technology was clicked (by id).</summary>
	public event Action<string> Chosen;

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
		Tech current = TechRules.Current(c, Category);
		foreach (Tech t in techs)
		{
			var rect = new Rect2(Margin + depth[t] * (NodeWidth + ColumnGap), Margin + rows[t] * (NodeHeight + RowGap), NodeWidth, NodeHeight);
			_rects[t] = rect;
			State state = TechRules.Knows(c, t) ? State.Known : t == current ? State.Researching
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
			State.Researching => $"{progress:0} / {t.Cost:0}" + (TechRules.MonthsLeft(c, t) is int m ? $" · {(m < 24 ? $"{m} months" : $"{m / 12} years")}" : ""),
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
			if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
				Chosen?.Invoke(id);
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
			State.Researching => "Our research of this kind flows into it.",
			State.Available => "Click to research it" + (c.ResearchStockpile.GetValueOrDefault(t.Category) >= 1 ? $": our stockpile of {c.ResearchStockpile[t.Category]:0} points pours into it." : "."),
			_ => TechRules.CanResearch(c, t, gs, out string why) ? "" : why,
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
