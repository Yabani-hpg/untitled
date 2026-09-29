using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.UI;

/// <summary>
/// The bar across the top of the screen: the player's country (flag, name, ruler's portrait) and its
/// resources: gold, population, the three kinds of research, and its soldiers. Clicking the country opens
/// the country window; clicking research opens the research trees.
/// </summary>
public partial class PlayerBar : PanelContainer
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }
	[Export] public NodePath CameraRigPath { get; set; }
	[Export] public NodePath CountryPanelPath { get; set; }
	[Export] public NodePath ResearchPanelPath { get; set; }

	TextureRect _flag;
	TextureRect _portrait;
	Label _name;
	Label _ruler;
	readonly Dictionary<string, (Control Box, Label Value, Label Detail)> _chips = new();

	static readonly Dictionary<TechCategory, string> ResearchIcons = new()
	{
		[TechCategory.Military] = "military",
		[TechCategory.Admin] = "admin",
		[TechCategory.Science] = "science",
	};

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", HudStyle.Panel());
		MouseFilter = MouseFilterEnum.Stop;
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		AddChild(row);

		// the country: flag, ruler's portrait, name and ruler
		var country = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop, TooltipText = "Our country: its ruler, finances, laws and army" };
		country.AddThemeConstantOverride("separation", 8);
		country.GuiInput += e => Clicked(e, () => CountryPanel()?.Toggle(0));
		row.AddChild(country);
		_flag = new TextureRect { MouseFilter = MouseFilterEnum.Ignore };
		var flagFrame = HudStyle.Framed(_flag);
		flagFrame.MouseFilter = MouseFilterEnum.Ignore;
		flagFrame.CustomMinimumSize = new Vector2(57, 38);
		flagFrame.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		country.AddChild(flagFrame);
		_portrait = new TextureRect { MouseFilter = MouseFilterEnum.Ignore };
		var portraitFrame = HudStyle.Framed(_portrait);
		portraitFrame.MouseFilter = MouseFilterEnum.Ignore;
		portraitFrame.CustomMinimumSize = new Vector2(40, 50);
		country.AddChild(portraitFrame);
		var text = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, CustomMinimumSize = new Vector2(112, 0), MouseFilter = MouseFilterEnum.Ignore };
		country.AddChild(text);
		_name = HudStyle.Title("", TitleFont, 20);
		_name.MouseFilter = MouseFilterEnum.Ignore;
		text.AddChild(_name);
		_ruler = HudStyle.Body("", BodyFont, 12, HudStyle.Muted);
		_ruler.MouseFilter = MouseFilterEnum.Ignore;
		text.AddChild(_ruler);

		row.AddChild(new VSeparator());
		var chips = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
		chips.AddThemeConstantOverride("separation", 5);
		row.AddChild(chips);
		Chip(chips, "gold", "gold", () => CountryPanel()?.Toggle(0), 88);
		Chip(chips, "population", "population", () => CountryPanel()?.Toggle(0), 88);
		foreach (var (cat, icon) in ResearchIcons)
		{
			TechCategory c = cat;
			Chip(chips, icon, icon, () => ResearchPanel()?.Toggle(c), 106);
		}
		Chip(chips, "army", "army", () => CountryPanel()?.Toggle(2), 84);

		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.PhaseChanged += Refresh;
		gs.FlagsChanged += Refresh;
		gs.MonthAdvanced += Refresh;
		gs.ProvinceChanged += OnProvinceChanged;
		gs.TribesChanged += Refresh;
		gs.ResearchChanged += Refresh;
		gs.ArmiesChanged += Refresh;
		gs.LawsChanged += Refresh;
		gs.RulerChanged += OnRulerChanged;
		Refresh();
		// a loaded game opens on the player's capital
		if (gs.Phase == GamePhase.Playing)
			GetTree().CreateTimer(0.4).Timeout += FocusCapital;
	}

	public override void _ExitTree()
	{
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.PhaseChanged -= Refresh;
		gs.FlagsChanged -= Refresh;
		gs.MonthAdvanced -= Refresh;
		gs.ProvinceChanged -= OnProvinceChanged;
		gs.TribesChanged -= Refresh;
		gs.ResearchChanged -= Refresh;
		gs.ArmiesChanged -= Refresh;
		gs.LawsChanged -= Refresh;
		gs.RulerChanged -= OnRulerChanged;
	}

	void OnProvinceChanged(int id) => Refresh();
	void OnRulerChanged(string tag) => Refresh();

	CountryPanel CountryPanel() => GetNodeOrNull<CountryPanel>(CountryPanelPath ?? new NodePath());
	ResearchPanel ResearchPanel() => GetNodeOrNull<ResearchPanel>(ResearchPanelPath ?? new NodePath());

	static void Clicked(InputEvent e, Action action)
	{
		if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			action();
	}

	/// <summary>A resource on the bar: its icon, its value and a detail line below.</summary>
	void Chip(HBoxContainer parent, string key, string icon, Action onClick, float width = 100)
	{
		var box = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(width, 0) };
		var style = new StyleBoxFlat { BgColor = new Color(0.2f, 0.15f, 0.09f, 0.7f), BorderColor = new Color(0.75f, 0.6f, 0.32f, 0.35f) };
		style.SetBorderWidthAll(1);
		style.SetCornerRadiusAll(4);
		style.SetContentMarginAll(4);
		box.AddThemeStyleboxOverride("panel", style);
		box.GuiInput += e => Clicked(e, onClick);
		parent.AddChild(box);
		var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		row.AddThemeConstantOverride("separation", 5);
		box.AddChild(row);
		row.AddChild(new TextureRect
		{
			Texture = HudStyle.Texture($"res://gfx/icons/{icon}.png"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = new Vector2(24, 24),
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			MouseFilter = MouseFilterEnum.Ignore,
		});
		var text = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		text.AddThemeConstantOverride("separation", -2);
		row.AddChild(text);
		var value = HudStyle.Body("", BodyFont, 15);
		value.MouseFilter = MouseFilterEnum.Ignore;
		text.AddChild(value);
		var detail = HudStyle.Body("", BodyFont, 11, HudStyle.Muted);
		detail.MouseFilter = MouseFilterEnum.Ignore;
		detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		detail.CustomMinimumSize = new Vector2(width - 40, 0);
		text.AddChild(detail);
		_chips[key] = (box, value, detail);
	}

	void Set(string key, string value, string detail, string tooltip, Color? detailColor = null)
	{
		var (box, v, d) = _chips[key];
		v.Text = value;
		d.Text = detail;
		d.AddThemeColorOverride("font_color", detailColor ?? HudStyle.Muted);
		box.TooltipText = tooltip;
	}

	void FocusCapital()
	{
		if (GameState.Instance?.PlayerCountry is { } c)
			MapCamera.FocusProvince(GetNodeOrNull<Node3D>(CameraRigPath ?? new NodePath()), c.CapitalId);
	}

	/// <summary>1234 → "1,234"; 1234567 → "1.23M".</summary>
	static string Short(double n) => Math.Abs(n) >= 1e6 ? $"{n / 1e6:0.00}M" : $"{n:N0}";

	void Refresh()
	{
		GameState gs = GameState.Instance;
		Country c = gs.PlayerCountry;
		Visible = gs.Phase == GamePhase.Playing && c != null;
		if (!Visible)
			return;
		_flag.Texture = HudStyle.Texture(c.CurrentFlag?.ImagePath);
		_name.Text = c.Name;
		Character r = c.Ruler;
		_portrait.Texture = HudStyle.Texture(r?.Portrait);
		_ruler.Text = r == null ? c.Government : $"{c.RulerTitle} {r.Name}, {r.AgeOn(gs.Date)}";

		var owned = gs.ProvincesOf(c.Tag).ToList();
		var ledger = EconomyRules.MonthlyLedger(c, owned, gs.Tribes.Values, gs.Armies.Values, gs.Date);
		Set("gold", $"{c.Gold:N0}", $"{ledger.Balance:+0.0;-0.0}/month",
			$"Treasury: {c.Gold:N0} gold\n\nGold a month:\n+{ledger.Tax:0.0} tax (tribesmen, nomads, priests and those under arms pay none)"
			+ $"\n-{ledger.Armies:0.0} armies\n-{ledger.Garrisons:0.0} garrisons\n-{ledger.Mercenaries:0.0} mercenaries",
			ledger.Balance >= 0 ? HudStyle.Good : HudStyle.Bad);

		long men = owned.Sum(p => p.UnitsOf(Sex.Male)) * (long)PopGroup.PeoplePerUnit;
		long women = owned.Sum(p => p.UnitsOf(Sex.Female)) * (long)PopGroup.PeoplePerUnit;
		var byWork = owned.SelectMany(p => p.Pops).GroupBy(g => g.Occupation).OrderByDescending(g => g.Sum(x => x.Units))
			.Select(g => $"{g.Key.Name}: {g.Sum(x => x.Units) * PopGroup.PeoplePerUnit:N0}");
		Set("population", Short(men + women), $"{owned.Count} provinces",
			$"Population: {men + women:N0} ({men:N0} men, {women:N0} women) in {owned.Count} provinces\n\n{string.Join("\n", byWork)}");

		var (points, sources) = gs.ResearchPoints(c);
		bool war = TechRules.AtWar(c, gs.Armies.Values);
		foreach (var (cat, icon) in ResearchIcons)
		{
			Tech t = TechRules.Current(c, cat);
			double stock = c.ResearchStockpile.GetValueOrDefault(cat);
			string detail = t != null ? $"{TechRules.Progress(c, t) / t.Cost:P0} {t.Name}" : stock >= 1 ? $"{stock:0} stored" : "nothing chosen";
			string tip = $"{cat} research: +{points[cat]:0.0} points a month" + (cat == TechCategory.Military && war ? " (at war: +50%)" : "")
				+ "\n" + string.Join("\n", sources.Where(s => s.Points[cat] > 0.005).Select(s => $"  {s.Name}: +{s.Points[cat]:0.00}"))
				+ (t != null ? $"\n\nResearching {t.Name}: {TechRules.Progress(c, t):0} of {t.Cost:0}" : "\n\nNothing is being researched: the points are stockpiled.")
				+ (stock >= 1 ? $"\nStockpile: {stock:0} points" : "") + "\n\nClick to open the research trees.";
			Set(icon, $"+{points[cat]:0.0}", detail, tip, t == null ? HudStyle.Bad : HudStyle.Muted);
		}

		int under = MilitaryRules.UnderArms(c, gs.Armies.Values, owned);
		int mercs = TribeRules.Mercenaries(gs.Tribes.Values, c.Tag);
		int levy = gs.AvailableLevies(c);
		Set("army", $"{under}", $"{levy} to levy" + (mercs > 0 ? $", {mercs} merc." : ""),
			$"Under arms: {under} regiments of 1000 in our armies and garrisons.\nWe can levy {levy} more"
			+ (mercs > 0 ? $"; {mercs} regiments of mercenaries fight for us." : ".") + "\n\nClick to open the Military tab.");
	}
}
