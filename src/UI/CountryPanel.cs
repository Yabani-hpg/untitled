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
	VBoxContainer _decisions;
	VBoxContainer _trade;
	VBoxContainer _estates;
	VBoxContainer _court;
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
		_decisions = AddTab("Decisions");
		_trade = AddTab("Trade");
		_estates = AddTab("Estates");
		_court = AddTab("Court");

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
		gs.DecisionsChanged += Refresh;
		gs.TradeChanged += Refresh;
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
		gs.DecisionsChanged -= Refresh;
		gs.TradeChanged -= Refresh;
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
		FillDecisions(gs, c);
		FillTrade(gs, c);
		FillEstates(gs, c);
		FillCourt(gs, c);
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
		Ledger ledger = EconomyRules.MonthlyLedger(c, owned, gs.Tribes.Values, gs.Armies.Values, gs.Date, gs.Trade);
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
		Row("Stability", $"{c.Stability:+0;-0;0} · {StateRules.StabilityText(c.Stability)}", c.Stability >= 0 ? HudStyle.Good : HudStyle.Bad);
		Row("Legitimacy", $"{c.Legitimacy:0} · prestige {c.Prestige:+0;-0;0}", c.Legitimacy >= 60 ? HudStyle.Text : HudStyle.Bad);
		Row("War exhaustion", $"{c.WarExhaustion:0.0} of {StateRules.MaxExhaustion:0}" + (gs.WarsOf(c.Tag).Any() ? $" · at war ({gs.WarsOf(c.Tag).Count()})" : ""), c.WarExhaustion >= 5 ? HudStyle.Bad : HudStyle.Text);
		Row("Unrest", $"uprisings x{StateRules.UnrestFactor(c):0.00} · tax x{StateRules.TaxFactor(c):0.00}", StateRules.UnrestFactor(c) > 1.3 ? HudStyle.Bad : HudStyle.Muted);
		Row("Treasury", $"{c.Gold:0} gold" + (c.IsBankrupt(gs.Date) ? $" · bankrupt until {gs.Definitions.DefaultCalendar.Format(c.BankruptUntil)}: tax at {EconomyRules.BankruptTaxFactor:P0}" : ""), c.IsBankrupt(gs.Date) ? HudStyle.Bad : HudStyle.Text);
		if (c.Loans.Count > 0)
			Row("Debt", $"{c.Loans.Count} loans, {c.Debt:0} gold, {ledger.Interest:0.0} interest a month · the oldest ({c.Loans[0].Amount:0} at {c.Loans[0].Rate:P0}) falls due {gs.Definitions.DefaultCalendar.Format(c.Loans[0].Due)}", HudStyle.Bad);
		Row("Each month", $"{ledger.Balance:+0.0;-0.0} gold: +{ledger.Tax:0.0} tax, +{ledger.Trade:0.0} trade, +{ledger.Production:0.0} production, -{ledger.Armies:0.0} armies, -{ledger.Garrisons:0.0} garrisons, -{ledger.Mercenaries:0.0} mercenaries" + (ledger.Interest > 0 ? $", -{ledger.Interest:0.0} interest" : "") + (ledger.Advisors > 0 ? $", -{ledger.Advisors:0.0} advisors" : ""),
			ledger.Balance >= 0 ? HudStyle.Good : HudStyle.Bad);
		long men = owned.Sum(p => p.UnitsOf(Sex.Male)), women = owned.Sum(p => p.UnitsOf(Sex.Female));
		Row("Men and women", $"{men * PopGroup.PeoplePerUnit:N0} men · {women * PopGroup.PeoplePerUnit:N0} women at home");
		Row("Under arms", $"{MilitaryRules.UnderArms(c, gs.Armies.Values, owned)} regiments · {gs.AvailableLevies(c)} more to levy · {TribeRules.Mercenaries(gs.Tribes.Values, c.Tag)} mercenaries");
		Row("Rivals", c.Rivals.Count > 0 ? string.Join(", ", c.Rivals.Select(t => gs.GetCountry(t)?.Name)) : "none named", HudStyle.Muted);
		var against = gs.CoalitionAgainst(c.Tag).ToList();
		if (against.Count > 0)
			Row("Coalition", $"{against.Count} stand against our aggression: {string.Join(", ", against.Select(m => m.Name))}", HudStyle.Bad);
		if (c.CoalitionAgainst != null)
			Row("Coalition", $"we stand against {gs.GetCountry(c.CoalitionAgainst)?.Name} with {gs.CoalitionAgainst(c.CoalitionAgainst).Count() - 1} others", HudStyle.Gold);
		if (c.OverlordTag != null)
			Row("Overlord", $"{gs.GetCountry(c.OverlordTag)?.Name} · we pay {gs.TributeOf(c):0.0} gold a month", HudStyle.Bad);
		var vassals = gs.VassalsOf(c.Tag).ToList();
		if (vassals.Count > 0)
			Row("Vassals", string.Join(", ", vassals.Select(v => $"{v.Name} ({gs.TributeOf(v):0.0} gold)")));
		Row("Allied tribes", string.Join(", ", gs.Tribes.Values.Where(t => t.AlliedTag == c.Tag).Select(t => t.Name)) is { Length: > 0 } a ? a : "None");
		var actions = new HBoxContainer();
		actions.AddThemeConstantOverride("separation", 6);
		_overview.AddChild(actions);
		double stabCost = StateRules.StabilityCost(c, owned.Count);
		var stab = HudStyle.Button($"Raise stability ({stabCost:0} gold)", BodyFont, 13);
		stab.Disabled = !StateRules.CanRaiseStability(c, owned.Count, out string why);
		stab.TooltipText = why ?? "Gifts to the temples and the great men: the realm settles";
		stab.Pressed += () => _status.Text = gs.RaiseStability(out string r) ? "" : r;
		actions.AddChild(stab);
		var relief = HudStyle.Button($"Ease war exhaustion ({StateRules.ExhaustionReliefCost:0} gold)", BodyFont, 13);
		relief.Disabled = c.WarExhaustion < 1 || c.Gold < StateRules.ExhaustionReliefCost;
		relief.TooltipText = "Pay the soldiers' families and the temples: exhaustion falls by one";
		relief.Pressed += () => _status.Text = gs.RelieveExhaustion(out string r) ? "" : r;
		actions.AddChild(relief);
		string loanWhy = gs.LoanObstacle(c);
		var loan = HudStyle.Button($"Take a loan ({EconomyRules.LoanSize(c):0} gold)", BodyFont, 13);
		loan.Disabled = loanWhy != null;
		loan.TooltipText = loanWhy ?? $"Borrow at {EconomyRules.LoanRate + 0.01 * c.Loans.Count:P0} a year, due in {EconomyRules.LoanYears} years (renewed dearer if unpaid); {c.Loans.Count} of {EconomyRules.MaxLoans} loans taken";
		loan.Pressed += () => _status.Text = gs.TakeLoan(out string r) ? "" : r;
		actions.AddChild(loan);
		if (c.Loans.Count > 0)
		{
			var repay = HudStyle.Button($"Repay a loan ({c.Loans[0].Amount:0} gold)", BodyFont, 13);
			repay.Disabled = c.Gold < c.Loans[0].Amount;
			repay.TooltipText = "Pay off the oldest loan";
			repay.Pressed += () => _status.Text = gs.RepayLoan(out string r) ? "" : r;
			actions.AddChild(repay);
			var bankrupt = HudStyle.Button("Declare bankruptcy", BodyFont, 13);
			bankrupt.TooltipText = $"Repudiate every loan: stability -3, legitimacy -20, prestige -30, tax at {EconomyRules.BankruptTaxFactor:P0} and no credit for {EconomyRules.BankruptcyYears} years";
			bankrupt.Pressed += () => _status.Text = gs.DeclareBankruptcy(out string r) ? "" : r;
			actions.AddChild(bankrupt);
		}
	}

	static string Kind(ControlKind? k) => k switch
	{
		ControlKind.Core => "cores",
		ControlKind.Absorbed => "absorbed",
		ControlKind.Subjugated => "subjugated",
		ControlKind.Conquered => "conquered",
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

	// ------------------------------------------------------------------------------------ Decisions

	void FillDecisions(GameState gs, Country c)
	{
		Clear(_decisions);
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
			_decisions.AddChild(panel);
			var box = new VBoxContainer();
			box.AddThemeConstantOverride("separation", 4);
			panel.AddChild(box);
			box.AddChild(HudStyle.Title(title, TitleFont, 17));
			return box;
		}

		// missions: the country's own goals, in their chains
		var missions = c.Definition?.Missions ?? new List<Mission>();
		var mBox = Section("Missions");
		if (missions.Count == 0)
			mBox.AddChild(Text("Our house has set itself no particular goals."));
		foreach (Mission m in missions)
		{
			bool done = c.Done.Contains(m.Id);
			bool open = !done && (m.After == null || c.Done.Contains(m.After));
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			mBox.AddChild(row);
			var mark = HudStyle.Body(done ? "✔" : open ? "◯" : "·", BodyFont, 15, done ? new Color(0.55f, 0.85f, 0.45f) : open ? HudStyle.Gold : HudStyle.Muted);
			mark.CustomMinimumSize = new Vector2(22, 0);
			row.AddChild(mark);
			var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			row.AddChild(col);
			col.AddChild(HudStyle.Body(m.Title, BodyFont, 14, done ? HudStyle.Muted : Colors.White));
			if (!done)
			{
				col.AddChild(Text(m.Text));
				string need = open ? m.RequiresText ?? "Meet its conditions" : $"After: {missions.Find(x => x.Id == m.After)?.Title}";
				var needLabel = Text($"Requires: {need}", 12, open ? HudStyle.Gold : HudStyle.Muted);
				needLabel.TooltipText = "Reward:\n" + EventPanel.Describe(m.Reward, gs);
				col.AddChild(needLabel);
			}
		}

		// decisions open to us
		var dBox = Section("Decisions");
		var visible = gs.VisibleDecisions(c).ToList();
		if (visible.Count == 0)
			dBox.AddChild(Text("Nothing to decide for now."));
		foreach (Decision d in visible)
		{
			bool can = gs.CanTakeDecision(c, d, out string why);
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 10);
			dBox.AddChild(row);
			var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			row.AddChild(col);
			col.AddChild(HudStyle.Body(d.Title, BodyFont, 14, Colors.White));
			col.AddChild(Text(d.Text));
			col.AddChild(Text(can ? EventPanel.Describe(d.Effects, gs).Replace("\n", ", ") : why, 12, can ? HudStyle.Gold : HudStyle.Muted));
			var b = HudStyle.Button("Decide", BodyFont, 13);
			b.Disabled = !can;
			b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			b.TooltipText = can ? EventPanel.Describe(d.Effects, gs) : why;
			string id = d.Id;
			b.Pressed += () =>
			{
				if (!gs.TakeDecision(id, out string reason))
					_status.Text = reason;
				Refresh();
			};
			row.AddChild(b);
		}
	}

	// ------------------------------------------------------------------------------------ Trade

	void FillTrade(GameState gs, Country c)
	{
		Clear(_trade);
		Label Text(string text, int size = 13, Color? color = null)
		{
			var l = HudStyle.Body(text, BodyFont, size, color ?? HudStyle.Muted);
			l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			l.CustomMinimumSize = new Vector2(480, 0);
			return l;
		}
		VBoxContainer Section(string title)
		{
			var panel = new PanelContainer();
			panel.AddThemeStyleboxOverride("panel", HudStyle.Section());
			_trade.AddChild(panel);
			var box = new VBoxContainer();
			box.AddThemeConstantOverride("separation", 4);
			panel.AddChild(box);
			box.AddChild(HudStyle.Title(title, TitleFont, 17));
			return box;
		}
		TradeReport trade = gs.Trade;
		TradeNode home = gs.HomeNode(c);
		var (income, production) = gs.TradeIncomeOf(c);
		var head = Section("Merchants");
		head.AddChild(Text("Every province's goods are sold in its trade node. We take a share of a node's value by our trade power there, "
			+ $"at home ({home?.Name ?? "nowhere"}) and wherever a merchant collects; what no one collects flows downstream, steered by merchants, until an end node keeps it. "
			+ $"{gs.FreeMerchants(c)} of our {gs.MerchantsOf(c)} merchants are free."));
		head.AddChild(Text($"Trade brings us {income:0.0} gold a month, and the production of our provinces {production:0.0}.", 13, HudStyle.Gold));
		var nodes = Section("Trade nodes within our reach");
		if (trade == null)
			return;
		foreach (NodeReport r in trade.Nodes.Values.Where(r => TradeRules.InReach(c, r.Node, trade))
			.OrderByDescending(r => r.Collected.GetValueOrDefault(c.Tag)).ThenByDescending(r => r.Total))
		{
			Merchant m = c.Merchants.Find(x => x.NodeId == r.Node.Id);
			string id = r.Node.Id;
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 10);
			nodes.AddChild(row);
			var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			row.AddChild(col);
			col.AddChild(HudStyle.Body(r.Node.Name + (r.Node == home ? " · our home node" : r.Node.End ? " · end node" : ""), BodyFont, 14, Colors.White));
			string flow = r.Node.Downstream.Count > 0 ? "flows on to " + string.Join(", ", r.Node.Downstream.Select(d => d.Name)) : "keeps its value";
			col.AddChild(Text($"Worth {r.Total:0.0} a month: {r.Local:0.0} made here, {r.Incoming:0.0} from upstream; {flow}.", 12));
			string ours = $"Our power {r.Power.GetValueOrDefault(c.Tag):0.0} of {r.TotalPower:0.0} ({r.Share(c.Tag):P0})";
			if (r.Collected.TryGetValue(c.Tag, out double got))
				ours += $" · we collect {got * TradeRules.TradeIncomeFactor:0.0} gold";
			if (m != null)
				ours += m.Task == MerchantTask.Collect ? " · our merchant collects here" : $" · our merchant steers to {gs.GetTradeNode(m.ToNodeId)?.Name}";
			col.AddChild(Text(ours, 12, m != null ? HudStyle.Gold : HudStyle.Muted));
			var buttons = new VBoxContainer();
			buttons.AddThemeConstantOverride("separation", 2);
			row.AddChild(buttons);
			void Send(string label, string tip, MerchantTask task, string to)
			{
				var b = HudStyle.Button(label, BodyFont, 12);
				b.TooltipText = tip;
				b.Pressed += () =>
				{
					if (!gs.PlaceMerchant(c.Tag, id, task, to, out string why))
						_status.Text = why;
					Refresh();
				};
				buttons.AddChild(b);
			}
			if (r.Node != home && (m == null || m.Task != MerchantTask.Collect))
				Send("Collect", "Send a merchant to take our share of this node's value home", MerchantTask.Collect, null);
			foreach (TradeNode d in r.Node.Downstream)
			{
				if (m == null || m.Task != MerchantTask.Steer || m.ToNodeId != d.Id)
					Send($"Steer to {d.Name}", $"Send a merchant to push this node's uncollected value on to {d.Name}", MerchantTask.Steer, d.Id);
			}
			if (m != null)
			{
				var b = HudStyle.Button("Recall", BodyFont, 12);
				b.Pressed += () => { gs.RecallMerchant(c.Tag, id); Refresh(); };
				buttons.AddChild(b);
			}
		}
	}

	// ------------------------------------------------------------------------------------ Estates

	void FillEstates(GameState gs, Country c)
	{
		Clear(_estates);
		Label Text(string text, int size = 13, Color? color = null)
		{
			var l = HudStyle.Body(text, BodyFont, size, color ?? HudStyle.Muted);
			l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			l.CustomMinimumSize = new Vector2(560, 0);
			return l;
		}
		VBoxContainer Section(string title)
		{
			var panel = new PanelContainer();
			panel.AddThemeStyleboxOverride("panel", HudStyle.Section());
			_estates.AddChild(panel);
			var box = new VBoxContainer();
			box.AddThemeConstantOverride("separation", 4);
			panel.AddChild(box);
			box.AddChild(HudStyle.Title(title, TitleFont, 17));
			return box;
		}
		var crown = Section("The crown and the estates");
		double total = gs.EstatesInfluence(c);
		crown.AddChild(Text($"The crown holds {c.Crownland:0}% of the land (tax {(c.Crownland - EstateRules.StartCrownland) / 200:+0%;-0%;0%}); the estates together have {total:0} influence"
			+ (total > EstateRules.DangerInfluence ? ": too much, and the realm stirs." : "."), 13, total > EstateRules.DangerInfluence ? HudStyle.Bad : HudStyle.Muted));
		string seizeWhy = gs.SeizeObstacle(c);
		var seize = HudStyle.Button($"Seize land (+{EstateRules.SeizeCrownland:0}% crown land)", BodyFont, 13);
		seize.Disabled = seizeWhy != null;
		seize.TooltipText = seizeWhy ?? $"Every estate loses {-EstateRules.SeizeLoyalty:0} loyalty";
		seize.Pressed += () => { if (!gs.SeizeLand(out string r)) _status.Text = r; };
		crown.AddChild(seize);
		foreach (Estate e in gs.Definitions.Estates)
		{
			double loyalty = EstateRules.Loyalty(c, e), influence = gs.EstateInfluence(c, e);
			string standing = EstateRules.Standing(c, e);
			var box = Section(e.Name);
			box.AddChild(Text(e.Description));
			var mods = EstateRules.IsLoyal(c, e) ? e.Loyal : EstateRules.IsDisloyal(c, e) ? e.Disloyal : null;
			string effects = mods == null ? "neither helps nor hinders" : string.Join(", ", mods.Select(kv => $"{kv.Key.Replace('_', ' ')} {kv.Value:+0%;-0%}"));
			box.AddChild(Text($"Loyalty {loyalty:0} ({standing}; settles at {EstateRules.Equilibrium(c, e):0}) · influence {influence:0} · {effects}", 13,
				EstateRules.IsLoyal(c, e) ? HudStyle.Good : EstateRules.IsDisloyal(c, e) ? HudStyle.Bad : HudStyle.Text));
			foreach (Privilege p in e.Privileges)
			{
				bool granted = c.Privileges.Contains(p.Id);
				var row = new HBoxContainer();
				row.AddThemeConstantOverride("separation", 8);
				box.AddChild(row);
				var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
				row.AddChild(col);
				col.AddChild(HudStyle.Body(p.Name + (granted ? " · granted" : ""), BodyFont, 14, granted ? HudStyle.Gold : Colors.White));
				col.AddChild(Text($"{p.Description} Loyalty +{p.Loyalty:0}, influence +{p.Influence:0}; {string.Join(", ", p.Modifiers.Select(kv => $"{kv.Key.Replace('_', ' ')} {(kv.Key == "merchants" || kv.Key == "years_to_core" ? kv.Value.ToString("+0;-0") : kv.Value.ToString("+0%;-0%"))}"))}", 12));
				var b = HudStyle.Button(granted ? "Revoke" : "Grant", BodyFont, 12);
				b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
				b.TooltipText = granted ? $"They lose {-EstateRules.RevokeLoyalty:0} loyalty" : "Pleases them and strengthens them";
				string id = p.Id;
				b.Pressed += () => { if (!(granted ? gs.RevokePrivilege(id, out string r) : gs.GrantPrivilege(id, out r))) _status.Text = r; };
				row.AddChild(b);
			}
		}
	}

	// ------------------------------------------------------------------------------------ Court

	void FillCourt(GameState gs, Country c)
	{
		Clear(_court);
		Label Text(string text, int size = 13, Color? color = null)
		{
			var l = HudStyle.Body(text, BodyFont, size, color ?? HudStyle.Muted);
			l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			l.CustomMinimumSize = new Vector2(560, 0);
			return l;
		}
		VBoxContainer Section(string title)
		{
			var panel = new PanelContainer();
			panel.AddThemeStyleboxOverride("panel", HudStyle.Section());
			_court.AddChild(panel);
			var box = new VBoxContainer();
			box.AddThemeConstantOverride("separation", 4);
			panel.AddChild(box);
			box.AddChild(HudStyle.Title(title, TitleFont, 17));
			return box;
		}
		Control Person(Advisor a, string action, string tip, bool enabled, System.Action onPress)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 10);
			var portrait = new TextureRect { Texture = HudStyle.Texture(a.Portrait), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered };
			var frame = HudStyle.Framed(portrait);
			frame.CustomMinimumSize = new Vector2(44, 56);
			row.AddChild(frame);
			var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			row.AddChild(col);
			col.AddChild(HudStyle.Body($"{a.Name}, {a.AgeOn(gs.Date)} · {a.Type.Name}", BodyFont, 14, Colors.White));
			col.AddChild(Text($"Skill {new string('★', a.Skill)}{new string('☆', AdvisorRules.MaxSkill - a.Skill)} · {AdvisorRules.Describe(a)} · {AdvisorRules.Salary(a):0} gold a month · {a.Type.Description}", 12));
			var b = HudStyle.Button(action, BodyFont, 12);
			b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			b.Disabled = !enabled;
			b.TooltipText = tip;
			b.Pressed += () => onPress();
			row.AddChild(b);
			return row;
		}
		var court = Section("The court");
		court.AddChild(Text($"{c.RulerTitle} {c.Ruler?.Name} of the house of {c.Ruler?.Dynasty}, aged {c.Ruler?.AgeOn(gs.Date)}. One advisor of each kind may serve: admin, diplomatic and military. They cost {AdvisorRules.Salaries(c):0} gold a month in all."));
		foreach (string category in AdvisorRules.Categories)
		{
			Advisor a = AdvisorRules.Hired(c, category);
			if (a == null)
				court.AddChild(Text($"No {category} advisor serves.", 13, HudStyle.Bad));
			else
			{
				int id = a.Id;
				court.AddChild(Person(a, "Dismiss", "They leave the court", true, () => gs.DismissAdvisor(id)));
			}
		}
		var pool = Section("Candidates");
		pool.AddChild(Text("Those who offer their service this year; new faces come each January."));
		foreach (Advisor a in c.Candidates.OrderBy(a => a.Type.Category).ThenByDescending(a => a.Skill))
		{
			string why = gs.HireObstacle(c, a);
			int id = a.Id;
			pool.AddChild(Person(a, "Hire", why ?? "Take them into the court", why == null, () => { if (!gs.HireAdvisor(id, out string r)) _status.Text = r; }));
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
