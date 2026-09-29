using Godot;
using Untitled.Core;

namespace Untitled.UI;

/// <summary>The date, and a button that advances the game by a month (nomads migrate, populations build).</summary>
public partial class DateBar : PanelContainer
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }

	Label _date;

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", HudStyle.Panel());
		MouseFilter = MouseFilterEnum.Stop;
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);
		AddChild(row);
		_date = HudStyle.Title("", TitleFont, 18);
		_date.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		row.AddChild(_date);
		var next = HudStyle.Button("Next month ▸", BodyFont);
		next.TooltipText = "Advance a month: nomads move to better pasture, populations put up buildings";
		next.Pressed += () => GameState.Instance?.AdvanceMonth();
		row.AddChild(next);

		if (GameState.Instance == null)
			return;
		GameState.Instance.MonthAdvanced += Refresh;
		Refresh();
	}

	public override void _ExitTree()
	{
		if (GameState.Instance != null)
			GameState.Instance.MonthAdvanced -= Refresh;
	}

	void Refresh() => _date.Text = GameState.Instance.DateText;
}
