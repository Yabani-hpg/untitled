using Godot;
using Untitled.Core;

namespace Untitled.UI;

/// <summary>
/// The game menu, opened with Esc: resume, save, load, back to the main menu or quit. The game pauses
/// while it is open.
/// </summary>
public partial class GameMenu : Control
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }
	[Export(PropertyHint.File, "*.tscn")] public string MainMenuScene { get; set; } = "res://scenes/main_menu.tscn";

	PanelContainer _menu;
	SaveBrowser _browser;
	bool _wasPaused;

	public override void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		AddChild(new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });

		var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore };
		AddChild(center);
		_menu = new PanelContainer { CustomMinimumSize = new Vector2(300, 0) };
		_menu.AddThemeStyleboxOverride("panel", HudStyle.Panel(0.97f));
		center.AddChild(_menu);
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 10);
		_menu.AddChild(box);
		var title = HudStyle.Title("Game Menu", TitleFont, 24);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		box.AddChild(title);
		AddEntry(box, "Resume", Close);
		AddEntry(box, "Save Game", () => OpenBrowser(SaveBrowser.BrowserMode.Save));
		AddEntry(box, "Load Game", () => OpenBrowser(SaveBrowser.BrowserMode.Load));
		AddEntry(box, "Exit to Main Menu", () =>
		{
			Close();
			GetTree().ChangeSceneToFile(MainMenuScene);
		});
		AddEntry(box, "Exit Game", () => GetTree().Quit());

		_browser = new SaveBrowser { TitleFont = TitleFont, BodyFont = BodyFont };
		center.AddChild(_browser);
		_browser.Closed += () => _menu.Visible = true;
		_browser.LoadRequested += LoadGame;
	}

	void AddEntry(VBoxContainer box, string text, System.Action action)
	{
		var b = HudStyle.Button(text, BodyFont, 17);
		b.Pressed += action;
		box.AddChild(b);
	}

	public override void _UnhandledKeyInput(InputEvent e)
	{
		if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
			return;
		GameState gs = GameState.Instance;
		if (gs == null || gs.Phase != GamePhase.Playing)
			return;
		if (!Visible)
			Open();
		else if (_browser.Visible)
			_browser.Close();
		else
			Close();
		GetViewport().SetInputAsHandled();
	}

	public void Open()
	{
		GameState gs = GameState.Instance;
		// the picture a save keeps: the map as it is now, before the menu covers it
		_browser.Screenshot = GetViewport().GetTexture().GetImage();
		_wasPaused = gs.Paused;
		gs.SetPaused(true);
		_menu.Visible = true;
		_browser.Visible = false;
		Visible = true;
	}

	public void Close()
	{
		Visible = false;
		GameState.Instance?.SetPaused(_wasPaused);
	}

	void OpenBrowser(SaveBrowser.BrowserMode mode)
	{
		_menu.Visible = false;
		_browser.Open(mode);
	}

	void LoadGame(string path)
	{
		if (!SaveGames.Load(GameState.Instance, path, out string error))
		{
			_browser.ShowError(error);
			return;
		}
		// the map, borders and labels are built from the game state when the scene loads
		GetTree().ReloadCurrentScene();
	}
}
