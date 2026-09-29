using Godot;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.UI;

/// <summary>The player's country in the top left corner: its flag and name, and its ruler's portrait.</summary>
public partial class PlayerBar : PanelContainer
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }
	[Export] public NodePath CameraRigPath { get; set; }
	[Export] public NodePath CountryPanelPath { get; set; }

	TextureRect _flag;
	TextureRect _portrait;
	Label _name;
	Label _ruler;

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", HudStyle.Panel());
		MouseFilter = MouseFilterEnum.Stop;
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		AddChild(row);
		_flag = new TextureRect();
		var flagFrame = HudStyle.Framed(_flag);
		flagFrame.CustomMinimumSize = new Vector2(84, 56);
		flagFrame.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		row.AddChild(flagFrame);
		var text = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, CustomMinimumSize = new Vector2(200, 0) };
		row.AddChild(text);
		_name = HudStyle.Title("", TitleFont, 22);
		text.AddChild(_name);
		_ruler = HudStyle.Body("", BodyFont, 13, HudStyle.Muted);
		text.AddChild(_ruler);
		_portrait = new TextureRect();
		var portraitFrame = HudStyle.Framed(_portrait);
		portraitFrame.CustomMinimumSize = new Vector2(56, 70);
		row.AddChild(portraitFrame);

		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.PhaseChanged += Refresh;
		gs.FlagsChanged += Refresh;
		gs.MonthAdvanced += Refresh;
		gs.ProvinceChanged += OnProvinceChanged;
		gs.TribesChanged += Refresh;
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
	}

	void OnProvinceChanged(int id) => Refresh();

	/// <summary>Click the bar to open the country window (its overview and laws).</summary>
	public override void _GuiInput(InputEvent e)
	{
		if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
		{
			GetNodeOrNull<CountryPanel>(CountryPanelPath ?? new NodePath())?.Toggle();
			AcceptEvent();
		}
	}

	void FocusCapital()
	{
		if (GameState.Instance?.PlayerCountry is { } c)
			MapCamera.FocusProvince(GetNodeOrNull<Node3D>(CameraRigPath ?? new NodePath()), c.CapitalId);
	}

	void Refresh()
	{
		GameState gs = GameState.Instance;
		Country c = gs.PlayerCountry;
		Visible = gs.Phase == GamePhase.Playing && c != null;
		if (!Visible)
			return;
		_flag.Texture = HudStyle.Texture(c.CurrentFlag?.ImagePath);
		_flag.TooltipText = c.CurrentFlag?.Name;
		_name.Text = c.Name;
		Character r = c.Ruler;
		_portrait.Texture = HudStyle.Texture(r?.Portrait);
		string ruler = r == null ? c.Government : $"{c.RulerTitle} {r.Name}, age {r.AgeOn(gs.Date)}";
		int mercs = Untitled.Rules.TribeRules.Mercenaries(gs.Tribes.Values, c.Tag);
		var ledger = Untitled.Rules.EconomyRules.MonthlyLedger(c, gs.ProvincesOf(c.Tag), gs.Tribes.Values, gs.Date);
		_ruler.Text = $"{ruler}\nTreasury: {c.Gold:0} gold ({ledger.Balance:+0.0;-0.0} a month)"
			+ $"\nManpower: {c.Manpower} of {c.MaxManpower} regiments" + (mercs > 0 ? $" · {mercs} mercenaries" : "");
		_ruler.TooltipText = $"Capital: {gs.CapitalText(c)}\n\nGold a month:\n+{ledger.Tax:0.0} tax from our settled people (tribesmen and nomads pay none)"
			+ $"\n-{ledger.Garrisons:0.0} garrisons\n-{ledger.Mercenaries:0.0} mercenaries\n\n"
			+ "Manpower: regiments of 1000 soldiers raised from the people of our core provinces.\nArmies and garrisons are drawn from them; they refill over ten years.";
		_ruler.MouseFilter = MouseFilterEnum.Pass;
		TooltipText = "Our country: its ruler, finances and laws";
		_portrait.TooltipText = r?.FullName ?? r?.Name;
	}
}
