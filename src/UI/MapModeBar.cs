using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.UI;

/// <summary>
/// Map mode buttons above the minimap: Natural, Political, Culture and Resources (food or deposits),
/// with a legend of the colours in use for the culture and resource modes.
/// </summary>
public partial class MapModeBar : VBoxContainer
{
	[Export] public Font BodyFont { get; set; }

	readonly Dictionary<MapMode, Button> _buttons = new();
	HBoxContainer _resourceRow;
	PanelContainer _legend;
	VBoxContainer _legendBox;

	public override void _Ready()
	{
		Alignment = AlignmentMode.End;
		MouseFilter = MouseFilterEnum.Ignore;
		AddThemeConstantOverride("separation", 4);

		_legend = new PanelContainer { MouseFilter = MouseFilterEnum.Stop, SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
		_legend.AddThemeStyleboxOverride("panel", HudStyle.Panel(0.88f));
		_legendBox = new VBoxContainer();
		_legendBox.AddThemeConstantOverride("separation", 2);
		_legend.AddChild(_legendBox);
		AddChild(_legend);

		_resourceRow = new HBoxContainer { Alignment = AlignmentMode.End };
		AddChild(_resourceRow);
		AddModeButton(_resourceRow, MapMode.Food, "Food", "Resources: what each province farms or herds");
		AddModeButton(_resourceRow, MapMode.Deposits, "Deposits", "Resources: each province's non-renewable deposit");

		var row = new HBoxContainer { Alignment = AlignmentMode.End };
		AddChild(row);
		AddModeButton(row, MapMode.Natural, "Natural", "The land as it is");
		AddModeButton(row, MapMode.Political, "Political", "Who owns each province");
		AddModeButton(row, MapMode.Culture, "Culture", "The main culture of each province");
		AddModeButton(row, MapMode.Terrain, "Terrain", "Each province's biome, with hills, mountains and impassable peaks in stripes");
		var resources = HudStyle.Button("Resources", BodyFont, 13);
		resources.TooltipText = "Food and non-renewable deposits";
		resources.ToggleMode = true;
		resources.Pressed += () =>
		{
			GameState gs = GameState.Instance;
			gs?.SetMapMode(gs.MapMode is MapMode.Food or MapMode.Deposits ? MapMode.Political : MapMode.Food);
		};
		_buttons[(MapMode)(-1)] = resources;
		row.AddChild(resources);

		if (GameState.Instance == null)
			return;
		GameState.Instance.MapModeChanged += Refresh;
		Refresh();
	}

	public override void _ExitTree()
	{
		if (GameState.Instance != null)
			GameState.Instance.MapModeChanged -= Refresh;
	}

	void AddModeButton(HBoxContainer row, MapMode mode, string text, string tooltip)
	{
		var b = HudStyle.Button(text, BodyFont, 13);
		b.TooltipText = tooltip;
		b.ToggleMode = true;
		b.Pressed += () => GameState.Instance?.SetMapMode(mode);
		_buttons[mode] = b;
		row.AddChild(b);
	}

	void Refresh()
	{
		MapMode mode = GameState.Instance.MapMode;
		bool resources = mode is MapMode.Food or MapMode.Deposits;
		foreach (var (m, b) in _buttons)
			b.SetPressedNoSignal(m == mode || (int)m == -1 && resources);
		_resourceRow.Visible = resources;
		FillLegend(mode);
	}

	void FillLegend(MapMode mode)
	{
		foreach (Node child in _legendBox.GetChildren())
			child.QueueFree();
		Definitions defs = GameState.Instance.Definitions;
		IEnumerable<(string Name, Color Color)> entries = mode switch
		{
			MapMode.Culture => defs.Cultures.Values.Select(c => (c.Name, c.Color)),
			MapMode.Food => defs.Resources.Values.Where(r => r.Category == ResourceCategory.Food).Select(r => (r.Name, r.Color)),
			MapMode.Deposits => defs.Resources.Values.Where(r => r.Category == ResourceCategory.NonRenewable).Select(r => (r.Name, r.Color)),
			MapMode.Terrain => defs.Biomes.Select(b => (b.Name, b.Color))
				.Concat(defs.Reliefs.Where(r => Untitled.Map.MapModes.ReliefStripe(r) != null)
					.Select(r => ($"{r.Name} (stripes)", Untitled.Map.MapModes.ReliefStripe(r).Value))),
			_ => Enumerable.Empty<(string, Color)>(),
		};
		var list = entries.ToList();
		_legend.Visible = list.Count > 0;
		var grid = new GridContainer { Columns = list.Count > 6 ? 2 : 1 };
		grid.AddThemeConstantOverride("h_separation", 14);
		_legendBox.AddChild(grid);
		foreach (var (name, color) in list)
		{
			var entry = new HBoxContainer();
			entry.AddChild(HudStyle.Swatch(color, 11));
			entry.AddChild(HudStyle.Body(name, BodyFont, 12));
			grid.AddChild(entry);
		}
	}
}
