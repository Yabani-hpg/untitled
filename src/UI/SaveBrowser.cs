using System;
using System.Collections.Generic;
using Godot;
using Untitled.Core;

namespace Untitled.UI;

/// <summary>
/// The save and load window: the list of saves (newest first) with a preview of the selected one, and
/// either a name field to save under (Save mode) or Load and Delete buttons (Load mode).
/// </summary>
public partial class SaveBrowser : PanelContainer
{
	public enum BrowserMode { Save, Load }

	[Signal] public delegate void ClosedEventHandler();
	/// <summary>Load mode: the player chose a save.</summary>
	[Signal] public delegate void LoadRequestedEventHandler(string path);

	public Font TitleFont { get; set; }
	public Font BodyFont { get; set; }
	/// <summary>Save mode: the picture stored with the save.</summary>
	public Image Screenshot { get; set; }

	BrowserMode _mode;
	List<SaveInfo> _saves = new();
	Label _title;
	ItemList _list;
	TextureRect _preview;
	TextureRect _flag;
	Label _details;
	LineEdit _name;
	Label _error;
	Button _action;
	Button _delete;
	ConfirmationDialog _confirm;

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", HudStyle.Panel(0.97f));
		CustomMinimumSize = new Vector2(860, 480);
		MouseFilter = MouseFilterEnum.Stop;
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 10);
		AddChild(box);
		_title = HudStyle.Title("", TitleFont, 24);
		box.AddChild(_title);

		var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 14);
		box.AddChild(body);
		_list = new ItemList { CustomMinimumSize = new Vector2(380, 340), SizeFlagsHorizontal = SizeFlags.ExpandFill, FixedIconSize = new Vector2I(36, 24) };
		if (BodyFont != null)
			_list.AddThemeFontOverride("font", BodyFont);
		_list.AddThemeFontSizeOverride("font_size", 15);
		_list.AddThemeColorOverride("font_color", HudStyle.Text);
		var listBg = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.35f) };
		listBg.SetContentMarginAll(6);
		_list.AddThemeStyleboxOverride("panel", listBg);
		_list.ItemSelected += OnSelected;
		_list.ItemActivated += index => { if (_mode == BrowserMode.Load) LoadSelected(); };
		body.AddChild(_list);

		var side = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
		side.AddThemeConstantOverride("separation", 8);
		body.AddChild(side);
		_preview = new TextureRect();
		var previewFrame = HudStyle.Framed(_preview);
		previewFrame.CustomMinimumSize = new Vector2(400, 225);
		side.AddChild(previewFrame);
		var info = new HBoxContainer();
		info.AddThemeConstantOverride("separation", 10);
		side.AddChild(info);
		_flag = new TextureRect();
		var flagFrame = HudStyle.Framed(_flag);
		flagFrame.CustomMinimumSize = new Vector2(72, 48);
		flagFrame.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		info.AddChild(flagFrame);
		_details = HudStyle.Body("", BodyFont, 14);
		info.AddChild(_details);

		var bottom = new HBoxContainer();
		bottom.AddThemeConstantOverride("separation", 10);
		box.AddChild(bottom);
		_name = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill, PlaceholderText = "Save name" };
		if (BodyFont != null)
			_name.AddThemeFontOverride("font", BodyFont);
		_name.AddThemeFontSizeOverride("font_size", 16);
		_name.TextSubmitted += _ => SaveNamed();
		bottom.AddChild(_name);
		_error = HudStyle.Body("", BodyFont, 13, HudStyle.Bad);
		_error.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bottom.AddChild(_error);
		_delete = HudStyle.Button("Delete", BodyFont, 15);
		_delete.Pressed += DeleteSelected;
		bottom.AddChild(_delete);
		_action = HudStyle.Button("", BodyFont, 15);
		_action.Pressed += () => { if (_mode == BrowserMode.Save) SaveNamed(); else LoadSelected(); };
		bottom.AddChild(_action);
		var cancel = HudStyle.Button("Cancel", BodyFont, 15);
		cancel.Pressed += Close;
		bottom.AddChild(cancel);

		_confirm = new ConfirmationDialog { OkButtonText = "Yes", CancelButtonText = "No" };
		AddChild(_confirm);
		Visible = false;
	}

	public void Open(BrowserMode mode)
	{
		_mode = mode;
		bool save = mode == BrowserMode.Save;
		_title.Text = save ? "Save Game" : "Load Game";
		_action.Text = save ? "Save" : "Load";
		_name.Visible = save;
		_delete.Visible = !save;
		_error.Visible = !save;
		_error.Text = "";
		if (save && GameState.Instance != null)
			_name.Text = SaveGames.SuggestName(GameState.Instance);
		Fill();
		Visible = true;
		if (save)
			_name.GrabFocus();
	}

	public void Close()
	{
		Visible = false;
		EmitSignal(SignalName.Closed);
	}

	void Fill()
	{
		_saves = SaveGames.List();
		_list.Clear();
		foreach (SaveInfo s in _saves)
		{
			int i = _list.AddItem($"{s.Name}   ·   {s.CountryName}, {s.DateText}", HudStyle.Texture(s.FlagImage));
			_list.SetItemTooltip(i, $"Saved {s.SavedAt}");
		}
		if (_saves.Count > 0 && _mode == BrowserMode.Load)
		{
			_list.Select(0);
			OnSelected(0);
		}
		else
			ShowDetails(null);
		_action.Disabled = _mode == BrowserMode.Load && _saves.Count == 0;
		_delete.Disabled = _saves.Count == 0;
	}

	void OnSelected(long index)
	{
		SaveInfo s = index >= 0 && index < _saves.Count ? _saves[(int)index] : null;
		ShowDetails(s);
		if (s != null && _mode == BrowserMode.Save)
			_name.Text = s.Name;
	}

	void ShowDetails(SaveInfo s)
	{
		if (s == null && _mode == BrowserMode.Save && GameState.Instance?.PlayerCountry is { } player)
		{
			// saving: the game as it is now
			GameState gs = GameState.Instance;
			_preview.Texture = Screenshot != null ? ImageTexture.CreateFromImage(Screenshot) : null;
			_flag.Texture = HudStyle.Texture(player.CurrentFlag?.ImagePath);
			_details.Text = $"{player.Name}\n{player.RulerTitle} {player.Ruler?.Name}\n{gs.DateText}";
			return;
		}
		_preview.Texture = s != null ? SaveGames.LoadThumbnail(s) : null;
		_flag.Texture = HudStyle.Texture(s?.FlagImage);
		_details.Text = s == null
			? (_mode == BrowserMode.Load ? "No saved games yet." : "")
			: $"{s.CountryName}\n{s.RulerName}\n{s.DateText}\nSaved {s.SavedAt}";
	}

	SaveInfo Selected => _list.GetSelectedItems() is { Length: > 0 } sel && sel[0] < _saves.Count ? _saves[sel[0]] : null;

	void SaveNamed()
	{
		string name = _name.Text.Trim();
		if (SaveGames.Exists(name))
			Confirm($"Overwrite the save '{name}'?", () => DoSave(name));
		else
			DoSave(name);
	}

	void DoSave(string name)
	{
		if (SaveGames.Save(GameState.Instance, name, Screenshot, out string error))
			Close();
		else
		{
			_error.Visible = true;
			_error.Text = error;
		}
	}

	void LoadSelected()
	{
		SaveInfo s = Selected;
		if (s != null)
			EmitSignal(SignalName.LoadRequested, s.Path);
	}

	void DeleteSelected()
	{
		SaveInfo s = Selected;
		if (s != null)
			Confirm($"Delete the save '{s.Name}'?", () => { SaveGames.Delete(s); Fill(); });
	}

	Action _onConfirm;

	void Confirm(string text, Action then)
	{
		_confirm.DialogText = text;
		if (_onConfirm != null)
			_confirm.Confirmed -= _onConfirm;
		_onConfirm = then;
		_confirm.Confirmed += _onConfirm;
		_confirm.PopupCentered();
	}

	/// <summary>Shows a load error in the browser.</summary>
	public void ShowError(string error)
	{
		_error.Visible = true;
		_error.Text = error;
	}
}
