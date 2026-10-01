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
		GameState.Instance.TribesChanged += Refresh;
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
		GameState.Instance.TribesChanged -= Refresh;
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
		_subtitle.Text = p.IsWater ? (p.IsLake ? "Lake" : "Sea") : $"{owner} · {TerrainRules.Describe(p)}";
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
			Row(grid, "Terrain", TerrainRules.Describe(p));
			Row(grid, "On the land", TerrainEffects(p));
			if (p.Features.Count > 0)
				Row(grid, "Features", string.Join(", ", p.Features.Select(Capitalize)));
			Row(grid, "Food", p.Food?.Name ?? "None");
			Row(grid, "Deposit", p.NonRenewable?.Name ?? "None");
			if (p.Control != null)
			{
				int nomads = p.Pops.Where(g => g.Occupation.Nomadic).Sum(g => g.Units);
				string note = nomads > 0 ? $" (its {nomads * PopGroup.PeoplePerUnit:N0} tribesmen pay none)" : "";
				double separatism = ControlRules.Separatism(p, GameState.Instance.Date, GameState.Instance.GetCountry(p.OwnerTag));
				if (separatism > 0)
					note += $", {separatism:P0} lost to separatism";
				Row(grid, "Tax", $"{EconomyRules.Tax(p, GameState.Instance.Date, GameState.Instance.GetCountry(p.OwnerTag)):0.0} gold a month{note}");
			}
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

	/// <summary>What the terrain does to armies: march, defense, horses, supply, attrition.</summary>
	static string TerrainEffects(Province p)
	{
		if (!p.IsPassable)
			return $"{p.Relief?.Description} Armies can't enter it.";
		var parts = new List<string> { $"marching x{TerrainRules.MoveFactor(p):0.##}" };
		int dice = TerrainRules.DefenseDice(p);
		if (dice > 0)
			parts.Add($"defenders +{dice} dice");
		if (p.Biome?.Native > 0)
			parts.Add($"its people +{p.Biome.Native} on home ground");
		double horses = TerrainRules.CategoryFactor(UnitCategory.Mounted, p);
		if (Math.Abs(horses - 1) > 0.01)
			parts.Add($"mounted x{horses:0.##}");
		double siege = TerrainRules.CategoryFactor(UnitCategory.Siege, p);
		if (Math.Abs(siege - 1) > 0.01)
			parts.Add($"siege x{siege:0.##}");
		parts.Add($"feeds {TerrainRules.Supply(p):0} regiments");
		double attrition = TerrainRules.Attrition(p, 1);
		if (attrition > 0)
			parts.Add($"armies lose {attrition:P1} of their men a month");
		return string.Join(" · ", parts);
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
				ControlKind.Absorbed => "Absorbed tribes",
				_ => "Subjugated nomads",
			};
			Line($"{how} of {owner?.Name}", HudStyle.Text, 15);
			if (!p.IsCore)
			{
				double separatism = ControlRules.Separatism(p, gs.Date, owner);
				Line($"Separatism {separatism:P0}: a core in {ControlRules.YearsUntilCore(p, gs.Date, owner)} years, if held in rein", HudStyle.Muted, 13);
				int warriors = ControlRules.Warriors(p);
				Line($"Garrison {p.Control.Garrison.Count} regiments · {warriors} regiments of possible rebels · uprising risk {ControlRules.UprisingChance(p, owner, gs.Date):P1} a month",
					ControlRules.UprisingChance(p, owner, gs.Date) > 0.01 ? HudStyle.Bad : HudStyle.Muted, 13);
				if (owner != null && owner == player)
					box.AddChild(GarrisonRow(p, player));
			}
			return;
		}

		Tribe tribe = gs.TribeOf(p);
		if (tribe == null)
		{
			Line(p.Inhabitants == Inhabitants.Empty ? "Uncontrolled and empty: land for settlers, one day" : "Uncontrolled", HudStyle.Text, 15);
			return;
		}
		Line($"Uncontrolled: the land of {tribe.TheName}", HudStyle.Text, 15);
		FillTribe(tribe, player);
	}

	/// <summary>An unsettled country: who they are, how they feel about us, and what we can do with them.</summary>
	void FillTribe(Tribe t, Country player)
	{
		GameState gs = GameState.Instance;
		var provinces = gs.Provinces;
		var box = Section(t.Name);
		Label Line(string text, Color? color = null, int size = 13)
		{
			var l = HudStyle.Body(text, BodyFont, size, color ?? HudStyle.Muted);
			l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			l.CustomMinimumSize = new Vector2(250, 0);
			box.AddChild(l);
			return l;
		}

		// who they are: chief's portrait beside the facts
		var head = new HBoxContainer();
		head.AddThemeConstantOverride("separation", 10);
		box.AddChild(head);
		var portrait = new TextureRect { Texture = HudStyle.Texture(t.Chief?.Portrait) };
		var frame = HudStyle.Framed(portrait);
		frame.CustomMinimumSize = new Vector2(72, 90);
		frame.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		head.AddChild(frame);
		var facts = new VBoxContainer();
		head.AddChild(facts);
		void Fact(string text, Color? color = null) => facts.AddChild(HudStyle.Body(text, BodyFont, 13, color ?? HudStyle.Text));
		Fact($"{(t.IsNomadic ? "Nomadic horde" : "Settled tribes")} · {t.Culture?.Name} · {t.Religion?.Name}");
		Fact($"Chief {t.Chief?.Name}, age {t.Chief?.AgeOn(gs.Date)}", HudStyle.Muted);
		Fact($"{t.Provinces.Count} provinces · {TribeRules.People(t, provinces) * PopGroup.PeoplePerUnit:N0} people", HudStyle.Muted);
		Fact($"{TribeRules.Warriors(t, provinces)} regiments of fierce warriors" + (t.HiredRegiments > 0 ? $" ({t.HiredRegiments} away as mercenaries)" : ""), HudStyle.Muted);
		Fact($"Camp: {gs.GetProvince(t.CampProvinceId)?.Name}", HudStyle.Muted);
		FoodLedger food = EconomyRules.MonthlyFood(t, provinces, gs.Definitions);
		var stores = HudStyle.Body($"Food stores: {t.Food:0} ({food.Balance:+0.0;-0.0} a month)", BodyFont, 13,
			EconomyRules.Starving(t) ? HudStyle.Bad : HudStyle.Text);
		stores.TooltipText = $"Their treasury: food they barter with others and keep their war bands with.\n"
			+ $"+{food.Surplus:0.0} surplus of their herds and fields\n-{food.WarBands:0.0} war bands at home"
			+ (food.Barter > 0 ? $"\n+{food.Barter:0.0} bartered for their mercenaries' pay" : "")
			+ (EconomyRules.Starving(t) ? "\nStarving: only half their warriors turn out" : "");
		stores.MouseFilter = MouseFilterEnum.Pass;
		facts.AddChild(stores);
		if (gs.GetCountry(t.AlliedTag) is Country ally)
			Fact($"Allied with {ally.Name} since {gs.Definitions.DefaultCalendar.Format(t.AlliedSince)}", HudStyle.Good);

		if (player == null || gs.Phase != GamePhase.Playing)
			return;

		// relations, and our diplomats
		int relation = t.RelationWith(player.Tag);
		var relRow = new HBoxContainer();
		relRow.AddThemeConstantOverride("separation", 8);
		box.AddChild(relRow);
		var relLabel = HudStyle.Body($"Relations with us: {relation:+0;-0;0}", BodyFont, 14, relation >= 0 ? HudStyle.Good : HudStyle.Bad);
		relLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		relRow.AddChild(relLabel);
		bool courting = player.ImprovingRelations.Contains(t.Id);
		var court = HudStyle.Button(courting ? "Recall diplomat" : "Improve relations", BodyFont, 13);
		court.Disabled = !courting && player.ImprovingRelations.Count >= TribeRules.Diplomats;
		court.TooltipText = courting
			? $"Our diplomat raises relations by {TribeRules.ImproveRelationsPerMonth} a month"
			: court.Disabled ? $"All our {TribeRules.Diplomats} diplomats are busy" : $"Send a diplomat: +{TribeRules.ImproveRelationsPerMonth} relations a month ({TribeRules.Diplomats - player.ImprovingRelations.Count} free)";
		court.Pressed += () => gs.SetImprovingRelations(t.Id, !courting);
		relRow.AddChild(court);
		var gifts = HudStyle.Button($"Gifts ({EconomyRules.GiftGold:0} gold)", BodyFont, 13);
		gifts.Disabled = player.Gold < EconomyRules.GiftGold;
		gifts.TooltipText = gifts.Disabled ? "We can't afford it"
			: $"Gold they barter for {EconomyRules.GiftGold * EconomyRules.FoodPerGold:0} food: +{EconomyRules.GiftRelations} relations";
		gifts.Pressed += () => gs.SendGifts(t.Id);
		relRow.AddChild(gifts);

		if (t.AlliedTag != player.Tag)
		{
			bool can = TribeRules.CanProposeAlliance(t, player, provinces, gs.Date, out string why);
			string tip = can ? $"{TribeRules.AllianceChance(t, player):P0} chance they accept" : why;
			box.AddChild(ActionRow($"Propose alliance{(can ? $" ({TribeRules.AllianceChance(t, player):P0})" : "")}", can, tip, () => gs.ProposeAlliance(t.Id)));
		}
		else
		{
			// mercenaries
			int hireable = TribeRules.Hireable(t, provinces);
			var hireRow = new HBoxContainer();
			hireRow.AddThemeConstantOverride("separation", 8);
			hireRow.AddChild(HudStyle.Body("Mercenaries", BodyFont, 13, HudStyle.Muted));
			var count = new SpinBox { MinValue = 1, MaxValue = Math.Max(1, hireable), Value = Math.Max(1, hireable), Suffix = "reg." };
			hireRow.AddChild(count);
			var hire = HudStyle.Button("Hire", BodyFont, 13);
			hire.Disabled = hireable < 1;
			hire.TooltipText = hireable < 1 ? "They will lend no more warriors"
				: $"They fight {ControlRules.FierceMultiplier}x as hard as our regiments; up to {hireable} more.\nPay: {EconomyRules.MercenaryPayOf(player):0.##} gold a month each, which they barter for food.";
			hire.Pressed += () => gs.HireMercenaries(t.Id, (int)count.Value);
			hireRow.AddChild(hire);
			if (t.HiredRegiments > 0)
			{
				var dismiss = HudStyle.Button($"Dismiss {t.HiredRegiments}", BodyFont, 13);
				dismiss.Pressed += () => gs.DismissMercenaries(t.Id);
				hireRow.AddChild(dismiss);
			}
			box.AddChild(hireRow);

			// absorption
			var conditions = TribeRules.AbsorbConditions(t, player, provinces, gs.Armies.Values, gs.Date);
			bool canAbsorb = conditions.TrueForAll(c => c.Met);
			box.AddChild(ActionRow("Absorb them", canAbsorb, canAbsorb ? "Their lands come under our rule, with separatism for 50 years" : null, () => gs.AbsorbTribe(t.Id)));
			foreach (var (condition, met) in conditions)
				Line($"{(met ? "✔" : "✘")} {condition}", met ? HudStyle.Good : HudStyle.Bad, 12);
		}

		if (t.IsNomadic && t.AlliedTag != player.Tag)
		{
			int mercs = TribeRules.Mercenaries(gs.Tribes.Values, player.Tag);
			Army army = TribeRules.ArmiesIn(t, player, gs.Armies.Values).FirstOrDefault();
			Line($"Subjugate by force: they have {TribeRules.Warriors(t, provinces)} fierce regiments. "
				+ (army != null ? $"The {army.Name} ({army.Regiments.Count} regiments) stands in their lands" : "March an army into their lands to give them battle")
				+ (mercs > 0 ? $"; {mercs} mercenary regiments can go with it." : "."), HudStyle.Muted);
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);
			var hired = new SpinBox { MinValue = 0, MaxValue = mercs, Value = mercs, Suffix = "merc.", Editable = mercs > 0 };
			var odds = HudStyle.Body("", BodyFont, 12, HudStyle.Muted);
			odds.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			var go = HudStyle.Button("Give battle", BodyFont, 13);
			void UpdateOdds()
			{
				int m = (int)hired.Value;
				bool can = TribeRules.CanSubjugate(t, player, army, gs.Tribes.Values, provinces, m, out string why);
				go.Disabled = !can;
				go.TooltipText = can ? "Break the horde: all their lands become ours, a regiment of the army left in each as its garrison" : why;
				odds.Text = can ? $"{TribeRules.SubjugationChance(t, player, army, provinces, m):P0} to win" : why;
			}
			hired.ValueChanged += _ => UpdateOdds();
			go.Pressed += () => gs.SubjugateTribe(t.Id, army?.Id ?? 0, (int)hired.Value);
			UpdateOdds();
			if (mercs > 0)
				row.AddChild(hired);
			row.AddChild(go);
			box.AddChild(row);
			box.AddChild(odds);
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

	/// <summary>The garrison: regiments of our army here stay behind in it, or leave it to join the army.</summary>
	Control GarrisonRow(Province p, Country player)
	{
		GameState gs = GameState.Instance;
		Army army = gs.ArmiesIn(p.Id).FirstOrDefault(a => a.OwnerTag == player.Tag && a.Regiments.Count > 0);
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		row.AddChild(HudStyle.Body("Garrison", BodyFont, 13, HudStyle.Muted));
		var minus = HudStyle.Button("−", BodyFont, 13);
		minus.Disabled = p.Control.Garrison.Count == 0;
		minus.TooltipText = "A regiment leaves the garrison and joins our army here";
		minus.Pressed += () => gs.ArmyFromGarrison(p.Id);
		var plus = HudStyle.Button("+", BodyFont, 13);
		plus.Disabled = army == null;
		plus.TooltipText = army != null ? $"A regiment of the {army.Name} stays behind in the garrison" : "Bring an army here to leave regiments in the garrison";
		plus.Pressed += () => gs.GarrisonFromArmy(p.Id);
		row.AddChild(minus);
		row.AddChild(HudStyle.Body($"{p.Control.Garrison.Count}", BodyFont, 14));
		row.AddChild(plus);
		row.AddChild(HudStyle.Body(army != null ? $"(the {army.Name}: {army.Regiments.Count} here)" : "(no army here)", BodyFont, 13, HudStyle.Muted));
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

		int men = p.UnitsOf(Sex.Male), women = p.UnitsOf(Sex.Female);
		_population.AddChild(HudStyle.Body($"{men * PopGroup.PeoplePerUnit:N0} men and {women * PopGroup.PeoplePerUnit:N0} women", BodyFont, 14, HudStyle.Muted));
		var grid = Grid(5);
		grid.AddThemeConstantOverride("h_separation", 14);
		_population.AddChild(grid);
		foreach (string h in new[] { "Culture", "Religion", "Occupation", "Men", "Women" })
			grid.AddChild(HudStyle.Body(h, BodyFont, 13, HudStyle.Muted));
		// men and women of one people side by side
		foreach (var kind in p.Pops.GroupBy(g => (g.Culture, g.Religion, g.Occupation)).OrderByDescending(k => k.Sum(g => g.Units)))
		{
			PopGroup pop = kind.First();
			var culture = new HBoxContainer();
			culture.AddChild(HudStyle.Swatch(pop.Culture.Color));
			culture.AddChild(HudStyle.Body(pop.Culture.Name, BodyFont));
			grid.AddChild(culture);
			grid.AddChild(HudStyle.Body(pop.Religion.Name, BodyFont));
			var occupation = HudStyle.Body(pop.Occupation.Name + (pop.Occupation.Nomadic ? " (nomadic)" : ""), BodyFont);
			occupation.TooltipText = pop.Occupation.Description is { Length: > 0 } d ? d
				: pop.Occupation.Nomadic ? "Nomads move freely between provinces toward better pasture."
				: "Settled farmers who work the land and staff the buildings.";
			occupation.MouseFilter = MouseFilterEnum.Pass;
			grid.AddChild(occupation);
			foreach (Sex sex in new[] { Sex.Male, Sex.Female })
			{
				var people = HudStyle.Body($"{kind.Where(g => g.Sex == sex).Sum(g => g.People):N0}", BodyFont);
				people.HorizontalAlignment = HorizontalAlignment.Right;
				grid.AddChild(people);
			}
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
		double cost = EconomyRules.BuildCost(p, t, gs.GetCountry(p.OwnerTag));
		button.Text = $"{text} ({cost:0} gold)";
		bool can = p.OwnerTag != null && p.OwnerTag == gs.PlayerTag && BuildingRules.CanBuild(p, t, Builder.Government, out reason);
		if (can && gs.PlayerCountry.Gold < cost)
		{
			can = false;
			reason = $"Costs {cost:0} gold; we have {gs.PlayerCountry.Gold:0}";
		}
		button.Disabled = !can;
		button.TooltipText = can ? $"The government builds a {t.Name.ToLowerInvariant()} level for {cost:0} gold" : reason;
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
