using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.UI;

/// <summary>
/// The selected army: where it is or where it marches, its regiments by kind, and its orders: split,
/// merge, leave a garrison, disband, give battle to the nomads whose lands it stands in.
/// </summary>
public partial class ArmyPanel : PanelContainer
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }

	VBoxContainer _box;

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", HudStyle.Panel());
		MouseFilter = MouseFilterEnum.Stop;
		_box = new VBoxContainer();
		_box.AddThemeConstantOverride("separation", 5);
		AddChild(_box);
		Visible = false;
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.SelectedArmyChanged += OnSelected;
		gs.ArmiesChanged += Refresh;
		gs.ArmiesMoved += Refresh;
		gs.MonthAdvanced += Refresh;
		gs.TribesChanged += Refresh;
	}

	public override void _ExitTree()
	{
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.SelectedArmyChanged -= OnSelected;
		gs.ArmiesChanged -= Refresh;
		gs.ArmiesMoved -= Refresh;
		gs.MonthAdvanced -= Refresh;
		gs.TribesChanged -= Refresh;
	}

	void OnSelected(int id) => Refresh();

	static string Regiments(int n) => n == 1 ? "1 regiment" : $"{n} regiments";

	void Refresh()
	{
		GameState gs = GameState.Instance;
		Army a = gs.GetArmy(gs.SelectedArmyId);
		Visible = a != null && gs.Phase == GamePhase.Playing;
		if (!Visible)
			return;
		foreach (Node child in _box.GetChildren())
		{
			_box.RemoveChild(child);
			child.QueueFree();
		}
		bool ours = a.OwnerTag == gs.PlayerTag;
		Country owner = gs.GetCountry(a.OwnerTag);
		Province here = gs.GetProvince(a.ProvinceId);

		var head = new HBoxContainer();
		head.AddThemeConstantOverride("separation", 10);
		_box.AddChild(head);
		var flag = new TextureRect { Texture = HudStyle.Texture(owner?.CurrentFlag?.ImagePath) };
		var frame = HudStyle.Framed(flag);
		frame.CustomMinimumSize = new Vector2(42, 28);
		frame.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		head.AddChild(frame);
		var title = HudStyle.Title($"{a.Name}", TitleFont, 20);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		head.AddChild(title);
		var close = HudStyle.Button("✕", BodyFont);
		close.Pressed += () => gs.SelectArmy(0);
		head.AddChild(close);

		string where = a.Moving
			? $"Marching to {gs.GetProvince(a.Destination)?.Name}: {gs.DaysToArrive(a)} days (now near {here?.Name})"
			: $"In {here?.Name}";
		_box.AddChild(HudStyle.Body($"{where} · {MilitaryRules.Speed(a):0} km a day", BodyFont, 14));
		_box.AddChild(HudStyle.Body($"{a.Regiments.Count} regiments, {a.Men:N0} of {a.Regiments.Count * PopGroup.PeoplePerUnit:N0} men · {MilitaryRules.Upkeep(a):0.0} gold a month",
			BodyFont, 13, HudStyle.Muted));

		// the regiments by kind
		var grid = new GridContainer { Columns = 3 };
		grid.AddThemeConstantOverride("h_separation", 14);
		_box.AddChild(grid);
		foreach (var kind in a.Regiments.GroupBy(r => r.Type).OrderBy(g => g.Key.Category))
		{
			grid.AddChild(HudStyle.Body(kind.Key.Category.ToString(), BodyFont, 13, HudStyle.Gold));
			var name = HudStyle.Body($"{kind.Count()} × {kind.Key.Name}", BodyFont, 14);
			name.TooltipText = $"{kind.Key.Description}\nAttack {kind.Key.Attack:0.0#} · defense {kind.Key.Defense:0.0#} · {kind.Key.Speed:0} km a day";
			name.MouseFilter = MouseFilterEnum.Pass;
			grid.AddChild(name);
			double strength = kind.Average(r => r.Strength);
			grid.AddChild(HudStyle.Body($"{strength:P0} strength", BodyFont, 13, strength > 0.7 ? HudStyle.Muted : HudStyle.Bad));
		}
		int fierce = a.Regiments.Count(r => r.Fierce);
		int women = a.Regiments.Count(r => r.Sex == Sex.Female);
		if (fierce > 0 || women > 0)
			_box.AddChild(HudStyle.Body((fierce > 0 ? $"{Regiments(fierce)} of tribesmen fight fiercely. " : "") + (women > 0 ? $"{Regiments(women)} of women." : ""), BodyFont, 12, HudStyle.Muted));
		int engines = a.Count(UnitCategory.Siege), foot = a.Count(UnitCategory.Foot);
		if (engines > foot)
			_box.AddChild(HudStyle.Body($"Only {foot} of {engines} siege regiments are screened by foot: the rest add nothing to a battle, and are lost if it is lost.", BodyFont, 12, HudStyle.Bad));

		if (!ours)
			return;
		var orders = new HFlowContainer();
		orders.AddThemeConstantOverride("h_separation", 6);
		_box.AddChild(orders);
		void Order(string text, string tip, bool enabled, System.Action action)
		{
			var b = HudStyle.Button(text, BodyFont, 13);
			b.TooltipText = tip;
			b.Disabled = !enabled;
			b.Pressed += action;
			orders.AddChild(b);
		}
		int others = gs.ArmiesIn(a.ProvinceId).Count(o => o != a && o.OwnerTag == a.OwnerTag);
		if (a.Moving)
			Order("Halt", "Stop at the next province", true, () => gs.MoveArmy(a.Id, a.Path[0], out _));
		Order("Split", "Split the army in two", !a.Moving && a.Regiments.Count >= 2, () => gs.SplitArmy(a.Id));
		Order("Merge", others > 0 ? $"The {others} other armies here join this one" : "No other army of ours here", !a.Moving && others > 0, () => gs.MergeArmies(a.Id));
		bool canGarrison = !a.Moving && here?.OwnerTag == gs.PlayerTag && here.Control != null;
		Order("Leave garrison", canGarrison ? $"A regiment stays behind to hold {here.Name}" : "Only in our own provinces", canGarrison, () => gs.GarrisonFromArmy(a.ProvinceId, a.Id));
		Order("Disband", "Send the survivors home to their fields", true, () => gs.DisbandArmy(a.Id));

		// nomads here: give them battle
		Tribe t = gs.TribeOf(here);
		if (t != null && t.IsNomadic && !a.Moving && t.AlliedTag != gs.PlayerTag)
		{
			int mercs = TribeRules.Mercenaries(gs.Tribes.Values, gs.PlayerTag);
			bool can = TribeRules.CanSubjugate(t, gs.PlayerCountry, a, gs.Tribes.Values, gs.Provinces, mercs, out string why);
			var battle = HudStyle.Button(can ? $"Give battle to {t.TheName} ({TribeRules.SubjugationChance(t, a, gs.Provinces, mercs):P0})" : $"Give battle to {t.TheName}", BodyFont, 13);
			battle.Disabled = !can;
			battle.TooltipText = can
				? $"They have {TribeRules.Warriors(t, gs.Provinces)} fierce regiments" + (mercs > 0 ? $"; our {mercs} mercenary regiments go with us" : "")
					+ ". Win, and their lands are ours, a regiment left in each."
				: why;
			battle.Pressed += () => gs.SubjugateTribe(t.Id, a.Id, mercs);
			_box.AddChild(battle);
		}
		_box.AddChild(HudStyle.Body("Right-click a province to march there.", BodyFont, 12, HudStyle.Muted));
	}
}
