using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.UI;

/// <summary>
/// The country selection screen after New Game: the world on 1 January 1200 BC, where clicking any
/// province picks its country. Shows the country's flag, government, capital, people and its ruler with
/// portrait, and starts the game as that country.
/// </summary>
public partial class CountrySelectScreen : Control
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }
	[Export] public NodePath CameraRigPath { get; set; }
	[Export(PropertyHint.File, "*.tscn")] public string MainMenuScene { get; set; } = "res://scenes/main_menu.tscn";

	Node3D _rig;
	TextureRect _flag;
	Label _flagName;
	Label _name;
	Label _government;
	TextureRect _portrait;
	Label _rulerName;
	Label _rulerDetails;
	GridContainer _facts;
	Button _play;
	Label _hint;

	/// <summary>Featured nations, shown as buttons; the first is the one the screen opens on.</summary>
	static readonly string[] Featured = { "EGY" };

	public override void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;
		_rig = GetNodeOrNull<Node3D>(CameraRigPath ?? new NodePath());

		BuildBanner();
		BuildNationPanel();
		BuildBottomBar();

		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.SelectedProvinceChanged += OnProvinceClicked;
		gs.FocusedCountryChanged += OnFocusChanged;
		gs.PhaseChanged += OnPhaseChanged;
		gs.FlagsChanged += Refresh;
		OnPhaseChanged();
		// after the camera's own start-up framing
		if (Visible)
			GetTree().CreateTimer(0.4).Timeout += FlyToFocus;
	}

	public override void _ExitTree()
	{
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.SelectedProvinceChanged -= OnProvinceClicked;
		gs.FocusedCountryChanged -= OnFocusChanged;
		gs.PhaseChanged -= OnPhaseChanged;
		gs.FlagsChanged -= Refresh;
	}

	// ------------------------------------------------------------------------------------------ layout

	void BuildBanner()
	{
		var banner = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
		banner.AddThemeStyleboxOverride("panel", HudStyle.Panel());
		banner.SetAnchorsPreset(LayoutPreset.CenterTop);
		banner.GrowHorizontal = GrowDirection.Both;
		banner.OffsetTop = 16;
		// centred in the space right of the nation panel
		banner.OffsetLeft = banner.OffsetRight = 200;
		AddChild(banner);
		var box = new VBoxContainer();
		banner.AddChild(box);
		var title = HudStyle.Title("Choose your nation", TitleFont, 26);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		box.AddChild(title);
		string date = GameState.Instance?.DateText ?? "";
		var sub = HudStyle.Body($"{date} · The Bronze Age collapses. Click a province to pick its nation.", BodyFont, 14, HudStyle.Muted);
		sub.HorizontalAlignment = HorizontalAlignment.Center;
		box.AddChild(sub);
		_hint = HudStyle.Body("Beyond the few organized states, the world belongs to tribes and nomads.", BodyFont, 13, HudStyle.Muted);
		_hint.HorizontalAlignment = HorizontalAlignment.Center;
		box.AddChild(_hint);
	}

	void BuildNationPanel()
	{
		var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, CustomMinimumSize = new Vector2(380, 0) };
		panel.AddThemeStyleboxOverride("panel", HudStyle.Panel());
		panel.Position = new Vector2(16, 16);
		AddChild(panel);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 8);
		panel.AddChild(box);

		// flag and name
		var head = new HBoxContainer();
		head.AddThemeConstantOverride("separation", 12);
		box.AddChild(head);
		_flag = new TextureRect();
		var flagFrame = HudStyle.Framed(_flag);
		flagFrame.CustomMinimumSize = new Vector2(120, 80);
		head.AddChild(flagFrame);
		var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
		head.AddChild(names);
		_name = HudStyle.Title("", TitleFont, 26);
		names.AddChild(_name);
		_government = HudStyle.Body("", BodyFont, 14, HudStyle.Muted);
		names.AddChild(_government);
		_flagName = HudStyle.Body("", BodyFont, 12, HudStyle.Muted);
		names.AddChild(_flagName);

		// ruler
		var rulerSection = new PanelContainer();
		rulerSection.AddThemeStyleboxOverride("panel", HudStyle.Section());
		box.AddChild(rulerSection);
		var rulerRow = new HBoxContainer();
		rulerRow.AddThemeConstantOverride("separation", 12);
		rulerSection.AddChild(rulerRow);
		_portrait = new TextureRect();
		var portraitFrame = HudStyle.Framed(_portrait);
		portraitFrame.CustomMinimumSize = new Vector2(144, 180);
		rulerRow.AddChild(portraitFrame);
		var rulerText = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		rulerRow.AddChild(rulerText);
		rulerText.AddChild(HudStyle.Body("Ruler", BodyFont, 12, HudStyle.Muted));
		_rulerName = HudStyle.Title("", TitleFont, 18);
		_rulerName.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_rulerName.CustomMinimumSize = new Vector2(190, 0);
		rulerText.AddChild(_rulerName);
		_rulerDetails = HudStyle.Body("", BodyFont, 13);
		_rulerDetails.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_rulerDetails.CustomMinimumSize = new Vector2(190, 0);
		rulerText.AddChild(_rulerDetails);

		// facts
		_facts = new GridContainer { Columns = 2 };
		_facts.AddThemeConstantOverride("h_separation", 16);
		box.AddChild(_facts);

		// featured nations
		var featured = new HFlowContainer();
		featured.AddChild(HudStyle.Body("Featured:", BodyFont, 13, HudStyle.Muted));
		foreach (string tag in Featured)
		{
			Country c = GameState.Instance?.GetCountry(tag);
			if (c == null)
				continue;
			var b = HudStyle.Button(c.Name, BodyFont, 13);
			b.Pressed += () =>
			{
				GameState.Instance.FocusCountry(tag);
				FlyToFocus();
			};
			featured.AddChild(b);
		}
		box.AddChild(featured);
	}

	void BuildBottomBar()
	{
		var bar = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
		bar.AddThemeStyleboxOverride("panel", HudStyle.Panel());
		bar.SetAnchorsPreset(LayoutPreset.CenterBottom);
		bar.GrowHorizontal = GrowDirection.Both;
		bar.GrowVertical = GrowDirection.Begin;
		bar.OffsetBottom = -16;
		AddChild(bar);
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 16);
		bar.AddChild(row);
		var back = HudStyle.Button("Back to menu", BodyFont, 16);
		back.Pressed += () => GetTree().ChangeSceneToFile(MainMenuScene);
		row.AddChild(back);
		_play = HudStyle.Button("Play", TitleFont, 20);
		_play.CustomMinimumSize = new Vector2(220, 0);
		_play.Pressed += () => GameState.Instance?.StartPlaying(GameState.Instance.FocusedCountryTag);
		row.AddChild(_play);
	}

	// -------------------------------------------------------------------------------------- behaviour

	void OnPhaseChanged()
	{
		Visible = GameState.Instance.Phase == GamePhase.CountrySelection;
		if (Visible)
			Refresh();
	}

	void OnProvinceClicked(int id)
	{
		GameState gs = GameState.Instance;
		if (gs.Phase != GamePhase.CountrySelection)
			return;
		Province p = gs.GetProvince(id);
		if (p == null || p.IsWater)
			return;
		if (p.OwnerTag != null)
		{
			gs.FocusCountry(p.OwnerTag);
			_hint.Text = $"{p.Name}: part of {gs.GetCountry(p.OwnerTag)?.Name}.";
			return;
		}
		Tribe t = gs.TribeOf(p);
		_hint.Text = t == null
			? $"{p.Name} is uncontrolled and empty."
			: $"{p.Name} is the land of {t.TheName}, {(t.IsNomadic ? "a nomadic horde" : "settled tribes")} with no state: an unsettled country, not playable.";
	}

	void OnFocusChanged(string tag) => Refresh();

	/// <summary>Moves the camera to the focused country's capital.</summary>
	void FlyToFocus()
	{
		GameState gs = GameState.Instance;
		Country c = gs.GetCountry(gs.FocusedCountryTag);
		if (c != null)
			MapCamera.FocusProvince(_rig, c.CapitalId);
	}

	void Refresh()
	{
		GameState gs = GameState.Instance;
		if (gs == null || !Visible)
			return;
		Country c = gs.GetCountry(gs.FocusedCountryTag);
		_play.Disabled = c == null;
		_play.Text = c == null ? "Play" : $"Play as {c.Name}";
		if (c == null)
			return;

		_flag.Texture = HudStyle.Texture(c.CurrentFlag?.ImagePath);
		_name.Text = c.Name;
		_government.Text = c.Government;
		_flagName.Text = c.CurrentFlag?.Name != c.Name ? $"Flag: {c.CurrentFlag?.Name}" : "";

		Character r = c.Ruler;
		_portrait.Texture = HudStyle.Texture(r?.Portrait);
		if (r == null)
		{
			_rulerName.Text = "No ruler";
			_rulerDetails.Text = "";
		}
		else
		{
			_rulerName.Text = $"{c.RulerTitle} {r.Name}";
			Province home = gs.GetProvince(r.ProvinceId);
			string lines = r.FullName != null ? $"{r.FullName}\n" : "";
			if (r.Dynasty != null)
				lines += $"Dynasty: {r.Dynasty}\n";
			lines += $"Age {r.AgeOn(gs.Date)}\n";
			lines += $"Belongs to the {r.Culture?.Name} {r.Occupation?.Name.ToLowerInvariant()} ({r.Religion?.Name}) of {home?.Name ?? "nowhere"}";
			_rulerDetails.Text = lines;
		}

		foreach (Node child in _facts.GetChildren())
			child.QueueFree();
		var provinces = gs.ProvincesOf(c.Tag).ToList();
		long people = provinces.Sum(p => (long)p.TotalUnits) * PopGroup.PeoplePerUnit;
		Culture culture = provinces.SelectMany(p => p.Pops).GroupBy(g => g.Culture)
			.OrderByDescending(g => g.Sum(x => x.Units)).Select(g => g.Key).FirstOrDefault();
		void Fact(string key, string value)
		{
			_facts.AddChild(HudStyle.Body(key, BodyFont, 14, HudStyle.Muted));
			_facts.AddChild(HudStyle.Body(value, BodyFont, 14));
		}
		Fact("Capital", gs.CapitalText(c));
		Fact("Provinces", provinces.Count.ToString());
		Fact("Population", $"{people:N0}");
		if (culture != null)
			Fact("Culture", $"{culture.Name} ({culture.Language.Name})");
	}
}
