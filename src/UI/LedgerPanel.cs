using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.UI;

/// <summary>The ledger: tables of the world's countries, its wars, its trade nodes and our provinces.</summary>
public partial class LedgerPanel : PanelContainer
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }
	[Export] public NodePath CameraRigPath { get; set; }
	[Export] public NodePath DiplomacyPanelPath { get; set; }

	TabContainer _tabs;
	readonly Dictionary<string, VBoxContainer> _pages = new();

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", HudStyle.Panel(0.97f));
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 6);
		AddChild(root);
		var head = new HBoxContainer();
		root.AddChild(head);
		var title = HudStyle.Title("Ledger", TitleFont, 24);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		head.AddChild(title);
		var close = HudStyle.Button("✕", BodyFont);
		close.Pressed += () => Visible = false;
		head.AddChild(close);
		_tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		if (BodyFont != null) _tabs.AddThemeFontOverride("font", BodyFont);
		_tabs.AddThemeFontSizeOverride("font_size", 14);
		root.AddChild(_tabs);
		foreach (string name in new[] { "Countries", "Wars", "Trade", "Provinces" })
		{
			var scroll = new ScrollContainer { Name = name, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
			var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			scroll.AddChild(box);
			_tabs.AddChild(scroll);
			_pages[name] = box;
		}
		_tabs.TabChanged += _ => Refresh();
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.MonthAdvanced += Refresh;
		gs.WarsChanged += Refresh;
		gs.OwnershipChanged += Refresh;
	}

	public override void _ExitTree()
	{
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.MonthAdvanced -= Refresh;
		gs.WarsChanged -= Refresh;
		gs.OwnershipChanged -= Refresh;
	}

	public void Toggle()
	{
		Visible = !Visible;
		Refresh();
	}

	void Refresh()
	{
		if (!Visible)
			return;
		GameState gs = GameState.Instance;
		Country us = gs?.PlayerCountry;
		if (us == null)
		{
			Visible = false;
			return;
		}
		switch (_tabs.CurrentTab)
		{
			case 0: FillCountries(gs, us); break;
			case 1: FillWars(gs, us); break;
			case 2: FillTrade(gs, us); break;
			default: FillProvinces(gs, us); break;
		}
	}

	GridContainer Table(string page, params string[] columns)
	{
		VBoxContainer box = _pages[page];
		foreach (Node child in box.GetChildren())
		{
			box.RemoveChild(child);
			child.QueueFree();
		}
		var grid = new GridContainer { Columns = columns.Length };
		grid.AddThemeConstantOverride("h_separation", 16);
		grid.AddThemeConstantOverride("v_separation", 3);
		box.AddChild(grid);
		foreach (string c in columns)
			grid.AddChild(HudStyle.Body(c, BodyFont, 12, HudStyle.Gold));
		return grid;
	}

	void Cell(GridContainer grid, string text, Color? color = null, System.Action onClick = null)
	{
		if (onClick == null)
		{
			grid.AddChild(HudStyle.Body(text, BodyFont, 13, color ?? HudStyle.Text));
			return;
		}
		var b = HudStyle.Button(text, BodyFont, 13);
		b.Alignment = HorizontalAlignment.Left;
		b.AddThemeColorOverride("font_color", color ?? HudStyle.Text);
		b.Pressed += () => onClick();
		grid.AddChild(b);
	}

	void FillCountries(GameState gs, Country us)
	{
		var grid = Table("Countries", "#", "Country", "Provinces", "People", "Income/month", "Strength", "Prestige", "Stability", "Allies", "Rivals");
		var rows = gs.SettledCountries.Select(c => (Country: c, Owned: gs.ProvincesOf(c.Tag).ToList())).OrderByDescending(r => r.Owned.Sum(p => p.TotalUnits)).ToList();
		int rank = 0;
		foreach (var (c, owned) in rows)
		{
			rank++;
			Color color = c == us ? HudStyle.Gold : HudStyle.Text;
			Cell(grid, $"{rank}", HudStyle.Muted);
			string tag = c.Tag;
			Cell(grid, c.Name, color, c == us ? null : () => GetNodeOrNull<DiplomacyPanel>(DiplomacyPanelPath ?? new NodePath())?.Open(tag));
			Cell(grid, $"{owned.Count}", color);
			Cell(grid, $"{owned.Sum(p => p.TotalUnits) * PopGroup.PeoplePerUnit:N0}", color);
			Cell(grid, $"{c.LastLedger.Income:0.0}", color);
			Cell(grid, $"{WarRules.Strength(c, gs.Provinces, gs.Armies.Values, gs.Date):0}", color);
			Cell(grid, $"{c.Prestige:0}", color);
			Cell(grid, $"{c.Stability:+0;-0;0}", c.Stability < 0 ? HudStyle.Bad : color);
			Cell(grid, string.Join(", ", c.Allies.Select(t => gs.GetCountry(t)?.Name)), HudStyle.Muted);
			Cell(grid, string.Join(", ", c.Rivals.Select(t => gs.GetCountry(t)?.Name)), HudStyle.Muted);
		}
	}

	void FillWars(GameState gs, Country us)
	{
		var grid = Table("Wars", "War", "Since", "Attackers", "Defenders", "Score", "Battles");
		if (gs.Wars.Count == 0)
			_pages["Wars"].AddChild(HudStyle.Body("The world is at peace.", BodyFont, 14, HudStyle.Muted));
		foreach (War w in gs.Wars.Values.OrderByDescending(w => w.Includes(us.Tag)).ThenBy(w => w.Started.Day))
		{
			Color color = w.Includes(us.Tag) ? HudStyle.Gold : HudStyle.Text;
			int id = w.Id;
			Cell(grid, w.Name(gs.Countries), color, w.Includes(us.Tag) ? () => GetNodeOrNull<DiplomacyPanel>(DiplomacyPanelPath ?? new NodePath())?.Open($"war:{id}") : null);
			Cell(grid, gs.Definitions.DefaultCalendar.Format(w.Started), HudStyle.Muted);
			Cell(grid, string.Join(", ", w.Attackers.Select(t => gs.GetCountry(t)?.Name)), color);
			Cell(grid, string.Join(", ", w.Defenders.Select(t => gs.GetCountry(t)?.Name)), color);
			Cell(grid, $"{w.Score:+0;-0;0} (attacker)", w.Score >= 0 ? HudStyle.Good : HudStyle.Bad);
			Cell(grid, $"{w.Battles.Count}", HudStyle.Muted);
		}
	}

	void FillTrade(GameState gs, Country us)
	{
		var grid = Table("Trade", "Node", "Worth", "Made here", "From upstream", "Top power", "Our share", "Our income");
		if (gs.Trade == null)
			return;
		foreach (NodeReport r in gs.Trade.Nodes.Values.OrderByDescending(r => r.Total).Take(40))
		{
			var top = r.Power.OrderByDescending(kv => kv.Value).FirstOrDefault();
			bool ours = r.Power.ContainsKey(us.Tag);
			Color color = ours ? HudStyle.Gold : HudStyle.Text;
			Cell(grid, r.Node.Name + (r.Node.End ? " (end)" : ""), color);
			Cell(grid, $"{r.Total:0.0}", color);
			Cell(grid, $"{r.Local:0.0}", HudStyle.Muted);
			Cell(grid, $"{r.Incoming:0.0}", HudStyle.Muted);
			Cell(grid, top.Key == null ? "nobody" : $"{gs.GetCountry(top.Key)?.Name} ({top.Value:0})", HudStyle.Muted);
			Cell(grid, ours ? $"{r.Share(us.Tag):P0}" : "", color);
			Cell(grid, r.Collected.TryGetValue(us.Tag, out double got) ? $"{got * TradeRules.TradeIncomeFactor:0.0}" : "", color);
		}
	}

	void FillProvinces(GameState gs, Country us)
	{
		var grid = Table("Provinces", "Province", "People", "Tax", "Trade value", "Garrison", "Fort", "Unrest", "Held");
		foreach (Province p in gs.ProvincesOf(us.Tag).OrderByDescending(p => p.TotalUnits))
		{
			int id = p.Id;
			Cell(grid, p.Name, p.Id == us.CapitalId ? HudStyle.Gold : HudStyle.Text, () => { gs.SelectProvince(id); MapCamera.FocusProvince(GetNodeOrNull<Node3D>(CameraRigPath ?? new NodePath()), id); });
			Cell(grid, $"{p.TotalUnits * PopGroup.PeoplePerUnit:N0}");
			Cell(grid, $"{EconomyRules.Tax(p, gs.Date, us):0.0}");
			Cell(grid, $"{gs.Trade?.ProvinceValue.GetValueOrDefault(p.Id) ?? 0:0.0}");
			Cell(grid, $"{p.Control?.Garrison.Count ?? 0}");
			Cell(grid, p.FortLevel > 0 ? FortRules.Names[p.FortLevel] : "", HudStyle.Muted);
			double risk = ControlRules.UprisingChance(p, us, gs.Date);
			Cell(grid, $"{risk:P1}", risk > 0.01 ? HudStyle.Bad : HudStyle.Muted);
			Cell(grid, p.OccupierTag != null ? $"occupied by {gs.GetCountry(p.OccupierTag)?.Adjective}" : p.SiegerTag != null && p.SiegerTag != us.Tag ? "under siege" : p.IsCore ? "core" : p.Control?.Kind.ToString().ToLowerInvariant(), p.OccupierTag != null ? HudStyle.Bad : HudStyle.Muted);
		}
	}
}
