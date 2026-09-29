using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.UI;

/// <summary>
/// The player's country: an Overview (ruler, succession, finances, people) and the Laws, where the
/// country sets its own code: succession, who is a citizen, how non-citizens and other cultures are
/// treated, religious law, what is legal, and how the frontier tribes are dealt with; and the
/// Military: raising the levies, how they are armed, and the armies in the field.
/// </summary>
public partial class CountryPanel : PanelContainer
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }
	[Export] public NodePath CameraRigPath { get; set; }

	TabContainer _tabs;
	VBoxContainer _overview;
	VBoxContainer _laws;
	VBoxContainer _military;
	Label _title;
	Label _status;

	static readonly Dictionary<string, (string Label, bool Percent, bool GoodWhenUp)> ModifierNames = new()
	{
		["tax"] = ("Tax", true, true),
		["noncitizen_tax"] = ("Tax from non-citizens", true, true),
		["noncitizen_manpower"] = ("Levies from non-citizens", true, true),
		["uprising"] = ("Uprising risk", true, false),
		["years_to_core"] = ("Years for new land to become a core", false, false),
		["tribe_relations"] = ("Tribes' opinion of us", false, true),
		["alliance_chance"] = ("Chance tribes accept an alliance", true, true),
		["building_cost"] = ("Building cost", true, false),
		["mercenary_pay"] = ("Mercenary pay", true, false),
		["change_cost"] = ("Cost of changing laws", true, false),
	};

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", HudStyle.Panel(0.97f));
		CustomMinimumSize = new Vector2(640, 560);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;

		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 8);
		AddChild(root);
		var header = new HBoxContainer();
		root.AddChild(header);
		_title = HudStyle.Title("", TitleFont, 26);
		_title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddChild(_title);
		var capital = HudStyle.Button("Go to capital", BodyFont, 13);
		capital.Pressed += () =>
		{
			if (GameState.Instance?.PlayerCountry is { } c)
				MapCamera.FocusProvince(GetNodeOrNull<Node3D>(CameraRigPath ?? new NodePath()), c.CapitalId);
		};
		header.AddChild(capital);
		var close = HudStyle.Button("✕", BodyFont);
		close.Pressed += () => Visible = false;
		header.AddChild(close);

		_tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		if (BodyFont != null)
			_tabs.AddThemeFontOverride("font", BodyFont);
		_tabs.AddThemeFontSizeOverride("font_size", 15);
		_tabs.AddThemeColorOverride("font_selected_color", HudStyle.Gold);
		_tabs.AddThemeColorOverride("font_unselected_color", HudStyle.Muted);
		_tabs.AddThemeStyleboxOverride("panel", new StyleBoxEmpty { ContentMarginTop = 8 });
		var tab = new StyleBoxFlat { BgColor = new Color(0.3f, 0.23f, 0.13f, 0.6f) };
		tab.SetCornerRadiusAll(4);
		tab.SetContentMarginAll(6);
		var sel = (StyleBoxFlat)tab.Duplicate();
		sel.BgColor = new Color(0.5f, 0.38f, 0.2f, 0.8f);
		_tabs.AddThemeStyleboxOverride("tab_unselected", tab);
		_tabs.AddThemeStyleboxOverride("tab_hovered", sel);
		_tabs.AddThemeStyleboxOverride("tab_selected", sel);
		root.AddChild(_tabs);
		_overview = AddTab("Overview");
		_laws = AddTab("Laws");
		_military = AddTab("Military");

		_status = HudStyle.Body("", BodyFont, 13, HudStyle.Muted);
		root.AddChild(_status);

		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.LawsChanged += Refresh;
		gs.MonthAdvanced += Refresh;
		gs.RulerChanged += OnRulerChanged;
		gs.TribesChanged += Refresh;
		gs.ArmiesChanged += Refresh;
	}

	public override void _ExitTree()
	{
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.LawsChanged -= Refresh;
		gs.MonthAdvanced -= Refresh;
		gs.RulerChanged -= OnRulerChanged;
		gs.TribesChanged -= Refresh;
		gs.ArmiesChanged -= Refresh;
	}

	void OnRulerChanged(string tag) => Refresh();

	VBoxContainer AddTab(string title)
	{
		var scroll = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		box.AddThemeConstantOverride("separation", 8);
		scroll.AddChild(box);
		_tabs.AddChild(scroll);
		return box;
	}

	public void Toggle(int tab = -1)
	{
		Visible = !Visible || tab >= 0 && _tabs.CurrentTab != tab;
		if (tab >= 0)
			_tabs.CurrentTab = tab;
		_status.Text = "";
		Refresh();
	}

	void Refresh()
	{
		if (!Visible)
			return;
		GameState gs = GameState.Instance;
		Country c = gs?.PlayerCountry;
		if (c == null)
		{
			Visible = false;
			return;
		}
		_title.Text = c.Name;
		FillOverview(gs, c);
		FillLaws(gs, c);
		FillMilitary(gs, c);
	}

	// ------------------------------------------------------------------------------------ Overview

	void FillOverview(GameState gs, Country c)
	{
		Clear(_overview);
		var top = new HBoxContainer();
		top.AddThemeConstantOverride("separation", 14);
		_overview.AddChild(top);
		var flag = new TextureRect { Texture = HudStyle.Texture(c.CurrentFlag?.ImagePath) };
		var flagFrame = HudStyle.Framed(flag);
		flagFrame.CustomMinimumSize = new Vector2(120, 80);
		flagFrame.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		top.AddChild(flagFrame);
		var portrait = new TextureRect { Texture = HudStyle.Texture(c.Ruler?.Portrait) };
		var portraitFrame = HudStyle.Framed(portrait);
		portraitFrame.CustomMinimumSize = new Vector2(112, 140);
		top.AddChild(portraitFrame);
		var ruler = new VBoxContainer();
		top.AddChild(ruler);
		Character r = c.Ruler;
		ruler.AddChild(HudStyle.Title(r == null ? "No ruler" : $"{c.RulerTitle} {r.Name}", TitleFont, 20));
		if (r != null)
		{
			if (r.FullName != null)
				ruler.AddChild(HudStyle.Body(r.FullName, BodyFont, 13, HudStyle.Muted));
			ruler.AddChild(HudStyle.Body($"Age {r.AgeOn(gs.Date)} · {r.Dynasty}", BodyFont, 14));
			ruler.AddChild(HudStyle.Body($"Of the {r.Culture?.Name} {r.Occupation?.Name.ToLowerInvariant()} ({r.Religion?.Name})", BodyFont, 13, HudStyle.Muted));
		}
		ruler.AddChild(HudStyle.Body($"Next ruler: {LawRules.SuccessionText(c)} ({LawRules.Option(c, gs.Definitions.GetLaw("succession"))?.Name})", BodyFont, 13, HudStyle.Muted));
		ruler.AddChild(HudStyle.Body(c.Government, BodyFont, 13, HudStyle.Muted));

		var owned = gs.ProvincesOf(c.Tag).ToList();
		Ledger ledger = EconomyRules.MonthlyLedger(c, owned, gs.Tribes.Values, gs.Armies.Values, gs.Date);
		long citizens = 0, others = 0;
		foreach (PopGroup pop in owned.SelectMany(p => p.Pops))
		{
			if (LawRules.IsCitizen(c, pop))
				citizens += pop.Units;
			else
				others += pop.Units;
		}
		var grid = new GridContainer { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 18);
		grid.AddThemeConstantOverride("v_separation", 4);
		_overview.AddChild(grid);
		void Row(string key, string value, Color? color = null)
		{
			grid.AddChild(HudStyle.Body(key, BodyFont, 14, HudStyle.Muted));
			grid.AddChild(HudStyle.Body(value, BodyFont, 14, color ?? HudStyle.Text));
		}
		Row("Capital", gs.CapitalText(c));
		Row("Provinces", $"{owned.Count}: " + string.Join(", ", owned.GroupBy(p => p.Control?.Kind).Select(g => $"{g.Count()} {Kind(g.Key)}")));
		Row("People", $"{(citizens + others) * PopGroup.PeoplePerUnit:N0}");
		Row("Citizens", $"{citizens * PopGroup.PeoplePerUnit:N0} ({LawRules.Option(c, gs.Definitions.GetLaw("citizenship"))?.Name})");
		Row("Non-citizens", $"{others * PopGroup.PeoplePerUnit:N0} ({LawRules.Option(c, gs.Definitions.GetLaw("noncitizens"))?.Name})");
		Row("Treasury", $"{c.Gold:0} gold");
		Row("Each month", $"{ledger.Balance:+0.0;-0.0} gold: +{ledger.Tax:0.0} tax, -{ledger.Armies:0.0} armies, -{ledger.Garrisons:0.0} garrisons, -{ledger.Mercenaries:0.0} mercenaries",
			ledger.Balance >= 0 ? HudStyle.Good : HudStyle.Bad);
		long men = owned.Sum(p => p.UnitsOf(Sex.Male)), women = owned.Sum(p => p.UnitsOf(Sex.Female));
		Row("Men and women", $"{men * PopGroup.PeoplePerUnit:N0} men · {women * PopGroup.PeoplePerUnit:N0} women at home");
		Row("Under arms", $"{MilitaryRules.UnderArms(c, gs.Armies.Values, owned)} regiments · {gs.AvailableLevies(c)} more to levy · {TribeRules.Mercenaries(gs.Tribes.Values, c.Tag)} mercenaries");
		Row("Allied tribes", string.Join(", ", gs.Tribes.Values.Where(t => t.AlliedTag == c.Tag).Select(t => t.Name)) is { Length: > 0 } a ? a : "None");
	}

	static string Kind(ControlKind? k) => k switch
	{
		ControlKind.Core => "cores",
		ControlKind.Absorbed => "absorbed",
		ControlKind.Subjugated => "subjugated",
		_ => "other",
	};

	// ---------------------------------------------------------------------------------------- Laws

	void FillLaws(GameState gs, Country c)
	{
		Clear(_laws);
		var defs = gs.Definitions;
		var intro = HudStyle.Body($"Our laws and code. Changing a law costs {LawRules.ChangeCost(c, defs):0} gold, and it can't change again for {defs.LawChangeCooldownYears:0} years. We have {c.Gold:0} gold.",
			BodyFont, 13, HudStyle.Muted);
		intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		intro.CustomMinimumSize = new Vector2(580, 0);
		_laws.AddChild(intro);

		foreach (var group in defs.Laws.GroupBy(l => l.Group))
		{
			_laws.AddChild(HudStyle.Title(group.Key, TitleFont, 18));
			foreach (LawDefinition law in group)
			{
				var panel = new PanelContainer();
				panel.AddThemeStyleboxOverride("panel", HudStyle.Section());
				_laws.AddChild(panel);
				var box = new VBoxContainer();
				box.AddThemeConstantOverride("separation", 4);
				panel.AddChild(box);
				LawOption current = LawRules.Option(c, law);
				var head = new HBoxContainer();
				box.AddChild(head);
				var name = HudStyle.Body(law.Name, BodyFont, 15);
				name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				head.AddChild(name);
				head.AddChild(HudStyle.Body(current.Name, BodyFont, 14, HudStyle.Gold));
				var desc = HudStyle.Body($"{law.Description} Now: {current.Description}", BodyFont, 12, HudStyle.Muted);
				desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				desc.CustomMinimumSize = new Vector2(560, 0);
				box.AddChild(desc);

				var options = new HFlowContainer();
				options.AddThemeConstantOverride("h_separation", 6);
				box.AddChild(options);
				foreach (LawOption option in law.Options)
				{
					var b = HudStyle.Button(option.Name, BodyFont, 13);
					b.ToggleMode = true;
					b.SetPressedNoSignal(option == current);
					if (option == current)
					{
						// the law in force: gold, so it stands out from the choices
						var gold = new StyleBoxFlat { BgColor = new Color(0.78f, 0.6f, 0.28f), BorderColor = new Color(1f, 0.88f, 0.55f) };
						gold.SetBorderWidthAll(2);
						gold.SetCornerRadiusAll(4);
						gold.ContentMarginLeft = gold.ContentMarginRight = 10;
						gold.ContentMarginTop = gold.ContentMarginBottom = 4;
						b.AddThemeStyleboxOverride("pressed", gold);
						b.AddThemeStyleboxOverride("hover_pressed", gold);
						b.AddThemeColorOverride("font_pressed_color", new Color(0.12f, 0.08f, 0.04f));
						b.AddThemeColorOverride("font_hover_pressed_color", new Color(0.12f, 0.08f, 0.04f));
					}
					bool can = option == current || LawRules.CanChange(c, law, option, gs, defs, out _);
					LawRules.CanChange(c, law, option, gs, defs, out string why);
					b.Disabled = option != current && !can;
					b.TooltipText = Describe(option) + (option == current ? "\n\nOur law now." : why != null ? $"\n\n{why}" : $"\n\nAdopt it for {LawRules.ChangeCost(c, defs):0} gold.");
					string lawId = law.Id, optionId = option.Id;
					b.Pressed += () =>
					{
						if (option == current)
						{
							b.SetPressedNoSignal(true);
							return;
						}
						_status.Text = gs.ChangeLaw(lawId, optionId, out string reason) ? "" : reason;
					};
					options.AddChild(b);
				}
			}
		}
	}

	// ------------------------------------------------------------------------------------ Military

	void FillMilitary(GameState gs, Country c)
	{
		Clear(_military);
		var defs = gs.Definitions;
		var owned = gs.ProvincesOf(c.Tag).ToList();
		Label Text(string text, int size = 13, Color? color = null)
		{
			var l = HudStyle.Body(text, BodyFont, size, color ?? HudStyle.Muted);
			l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			l.CustomMinimumSize = new Vector2(580, 0);
			return l;
		}
		VBoxContainer Section(string title)
		{
			var panel = new PanelContainer();
			panel.AddThemeStyleboxOverride("panel", HudStyle.Section());
			_military.AddChild(panel);
			var box = new VBoxContainer();
			box.AddThemeConstantOverride("separation", 4);
			panel.AddChild(box);
			box.AddChild(HudStyle.Title(title, TitleFont, 17));
			return box;
		}

		// the levies
		var levies = Section("The levies");
		int available = gs.AvailableLevies(c);
		int under = MilitaryRules.UnderArms(c, gs.Armies.Values, owned);
		bool women = LawRules.Effect(c, "women_serve") > 0;
		double canServe = MilitaryRules.CanServeAtHome(c, owned, gs.Date);
		levies.AddChild(Text($"We have no standing army: in war, the {(women ? "men and women" : "men")} of our provinces are called up as levies, "
			+ "each unit of 1000 people a regiment. Those levied leave their fields and pay no tax until they come home."));
		levies.AddChild(Text($"{LawRules.Option(c, defs.GetLaw("levies"))?.Name}: {MilitaryRules.LevyShare(c):P0} of those who can serve. "
			+ $"{LawRules.Option(c, defs.GetLaw("women_in_arms"))?.Name}. About {(long)canServe * PopGroup.PeoplePerUnit:N0} at home answer the levy "
			+ "(non-citizens as the law on them says, absorbed tribes less their separatism, subjugated nomads not at all).", 13, HudStyle.Text));
		levies.AddChild(Text($"Under arms: {under} regiments. We can levy {available} more.", 14, HudStyle.Text));
		var raiseRow = new HBoxContainer();
		raiseRow.AddThemeConstantOverride("separation", 8);
		levies.AddChild(raiseRow);
		var count = new SpinBox { MinValue = 1, MaxValue = Math.Max(1, available), Value = Math.Max(1, available), Suffix = "reg.", Editable = available > 0, CustomMinimumSize = new Vector2(120, 0) };
		raiseRow.AddChild(count);
		var raise = HudStyle.Button("Raise the levies", BodyFont, 14);
		raise.Disabled = available < 1;
		raiseRow.AddChild(raise);
		var preview = HudStyle.Body("", BodyFont, 12, HudStyle.Muted);
		preview.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		raiseRow.AddChild(preview);
		void UpdatePreview()
		{
			if (available < 1)
			{
				preview.Text = "No one left to levy";
				return;
			}
			var (types, cost) = MilitaryRules.Arm(c, (int)count.Value, defs, gs);
			preview.Text = string.Join(", ", types.GroupBy(t => t).Select(g => $"{g.Count()} {g.Key.Name.ToLowerInvariant()}"))
				+ (cost > 0 ? $" · arms cost {cost:0} gold" : "");
		}
		count.ValueChanged += _ => UpdatePreview();
		UpdatePreview();
		raise.Pressed += () =>
		{
			Army army = gs.RaiseLevies((int)count.Value, out string why);
			_status.Text = army != null ? $"The {army.Name} musters at {gs.CapitalText(c)}. Right-click the map to march it." : why;
		};

		// how they are armed
		var arms = Section("How our levies are armed");
		arms.AddChild(Text("Each kind of troops' share of the levies raised. Horses and engines must be paid for when the levies are raised; "
			+ "those the treasury can't pay for are raised as spearmen. Siege engines are powerful but need a regiment of foot each to screen them."));
		var template = MilitaryRules.Template(c, defs, gs).ToDictionary(x => x.Type, x => x.Share);
		var grid = new GridContainer { Columns = 6 };
		grid.AddThemeConstantOverride("h_separation", 12);
		grid.AddThemeConstantOverride("v_separation", 3);
		arms.AddChild(grid);
		foreach (string h in new[] { "", "Attack", "Defense", "Speed", "Cost · upkeep", "Share" })
			grid.AddChild(HudStyle.Body(h, BodyFont, 12, HudStyle.Muted));
		foreach (var group in MilitaryRules.AvailableTypes(defs, gs, c).GroupBy(t => t.Category))
		{
			grid.AddChild(HudStyle.Body(group.Key.ToString(), TitleFont, 15, HudStyle.Gold));
			for (int i = 0; i < 5; i++)
				grid.AddChild(new Control());
			foreach (UnitType t in group)
			{
				var name = HudStyle.Body(t.Name, BodyFont, 14);
				name.TooltipText = t.Description + (t.Siege > 0 ? $"\nSiege power {t.Siege:0.#}" : "");
				name.MouseFilter = MouseFilterEnum.Pass;
				grid.AddChild(name);
				grid.AddChild(HudStyle.Body($"{t.Attack:0.0#}", BodyFont, 13));
				grid.AddChild(HudStyle.Body($"{t.Defense:0.0#}", BodyFont, 13));
				grid.AddChild(HudStyle.Body($"{t.Speed:0} km/day", BodyFont, 13));
				grid.AddChild(HudStyle.Body($"{t.Cost:0} · {t.Upkeep:0.0#}/month", BodyFont, 13));
				var share = new SpinBox { MinValue = 0, MaxValue = 100, Step = 5, Value = template.GetValueOrDefault(t), Suffix = "%" };
				string id = t.Id;
				share.ValueChanged += v =>
				{
					gs.SetLevyShare(id, (int)v);
					UpdatePreview();
				};
				grid.AddChild(share);
			}
		}
		var later = defs.UnitTypes.Where(t => !MilitaryRules.IsAvailable(t, gs, c)).OrderBy(t => t.Tech.Cost).Take(4).ToList();
		if (later.Count > 0)
			arms.AddChild(Text("Still to come: " + string.Join("; ", later.Select(t => $"{t.Name} (with {t.Tech.Name})")) + "... See the research window."));

		// the armies
		var field = Section("Armies in the field");
		var armies = gs.ArmiesOf(c.Tag).ToList();
		if (armies.Count == 0)
			field.AddChild(Text("None. Raise the levies to form an army."));
		foreach (Army a in armies)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			field.AddChild(row);
			var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			row.AddChild(info);
			info.AddChild(HudStyle.Body($"{a.Name}: {a.Regiments.Count} regiments, {a.Men:N0} men", BodyFont, 14));
			string where = a.Moving
				? $"Marching to {gs.GetProvince(a.Destination)?.Name}, {gs.DaysToArrive(a)} days"
				: $"In {gs.GetProvince(a.ProvinceId)?.Name}";
			info.AddChild(HudStyle.Body($"{where} · {GameState.Describe(a)} · {MilitaryRules.Upkeep(a):0.0} gold a month", BodyFont, 12, HudStyle.Muted));
			var select = HudStyle.Button("Select", BodyFont, 13);
			int armyId = a.Id;
			select.Pressed += () =>
			{
				gs.SelectArmy(armyId);
				MapCamera.FocusProvince(GetNodeOrNull<Node3D>(CameraRigPath ?? new NodePath()), gs.GetArmy(armyId)?.ProvinceId ?? 0);
				Visible = false;
			};
			row.AddChild(select);
			var disband = HudStyle.Button("Disband", BodyFont, 13);
			disband.TooltipText = "Send the survivors home to their fields";
			disband.Pressed += () => gs.DisbandArmy(armyId);
			row.AddChild(disband);
		}
		int garrisons = owned.Sum(p => p.Control?.Garrison.Count ?? 0);
		if (garrisons > 0)
			field.AddChild(Text($"Garrisons: {garrisons} regiments holding {owned.Count(p => p.Control?.Garrison.Count > 0)} provinces."));
	}

	/// <summary>An option's description, effects and requirements, for its tooltip.</summary>
	static string Describe(LawOption o)
	{
		var lines = new List<string> { o.Name, o.Description };
		foreach (var (key, value) in o.Modifiers)
		{
			if (!ModifierNames.TryGetValue(key, out var m))
				continue;
			string amount = key == "noncitizen_manpower" ? $"{value:P0} of theirs"
				: m.Percent ? $"{value:+0%;-0%}" : $"{value:+0;-0}";
			lines.Add($"  {m.Label}: {amount}");
		}
		foreach (var (key, value) in o.Effects)
		{
			lines.Add(key switch
			{
				"assimilation" => $"  Other peoples take our culture: {value:0.#} units a year in each core province",
				"conversion" => $"  Other faiths take our gods: {value:0.#} units a year in each core province",
				"levy_share" => $"  Levies: {value:P0} of those who can serve",
				"women_serve" => "  Women are levied as well as men",
				_ => $"  {key}: {value}",
			});
		}
		if (o.Requires != null)
			lines.Add($"  Requires: {o.RequiresText ?? "conditions we must meet"}");
		return string.Join("\n", lines);
	}

	static void Clear(Node box)
	{
		foreach (Node child in box.GetChildren())
		{
			box.RemoveChild(child);
			child.QueueFree();
		}
	}
}
