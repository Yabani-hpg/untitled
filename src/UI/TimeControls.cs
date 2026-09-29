using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.UI;

/// <summary>
/// The date ("17 November 2026 AD"), pause/play and a 1x-5x speed menu. Hover the date to see it in every
/// calendar in use. Keys: Space pauses and resumes, + and - change speed.
/// </summary>
public partial class TimeControls : PanelContainer
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }

	Label _date;
	PlayPauseButton _play;
	OptionButton _speed;

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", HudStyle.Panel());
		MouseFilter = MouseFilterEnum.Stop;
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		AddChild(row);

		_date = HudStyle.Title("", TitleFont, 18);
		_date.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_date.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_date.HorizontalAlignment = HorizontalAlignment.Center;
		_date.MouseFilter = MouseFilterEnum.Pass;   // for the tooltip
		row.AddChild(_date);

		_play = new PlayPauseButton { CustomMinimumSize = new Vector2(34, 30), FocusMode = FocusModeEnum.None };
		foreach (var (state, alpha) in new[] { ("normal", 0.55f), ("hover", 0.7f), ("pressed", 0.45f) })
		{
			var s = new StyleBoxFlat { BgColor = new Color(0.45f, 0.33f, 0.18f, alpha), BorderColor = new Color(0.75f, 0.6f, 0.32f, 0.8f) };
			s.SetBorderWidthAll(1);
			s.SetCornerRadiusAll(4);
			_play.AddThemeStyleboxOverride(state, s);
		}
		_play.Pressed += () => GameState.Instance?.SetPaused(!GameState.Instance.Paused);
		row.AddChild(_play);

		_speed = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "Game speed (+ / -)" };
		if (BodyFont != null)
			_speed.AddThemeFontOverride("font", BodyFont);
		_speed.AddThemeFontSizeOverride("font_size", 14);
		_speed.AddThemeColorOverride("font_color", HudStyle.Text);
		for (int s = GameState.MinSpeed; s <= GameState.MaxSpeed; s++)
			_speed.AddItem($"{s}x", s);
		_speed.ItemSelected += index => GameState.Instance?.SetSpeed(_speed.GetItemId((int)index));
		row.AddChild(_speed);

		if (GameState.Instance == null)
			return;
		GameState.Instance.DayAdvanced += RefreshDate;
		GameState.Instance.ClockChanged += RefreshClock;
		GameState.Instance.PhaseChanged += RefreshPhase;
		RefreshPhase();
		RefreshDate();
		RefreshClock();
	}

	public override void _ExitTree()
	{
		if (GameState.Instance == null)
			return;
		GameState.Instance.DayAdvanced -= RefreshDate;
		GameState.Instance.ClockChanged -= RefreshClock;
		GameState.Instance.PhaseChanged -= RefreshPhase;
	}

	/// <summary>Time stands still while the player picks a country.</summary>
	void RefreshPhase() => Visible = GameState.Instance.Phase == GamePhase.Playing;

	public override void _UnhandledKeyInput(InputEvent e)
	{
		GameState gs = GameState.Instance;
		if (gs == null || gs.Phase != GamePhase.Playing || e is not InputEventKey { Pressed: true, Echo: false } key)
			return;
		switch (key.Keycode)
		{
			case Key.Space:
				gs.SetPaused(!gs.Paused);
				break;
			case Key.Plus or Key.Equal or Key.KpAdd:
				gs.SetSpeed(gs.Speed + 1);
				break;
			case Key.Minus or Key.KpSubtract:
				gs.SetSpeed(gs.Speed - 1);
				break;
			default:
				return;
		}
		GetViewport().SetInputAsHandled();
	}

	void RefreshDate()
	{
		GameState gs = GameState.Instance;
		_date.Text = gs.DateText;
		// the same day in every calendar in use
		_date.TooltipText = string.Join("\n", gs.EnabledCalendars.Select(c => $"{c.Name}: {c.Format(gs.Date)}"));
	}

	void RefreshClock()
	{
		GameState gs = GameState.Instance;
		_play.ShowPlay = gs.Paused;
		_play.TooltipText = gs.Paused ? "Play (Space)" : "Pause (Space)";
		_speed.Select(_speed.GetItemIndex(gs.Speed));
		_date.AddThemeColorOverride("font_color", gs.Paused ? HudStyle.Muted : HudStyle.Gold);
	}

	/// <summary>A button drawing a play triangle or pause bars, so it doesn't depend on the font having the symbols.</summary>
	sealed partial class PlayPauseButton : Button
	{
		bool _showPlay = true;

		public bool ShowPlay
		{
			get => _showPlay;
			set
			{
				_showPlay = value;
				QueueRedraw();
			}
		}

		public override void _Draw()
		{
			Vector2 c = Size / 2f;
			float h = Mathf.Min(Size.X, Size.Y) * 0.22f;
			if (_showPlay)
				DrawColoredPolygon(new[] { c + new Vector2(-h * 0.8f, -h), c + new Vector2(h, 0), c + new Vector2(-h * 0.8f, h) }, HudStyle.Text);
			else
			{
				DrawRect(new Rect2(c + new Vector2(-h * 0.85f, -h), new Vector2(h * 0.6f, h * 2f)), HudStyle.Text);
				DrawRect(new Rect2(c + new Vector2(h * 0.25f, -h), new Vector2(h * 0.6f, h * 2f)), HudStyle.Text);
			}
		}
	}
}
