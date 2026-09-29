using System.Collections.Generic;
using Godot;
using Untitled.Core;

namespace Untitled.UI;

/// <summary>
/// The main menu's Load Game overlay (scenes/main_menu.tscn, Overlay/LoadOverlay): lists the saves with
/// the selected one's preview, country, date and ruler, and loads it into the map scene.
/// </summary>
public partial class MainMenuLoadOverlay : Control
{
	[Export(PropertyHint.File, "*.tscn")] public string GameScene { get; set; } = "res://scenes/main.tscn";

	ItemList _list;
	TextureRect _preview;
	Label _name;
	Label _date;
	Label _info;
	Button _load;
	Button _delete;
	ConfirmationDialog _confirm;
	List<SaveInfo> _saves = new();

	public override void _Ready()
	{
		const string row = "Panel/VBoxContainer/HBoxContainer";
		_list = GetNode<ItemList>($"{row}/SaveList");
		_preview = GetNode<TextureRect>($"{row}/Preview");
		_name = GetNode<Label>($"{row}/NameLabel");
		_date = GetNode<Label>($"{row}/DateLabel");
		_info = GetNode<Label>($"{row}/InfoLabel");
		_load = GetNode<Button>("Panel/VBoxContainer/HBoxContainer2/LoadBtn");
		_delete = GetNode<Button>("Panel/VBoxContainer/HBoxContainer2/DeleteBtn");
		var back = GetNode<Button>("Panel/VBoxContainer/HBoxContainer2/BackBtn");
		if (GetNodeOrNull<ColorRect>("Dim") is ColorRect dim)
			dim.Color = new Color(0, 0, 0, 0.6f);
		// a solid backing, so the menu doesn't show through the list
		var panel = GetNode<PanelContainer>("Panel");
		var bg = new StyleBoxFlat { BgColor = new Color(0.08f, 0.07f, 0.06f, 0.96f), BorderColor = new Color(0.62f, 0.49f, 0.26f) };
		bg.SetBorderWidthAll(2);
		bg.SetCornerRadiusAll(8);
		bg.SetContentMarginAll(16);
		panel.AddThemeStyleboxOverride("panel", bg);

		// the details go under the preview, in a column beside the list
		var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_preview.GetParent().AddChild(details);
		foreach (Control c in new Control[] { _preview, _name, _date, _info })
		{
			c.GetParent().RemoveChild(c);
			details.AddChild(c);
		}
		_preview.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_preview.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_list.CustomMinimumSize = new Vector2(420, 440);
		_list.FixedIconSize = new Vector2I(42, 28);
		_list.AddThemeFontSizeOverride("font_size", 18);
		foreach (Label l in new[] { _date, _info })
			l.AddThemeFontSizeOverride("font_size", 18);

		_list.ItemSelected += index => Show((int)index);
		_list.ItemActivated += index => LoadSelected();
		_load.Pressed += LoadSelected;
		_delete.Pressed += DeleteSelected;
		back.Pressed += Close;
		_confirm = new ConfirmationDialog { OkButtonText = "Yes", CancelButtonText = "No" };
		_confirm.Confirmed += () =>
		{
			if (Selected is SaveInfo s)
				SaveGames.Delete(s);
			Fill();
		};
		AddChild(_confirm);
	}

	/// <summary>Called by script/main_menu.gd.</summary>
	public void Open()
	{
		GetNodeOrNull<Control>("Dim")?.Show();
		Fill();
		Visible = true;
	}

	public void Close()
	{
		Visible = false;
		GetNodeOrNull<Control>("Dim")?.Hide();
	}

	public override void _UnhandledKeyInput(InputEvent e)
	{
		if (Visible && e is InputEventKey { Pressed: true, Keycode: Key.Escape })
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}

	void Fill()
	{
		_saves = SaveGames.List();
		_list.Clear();
		foreach (SaveInfo s in _saves)
			_list.AddItem($"{s.Name}", HudStyle.Texture(s.FlagImage));
		_load.Disabled = _delete.Disabled = _saves.Count == 0;
		if (_saves.Count > 0)
		{
			_list.Select(0);
			Show(0);
		}
		else
		{
			_preview.Texture = null;
			_name.Text = "No saved games";
			_date.Text = _info.Text = "";
		}
	}

	SaveInfo Selected => _list.GetSelectedItems() is { Length: > 0 } sel && sel[0] < _saves.Count ? _saves[sel[0]] : null;

	void Show(int index)
	{
		SaveInfo s = _saves[index];
		_preview.Texture = SaveGames.LoadThumbnail(s);
		_name.Text = s.CountryName;
		_date.Text = s.DateText;
		_info.Text = $"{s.RulerName}\nSaved {s.SavedAt}";
	}

	void LoadSelected()
	{
		if (Selected is not SaveInfo s)
			return;
		if (!SaveGames.Load(GameState.Instance, s.Path, out string error))
		{
			_info.Text = error;
			return;
		}
		GetTree().ChangeSceneToFile(GameScene);
	}

	void DeleteSelected()
	{
		if (Selected is not SaveInfo s)
			return;
		_confirm.DialogText = $"Delete the save '{s.Name}'?";
		_confirm.PopupCentered();
	}
}
