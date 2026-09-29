using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.UI;

/// <summary>
/// The selected province: an Overview, its Population by culture, religion and occupation, and its
/// Production in three slots (non-renewable, food, produced) where the government can build.
/// The City View button opens the province's city in 3D.
/// </summary>
public partial class ProvincePanel : PanelContainer
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }
	[Export] public NodePath CityViewPath { get; set; }

	CityView _cityView;
	Label _name;
	Label _subtitle;
	TabContainer _tabs;
	VBoxContainer _overview;
	VBoxContainer _population;
	VBoxContainer _production;
	Button _cityButton;
	TextureRect _ownerFlag;
	PanelContainer _ownerFlagFrame;
	VBoxContainer _filling;     // the tab being filled; sections are added to it
	int _provinceId = -1;

	public override void _Ready()
	{
		_cityView = GetNodeOrNull<CityView>(CityViewPath ?? new NodePath());
		AddThemeStyleboxOverride("panel", HudStyle.Panel());
		CustomMinimumSize = new Vector2(420, 0);
		MouseFilter = MouseFilterEnum.Stop;

		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 8);
		AddChild(root);

		var header = new HBoxContainer();
		header.AddThemeConstantOverride("separation", 10);
		root.AddChild(header);
		_ownerFlag = new TextureRect();
		_ownerFlagFrame = HudStyle.Framed(_ownerFlag);
		_ownerFlagFrame.CustomMinimumSize = new Vector2(54, 36);
		_ownerFlagFrame.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		header.AddChild(_ownerFlagFrame);
		var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		header.AddChild(titles);
		_name = HudStyle.Title("", TitleFont, 24);
		_name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		titles.AddChild(_name);
		_subtitle = HudStyle.Body("", BodyFont, 14, HudStyle.Muted);
		titles.AddChild(_subtitle);
		_cityButton = HudStyle.Button("City View", BodyFont);
		_cityButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_cityButton.TooltipText = "See the city in 3D: its buildings and its people";
		_cityButton.Pressed += () => _cityView?.Open(_provinceId);
		header.AddChild(_cityButton);
		var close = HudStyle.Button("✕", BodyFont);
		close.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		close.Pressed += () => GameState.Instance?.SelectProvince(-1);
		header.AddChild(close);

		_tabs = new TabContainer { CustomMinimumSize = new Vector2(0, 430) };
		if (BodyFont != null)
			_tabs.AddThemeFontOverride("font", BodyFont);
		_tabs.AddThemeFontSizeOverride("font_size", 15);
		_tabs.AddThemeColorOverride("font_selected_color", HudStyle.Gold);
		_tabs.AddThemeColorOverride("font_unselected_color", HudStyle.Muted);
		_tabs.AddThemeColorOverride("font_hovered_color", HudStyle.Text);
		_tabs.AddThemeStyleboxOverride("panel", new StyleBoxEmpty { ContentMarginTop = 8 });
		var tabBg = new StyleBoxFlat { BgColor = new Color(0.3f, 0.23f, 0.13f, 0.6f) };
		tabBg.SetCornerRadiusAll(4);
		tabBg.SetContentMarginAll(6);
		var tabSel = (StyleBoxFlat)tabBg.Duplicate();
		tabSel.BgColor = new Color(0.5f, 0.38f, 0.2f, 0.8f);
		_tabs.AddThemeStyleboxOverride("tab_unselected", tabBg);
		_tabs.AddThemeStyleboxOverride("tab_hovered", tabSel);
		_tabs.AddThemeStyleboxOverride("tab_selected", tabSel);
		root.AddChild(_tabs);
		_overview = AddTab("Overview");
		_population = AddTab("Population");
		_production = AddTab("Production");

		Visible = false;
		if (GameState.Instance == null)
			return;
		GameState.Instance.SelectedProvinceChanged += OnSelected;
		GameState.Instance.ProvinceChanged += OnProvinceChanged;
		GameState.Instance.MonthAdvanced += Refresh;
		GameState.Instance.PhaseChanged += Refresh;
		GameState.Instance.FlagsChanged += Refresh;
		OnSelected(GameState.Instance.SelectedProvinceId);
	}

	public override void _ExitTree()
	{
		if (GameState.Instance == null)
			return;
		GameState.Instance.SelectedProvinceChanged -= OnSelected;
		GameState.Instance.ProvinceChanged -= OnProvinceChanged;
		GameState.Instance.MonthAdvanced -= Refresh;
		GameState.Instance.PhaseChanged -= Refresh;
		GameState.Instance.FlagsChanged -= Refresh;
	}

	VBoxContainer AddTab(string title)
	{
		var scroll = new ScrollContainer { Name = title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		box.AddThemeConstantOverride("separation", 8);
		scroll.AddChild(box);
		_tabs.AddChild(scroll);
		return box;
	}

	void OnSelected(int id)
	{
		_provinceId = id;
		Refresh();
	}

	void OnProvinceChanged(int id)
	{
		if (id == _provinceId)
			Refresh();
		if (_cityView != null && _cityView.Visible && id == _provinceId)
			_cityView.Rebuild();
	}

	void Refresh()
	{
		Province p = GameState.Instance?.GetProvince(_provinceId);
		// the country selection screen has its own panel
		Visible = p != null && GameState.Instance.Phase == GamePhase.Playing;
		if (!Visible)
			return;

		_name.Text = p.Name;
		Country ownerCountry = GameState.Instance.GetCountry(p.OwnerTag);
		string owner = ownerCountry?.Name ?? "Uncontrolled";
		_ownerFlagFrame.Visible = ownerCountry != null;
		_ownerFlag.Texture = HudStyle.Texture(ownerCountry?.CurrentFlag?.ImagePath);
		_ownerFlag.TooltipText = ownerCountry?.CurrentFlag?.Name;
		_subtitle.Text = p.IsWater ? (p.IsLake ? "Lake" : "Sea") : $"{owner} · {Capitalize(p.Terrain)}";
		_tabs.SetTabHidden(1, p.IsWater);
		_tabs.SetTabHidden(2, p.IsWater);
		_cityButton.Disabled = p.IsWater || p.Pops.Count == 0 && p.Buildings.Count == 0;

		FillOverview(p);
		if (!p.IsWater)
		{
			FillPopulation(p);
			FillProduction(p);
		}
	}

	// ---------------------------------------------------------------------------------- Overview

	void FillOverview(Province p)
	{
		Clear(_overview);
		_filling = _overview;
		if (!p.IsWater)
			FillControl(p);
		if (!p.IsWater)
		{
			var grid = Grid(2);
			_overview.AddChild(grid);
			Row(grid, "Population", p.TotalUnits > 0 ? $"{p.TotalUnits * PopGroup.PeoplePerUnit:N0}  ({p.TotalUnits} units)" : "Uninhabited");
			Culture main = p.MainCulture;
			if (main != null)
			{
				Row(grid, "Main culture", main.Name);
				Row(grid, "Language", $"{main.Language.Name} ({main.Language.Family.Name})");
			}
			var religions = p.Pops.Select(g => g.Religion.Name).Distinct().ToList();
			if (religions.Count > 0)
				Row(grid, "Religion", string.Join(", ", religions));
			Row(grid, "Terrain", Capitalize(p.Terrain));
			if (p.Features.Count > 0)
				Row(grid, "Features", string.Join(", ", p.Features.Select(Capitalize)));
			Row(grid, "Food", p.Food?.Name ?? "None");
			Row(grid, "Deposit", p.NonRenewable?.Name ?? "None");
			Row(grid, "Pasture", $"{PopulationRules.PastureQuality(p):F2}");
		}

		var crossings = new SortedSet<string>();
		var navigable = new SortedSet<string>();
		foreach (Adjacency link in p.Neighbors)
		{
			if (link.IsRiverCrossing)
				crossings.Add(link.CrossingRiver);
			if (link.IsNavigableRiver)
				navigable.Add(link.NavigableRiver);
		}
		if (crossings.Count > 0 || navigable.Count > 0)
		{
			var grid = Grid(2);
			_overview.AddChild(grid);
			if (crossings.Count > 0)
				Row(grid, "River borders", string.Join(", ", crossings));
			if (navigable.Count > 0)
				Row(grid, "Navigable", string.Join(", ", navigable));
		}
		_overview.AddChild(HudStyle.Body($"Province #{p.Id}", BodyFont, 12, HudStyle.Muted));
	}

	// ---------------------------------------------------------------------------------- Control

	/// <summary>Who holds the province and how; for uncontrolled land, what the player can do to take it.</summary>
	void FillControl(Province p)
	{
		GameState gs = GameState.Instance;
		Country player = gs.PlayerCountry;
		var box = Section("Control");
		Label Line(string text, Color? color = null, int size = 14)
		{
			var l = HudStyle.Body(text, BodyFont, size, color);
			l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			l.CustomMinimumSize = new Vector2(360, 0);
			box.AddChild(l);
			return l;
		}

		if (p.Control != null)
		{
			Country owner = gs.GetCountry(p.OwnerTag);
			string how = p.Control.Kind switch
			{
				ControlKind.Core => "Core province",
				ControlKind.Vassal => "Vassal tribes",
				_ => "Subjugated nomads",
			};
			Line($"{how} of {owner?.Name}", HudStyle.Text, 15);
			if (!p.IsCore)
			{
				double separatism = ControlRules.Separatism(p, gs.Date);
				Line($"Separatism {separatism:P0}: a core in {ControlRules.YearsUntilCore(p, gs.Date)} years, if held in rein", HudStyle.Muted, 13);
				int warriors = ControlRules.Warriors(p);
				Line($"Garrison {p.Control.Garrison} regiments · {warriors} regiments of possible rebels · uprising risk {ControlRules.UprisingChance(p, owner, gs.Date):P1} a month",
					ControlRules.UprisingChance(p, owner, gs.Date) > 0.01 ? HudStyle.Bad : HudStyle.Muted, 13);
				if (owner != null && owner == player)
					box.AddChild(GarrisonRow(p, player));
			}
			return;
		}

		string who = p.Inhabitants switch
		{
			Inhabitants.Tribes => "Uncontrolled: settled tribes live here",
			Inhabitants.Nomads => "Uncontrolled: nomads roam here",
			_ => "Uncontrolled and empty",
		};
		Line(who, HudStyle.Text, 15);
		if (gs.GetCountry(p.AlliedTag) is Country ally)
			Line($"Allied with {ally.Name} since {gs.Definitions.DefaultCalendar.Format(p.AlliedSince)}", HudStyle.Muted, 13);
		if (player == null || gs.Phase != GamePhase.Playing)
			return;

		if (p.Inhabitants == Inhabitants.Tribes)
		{
			if (p.AlliedTag == player.Tag)
			{
				bool can = ControlRules.CanVassalize(player, p, gs.Date, out string why);
				box.AddChild(ActionRow("Make them vassals", can, why ?? "The province comes under our control, with separatism for 50 years",
					() => gs.VassalizeTribes(p.Id)));
			}
			else
			{
				bool can = ControlRules.CanAlly(player, p, gs.Date, out string why);
				string tip = can ? $"{ControlRules.AllianceChance(player, p):P0} chance they accept. After {ControlRules.YearsAlliedToVassalize} years of alliance they can become vassals." : why;
				box.AddChild(ActionRow("Ally with the tribes", can, tip, () => gs.AllyTribes(p.Id)));
				if (can)
					Line($"Chance they accept: {ControlRules.AllianceChance(player, p):P0} (kinship with {player.Adjective} people helps)", HudStyle.Muted, 13);
			}
		}
		else if (p.Inhabitants == Inhabitants.Nomads)
		{
			int warriors = ControlRules.Warriors(p);
			Line($"About {warriors} regiments of nomad warriors · we have {player.Manpower} regiments free", HudStyle.Muted, 13);
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			var count = new SpinBox { MinValue = 1, MaxValue = Math.Max(1, player.Manpower), Value = Math.Clamp(warriors * 2, 1, Math.Max(1, player.Manpower)), Suffix = "reg." };
			count.GetLineEdit().AddThemeFontSizeOverride("font_size", 13);
			row.AddChild(count);
			var odds = HudStyle.Body("", BodyFont, 13, HudStyle.Muted);
			odds.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			var go = HudStyle.Button("Subjugate", BodyFont, 13);
			void UpdateOdds()
			{
				int n = (int)count.Value;
				bool can = ControlRules.CanSubjugate(player, p, n, out string why);
				go.Disabled = !can;
				go.TooltipText = can ? "Send the army. If it wins, the survivors stay as the garrison." : why;
				odds.Text = can ? $"{ControlRules.SubjugationChance(player, p, n):P0} to win" : why;
			}
			count.ValueChanged += _ => UpdateOdds();
			go.Pressed += () => gs.SubjugateNomads(p.Id, (int)count.Value);
			UpdateOdds();
			row.AddChild(odds);
			row.AddChild(go);
			box.AddChild(row);
		}
	}

	Control ActionRow(string text, bool enabled, string tooltip, Action action)
	{
		var b = HudStyle.Button(text, BodyFont, 13);
		b.Disabled = !enabled;
		b.TooltipText = tooltip;
		b.Pressed += action;
		b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		if (!enabled && tooltip != null)
		{
			var box = new VBoxContainer();
			box.AddChild(b);
			var why = HudStyle.Body(tooltip, BodyFont, 12, HudStyle.Muted);
			why.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			why.CustomMinimumSize = new Vector2(360, 0);
			box.AddChild(why);
			return box;
		}
		return b;
	}

	Control GarrisonRow(Province p, Country player)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		row.AddChild(HudStyle.Body("Garrison", BodyFont, 13, HudStyle.Muted));
		var minus = HudStyle.Button("−", BodyFont, 13);
		minus.Disabled = p.Control.Garrison <= 0;
		minus.TooltipText = "Send a regiment home";
		minus.Pressed += () => GameState.Instance.SetGarrison(p.Id, p.Control.Garrison - 1);
		var plus = HudStyle.Button("+", BodyFont, 13);
		plus.Disabled = player.Manpower <= 0;
		plus.TooltipText = player.Manpower > 0 ? "Station another regiment" : "No regiments to spare";
		plus.Pressed += () => GameState.Instance.SetGarrison(p.Id, p.Control.Garrison + 1);
		row.AddChild(minus);
		row.AddChild(HudStyle.Body($"{p.Control.Garrison}", BodyFont, 14));
		row.AddChild(plus);
		row.AddChild(HudStyle.Body($"({player.Manpower} free)", BodyFont, 13, HudStyle.Muted));
		return row;
	}

	// -------------------------------------------------------------------------------- Population

	void FillPopulation(Province p)
	{
		Clear(_population);
		_filling = _population;
		if (p.Pops.Count == 0)
		{
			_population.AddChild(HudStyle.Body("Nobody lives here.", BodyFont, 15, HudStyle.Muted));
			return;
		}

		var grid = Grid(4);
		grid.AddThemeConstantOverride("h_separation", 14);
		_population.AddChild(grid);
		foreach (string h in new[] { "Culture", "Religion", "Occupation", "People" })
			grid.AddChild(HudStyle.Body(h, BodyFont, 13, HudStyle.Muted));
		foreach (PopGroup pop in p.Pops.OrderByDescending(g => g.Units))
		{
			var culture = new HBoxContainer();
			culture.AddChild(HudStyle.Swatch(pop.Culture.Color));
			culture.AddChild(HudStyle.Body(pop.Culture.Name, BodyFont));
			grid.AddChild(culture);
			grid.AddChild(HudStyle.Body(pop.Religion.Name, BodyFont));
			var occupation = HudStyle.Body(pop.Occupation.Name + (pop.Occupation.Nomadic ? " (nomadic)" : ""), BodyFont);
			occupation.TooltipText = pop.Occupation.Nomadic
				? "Nomads move freely between provinces toward better pasture."
				: "Settled farmers who work the land and staff the buildings.";
			occupation.MouseFilter = MouseFilterEnum.Pass;
			grid.AddChild(occupation);
			var people = HudStyle.Body($"{pop.People:N0}", BodyFont);
			people.HorizontalAlignment = HorizontalAlignment.Right;
			grid.AddChild(people);
		}

		// share by occupation as a bar
		int total = p.TotalUnits;
		var bar = new HBoxContainer { CustomMinimumSize = new Vector2(0, 10) };
		bar.AddThemeConstantOverride("separation", 0);
		foreach (var group in p.Pops.GroupBy(g => g.Occupation))
		{
			int units = group.Sum(g => g.Units);
			bar.AddChild(new ColorRect
			{
				Color = group.Key.Color,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				SizeFlagsStretchRatio = Math.Max(units, 1) / (float)total,
				TooltipText = $"{group.Key.Name}: {units * 100 / total}%",
			});
		}
		_population.AddChild(bar);

		// kinship: how the province's cultures relate to its main culture through language
		Culture main = p.MainCulture;
		var section = Section("Kinship");
		foreach (Culture c in p.Pops.Select(g => g.Culture).Distinct())
		{
			var row = new HBoxContainer();
			row.AddChild(HudStyle.Swatch(c.Color));
			row.AddChild(HudStyle.Body($"{c.Name}: speaks {c.Language.Name} ({c.Language.Family.Name})", BodyFont, 14));
			section.AddChild(row);
			if (c != main)
				section.AddChild(HudStyle.Body($"   {PopulationRules.AffinityName(main, c)} as the {main.Name} majority", BodyFont, 13, HudStyle.Muted));
		}
		section.AddChild(HudStyle.Body("Peoples of one language, and less so of one language family, feel they have something in common.", BodyFont, 12, HudStyle.Muted));
		((Label)section.GetChild(section.GetChildCount() - 1)).AutowrapMode = TextServer.AutowrapMode.WordSmart;
	}

	// -------------------------------------------------------------------------------- Production

	void FillProduction(Province p)
	{
		Clear(_production);
		_filling = _production;
		ProductionReport report = GameState.Instance.GetProduction(p.Id);
		Definitions defs = GameState.Instance.Definitions;

		var nr = Section("Non-renewable");
		if (p.NonRenewable == null)
			nr.AddChild(HudStyle.Body("No deposits", BodyFont, 14, HudStyle.Muted));
		else
		{
			var row = ResourceRow(p.NonRenewable, report.Extracted > 0 ? $"extracting {report.Extracted:0.#}/month" : "untapped");
			nr.AddChild(row);
		}

		var food = Section("Food");
		if (p.Food == null)
			food.AddChild(HudStyle.Body("Nothing grows here", BodyFont, 14, HudStyle.Muted));
		else
		{
			food.AddChild(ResourceRow(p.Food, $"{report.FoodOutput:0.#} food/month"));
			string workers = string.Join(" and ", p.Food.WorkedBy.Select(o => o.Name.ToLowerInvariant()));
			food.AddChild(HudStyle.Body($"{report.FoodWorkers} units of {workers} work the land ({p.Food.FoodYield:0.##} each)", BodyFont, 13, HudStyle.Muted));
		}

		var produced = Section("Produced");
		if (report.Buildings.Count == 0)
			produced.AddChild(HudStyle.Body("No production buildings yet", BodyFont, 14, HudStyle.Muted));
		foreach (BuildingOutput output in report.Buildings)
			produced.AddChild(BuildingRow(p, output));

		var net = report.Net.Where(kv => MathF.Abs(kv.Value) > 0.001f && kv.Key.Category == ResourceCategory.Produced).ToList();
		if (net.Count > 0)
		{
			var totals = new HFlowContainer();
			totals.AddChild(HudStyle.Body("Net:", BodyFont, 13, HudStyle.Muted));
			foreach (var (r, v) in net)
			{
				totals.AddChild(HudStyle.Swatch(r.Color, 10));
				totals.AddChild(HudStyle.Body($"{r.Name} {v:+0.#;-0.#}", BodyFont, 13, v >= 0 ? HudStyle.Good : HudStyle.Bad));
			}
			produced.AddChild(totals);
		}

		// what could be built here
		var options = defs.Buildings.Where(t => p.GetBuilding(t) == null && BuildingRules.MeetsRequirements(p, t, out _)).ToList();
		if (options.Count > 0)
		{
			var build = Section("Can be built here");
			foreach (BuildingType t in options)
			{
				var row = new HBoxContainer();
				var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				text.AddChild(HudStyle.Body(t.Name, BodyFont, 14));
				text.AddChild(HudStyle.Body(DescribeRecipe(t, p, 1), BodyFont, 12, HudStyle.Muted));
				if (t.PopulationCanBuild)
					text.AddChild(HudStyle.Body($"The people build it themselves with {t.PopulationBuildMinUnits} units of {t.PopulationBuildOccupation.Name.ToLowerInvariant()}", BodyFont, 12, HudStyle.Muted));
				row.AddChild(text);
				row.AddChild(BuildButton(p, t, "Build"));
				build.AddChild(row);
			}
		}
	}

	Control BuildingRow(Province p, BuildingOutput output)
	{
		Building b = output.Building;
		var row = new HBoxContainer();
		var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		row.AddChild(text);
		string by = b.BuiltByPopulation ? " · built by the people" : "";
		text.AddChild(HudStyle.Body($"{b.Type.Name}  (level {b.Level}){by}", BodyFont, 14));

		var flow = new HFlowContainer();
		foreach (var (r, v) in output.Produced)
		{
			flow.AddChild(HudStyle.Swatch(r.Color, 10));
			flow.AddChild(HudStyle.Body($"+{v:0.#} {r.Name}", BodyFont, 13, HudStyle.Good));
		}
		foreach (var (r, v) in output.Consumed)
			flow.AddChild(HudStyle.Body($"−{v:0.#} {r.Name}", BodyFont, 13, HudStyle.Bad));
		int jobs = b.Level * b.Type.JobUnits;
		flow.AddChild(HudStyle.Body($"· employs {jobs} unit{(jobs == 1 ? "" : "s")} of {b.Type.JobOccupation.Name.ToLowerInvariant()}", BodyFont, 13, HudStyle.Muted));
		text.AddChild(flow);
		if (output.Shortfall != null)
			text.AddChild(HudStyle.Body(output.Shortfall, BodyFont, 12, HudStyle.Bad));
		row.AddChild(BuildButton(p, b.Type, "Expand"));
		return row;
	}

	Button BuildButton(Province p, BuildingType t, string text)
	{
		var button = HudStyle.Button(text, BodyFont, 13);
		button.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		GameState gs = GameState.Instance;
		string reason = p.OwnerTag == null ? "Nobody governs this province"
			: $"Only the government of {gs.GetCountry(p.OwnerTag)?.Name} can build here";
		bool can = p.OwnerTag != null && p.OwnerTag == gs.PlayerTag && BuildingRules.CanBuild(p, t, Builder.Government, out reason);
		button.Disabled = !can;
		button.TooltipText = can ? $"The government builds a {t.Name.ToLowerInvariant()} level" : reason;
		int id = p.Id;
		string type = t.Id;
		button.Pressed += () => GameState.Instance.GovernmentBuild(id, type, out _);
		return button;
	}

	string DescribeRecipe(BuildingType t, Province p, int level)
	{
		var parts = new List<string>();
		foreach (var (id, v) in t.Produces)
		{
			string name = id == BuildingType.LocalDeposit ? p.NonRenewable?.Name ?? "ore" : GameState.Instance.Definitions.Resources[id].Name;
			parts.Add($"+{v * level:0.#} {name}");
		}
		foreach (var (id, v) in t.Consumes)
			parts.Add($"−{v * level:0.#} {GameState.Instance.Definitions.Resources[id].Name}");
		return string.Join("  ", parts) + $"  per level, employing {t.JobUnits} unit{(t.JobUnits > 1 ? "s" : "")} of {t.JobOccupation.Name.ToLowerInvariant()}";
	}

	// ------------------------------------------------------------------------------------ helpers

	VBoxContainer Section(string title)
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", HudStyle.Section());
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 4);
		panel.AddChild(box);
		box.AddChild(HudStyle.Title(title, TitleFont, 16));
		_filling.AddChild(panel);
		return box;
	}

	Control ResourceRow(ResourceType r, string detail)
	{
		var row = new HBoxContainer();
		row.AddChild(HudStyle.Swatch(r.Color, 14));
		row.AddChild(HudStyle.Body(r.Name, BodyFont, 15));
		var d = HudStyle.Body(detail, BodyFont, 13, HudStyle.Muted);
		d.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		d.HorizontalAlignment = HorizontalAlignment.Right;
		row.AddChild(d);
		return row;
	}

	GridContainer Grid(int columns)
	{
		var g = new GridContainer { Columns = columns };
		g.AddThemeConstantOverride("h_separation", 16);
		g.AddThemeConstantOverride("v_separation", 3);
		return g;
	}

	void Row(GridContainer grid, string key, string value)
	{
		grid.AddChild(HudStyle.Body(key, BodyFont, 14, HudStyle.Muted));
		var v = HudStyle.Body(value, BodyFont, 14);
		v.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		v.CustomMinimumSize = new Vector2(250, 0);
		grid.AddChild(v);
	}

	static void Clear(Node box)
	{
		foreach (Node child in box.GetChildren())
		{
			box.RemoveChild(child);
			child.QueueFree();
		}
	}

	static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
