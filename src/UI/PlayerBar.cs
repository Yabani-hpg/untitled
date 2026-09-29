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
	}

	/// <summary>Click the bar to go to the capital.</summary>
	public override void _GuiInput(InputEvent e)
	{
		if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
		{
			FocusCapital();
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
		_ruler.Text = r == null ? c.Government : $"{c.RulerTitle} {r.Name}, age {r.AgeOn(gs.Date)}\nCapital: {gs.CapitalText(c)}";
		_portrait.TooltipText = r?.FullName ?? r?.Name;
	}
}
