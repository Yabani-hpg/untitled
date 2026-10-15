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

	/// <summary>Who leads the army, and for the player's own, whom it could be given to.</summary>
	Control LeaderRow(Army a, Country owner, bool ours)
	{
		GameState gs = GameState.Instance;
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		Leader led = gs.LeaderOf(a);
		string kindWord = a.IsFleet ? "admiral" : "general";
		var text = HudStyle.Body(led != null ? $"Led by {led.Name} ({led.Pips})" : $"No {kindWord}", BodyFont, 13, led != null ? HudStyle.Gold : HudStyle.Muted);
		text.TooltipText = "Pips: fire / shock / manoeuvre / siege. Fire and shock sharpen the dice in battle, manoeuvre spares the men on the march, siege hastens sieges.";
		text.MouseFilter = MouseFilterEnum.Pass;
		row.AddChild(text);
		if (!ours || owner == null)
			return row;
		var free = gs.FreeLeaders(owner).Where(l => (l.Kind == LeaderKind.Admiral) == a.IsFleet).ToList();
		if (free.Count > 0 || led != null)
		{
			var pick = new OptionButton { FocusMode = FocusModeEnum.None };
			if (BodyFont != null) pick.AddThemeFontOverride("font", BodyFont);
			pick.AddThemeFontSizeOverride("font_size", 12);
			pick.AddItem(led != null ? "Relieve of command" : $"Give a {kindWord}...", 0);
			foreach (Leader l in free)
				pick.AddItem($"{l.Name} ({l.Pips})", l.Id);
			pick.ItemSelected += i =>
			{
				if (!gs.AssignLeader(a.Id, (int)pick.GetItemId((int)i), out string r))
					gs.Notify(r);
			};
			row.AddChild(pick);
		}
		string why = LeaderRules.RecruitObstacle(owner, gs.ProvincesOf(owner.Tag).Count(), a.IsFleet ? LeaderKind.Admiral : LeaderKind.General);
		var recruit = HudStyle.Button($"Recruit a {kindWord} ({LeaderRules.RecruitCost:0} gold)", BodyFont, 12);
		recruit.Disabled = why != null;
		recruit.TooltipText = why ?? $"A new {kindWord} takes command here; better ones come with tradition ({(a.IsFleet ? owner.NavalTradition : owner.ArmyTradition):0} now)";
		recruit.Pressed += () =>
		{
			if (gs.RecruitLeader(a.IsFleet ? LeaderKind.Admiral : LeaderKind.General, out string r))
				gs.AssignLeader(a.Id, owner.Leaders[^1].Id, out _);
			else
				gs.Notify(r);
		};
		row.AddChild(recruit);
		return row;
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

		Army ship = gs.GetArmy(a.AboardFleetId);
		string where = ship != null
			? $"Aboard the {ship.Name}" + (ship.Moving ? $", sailing to {gs.GetProvince(ship.Destination)?.Name}" : here.IsWater ? $", off the coast in the {here.Name}" : $", in harbour at {here.Name}")
			: a.Moving
			? $"{(a.IsFleet ? "Sailing" : "Marching")} to {gs.GetProvince(a.Destination)?.Name}: {gs.DaysToArrive(a)} days (now near {here?.Name})"
			: a.IsFleet && here?.IsWater == true ? $"At sea in the {here.Name}" : a.IsFleet ? $"In harbour at {here?.Name}" : $"In {here?.Name}";
		_box.AddChild(HudStyle.Body($"{where} · {MilitaryRules.Speed(a, owner):0} km a day", BodyFont, 14));
		_box.AddChild(LeaderRow(a, owner, ours));
		if (a.IsFleet)
			_box.AddChild(HudStyle.Body($"{a.Regiments.Count} ships · room for {a.Capacity} regiments, {gs.CargoUsed(a)} aboard · {MilitaryRules.Upkeep(a):0.0} gold a month",
				BodyFont, 13, HudStyle.Muted));
		else
			_box.AddChild(HudStyle.Body($"{a.Regiments.Count} regiments, {a.Men:N0} of {a.Regiments.Count * PopGroup.PeoplePerUnit:N0} men · {MilitaryRules.Upkeep(a):0.0} gold a month",
				BodyFont, 13, HudStyle.Muted));
		if (here != null && !here.IsWater && !a.IsFleet && ship == null)
		{
			double supply = TerrainRules.Supply(here), attrition = TerrainRules.Attrition(here, a.Regiments.Count);
			var land = HudStyle.Body($"{TerrainRules.Describe(here)}: the land feeds {supply:0} regiments"
				+ (attrition > 0 ? $"; we lose {attrition:P1} of our men a month here" : ""), BodyFont, 13,
				attrition >= 0.03 ? HudStyle.Bad : attrition > 0 ? HudStyle.Gold : HudStyle.Muted);
			land.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			land.CustomMinimumSize = new Vector2(480, 0);
			land.TooltipText = "Every month an army loses men to the land it stands in (heat, cold, fever), and to hunger if it is larger than the land and its people can feed.";
			land.MouseFilter = MouseFilterEnum.Pass;
			_box.AddChild(land);
		}

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
		int others = gs.ArmiesIn(a.ProvinceId).Count(o => o != a && o.OwnerTag == a.OwnerTag && o.IsFleet == a.IsFleet);
		if (ship != null)
		{
			// an army aboard a fleet: ashore into the harbour, or onto a coast by right-click
			bool inHarbour = !ship.Moving && here != null && !here.IsWater;
			Order("Go ashore", inHarbour ? $"Land at {here.Name}" : "Right-click a coast beside the fleet to land there", inHarbour, () => gs.Disembark(a.Id, here.Id, out _));
			_box.AddChild(HudStyle.Body("Right-click a coast beside the fleet to land there.", BodyFont, 12, HudStyle.Muted));
			return;
		}
		if (a.Moving)
			Order("Halt", "Stop at the next province", true, () => gs.MoveArmy(a.Id, a.Path[0], out _));
		Order("Split", $"Split the {(a.IsFleet ? "fleet" : "army")} in two", !a.Moving && a.Regiments.Count >= 2, () => gs.SplitArmy(a.Id));
		Order("Merge", others > 0 ? $"The {others} other {(a.IsFleet ? "fleets" : "armies")} here join this one" : $"No other {(a.IsFleet ? "fleet" : "army")} of ours here", !a.Moving && others > 0, () => gs.MergeArmies(a.Id));
		if (a.IsFleet)
		{
			bool inHarbour = !a.Moving && here != null && !here.IsWater;
			var cargo = gs.Cargo(a).ToList();
			Order("Land troops", cargo.Count == 0 ? "No troops aboard" : inHarbour ? $"The {cargo.Count} armies aboard go ashore at {here.Name}" : "Right-click a coast beside the fleet to land them there",
				cargo.Count > 0 && inHarbour, () => { foreach (Army c in cargo) gs.Disembark(c.Id, here.Id, out _); });
			Order("Scuttle", inHarbour || cargo.Count == 0 ? "Break up the fleet" : "The troops aboard would drown: land them first", inHarbour || cargo.Count == 0, () => gs.DisbandArmy(a.Id));
			foreach (Army c in cargo)
				_box.AddChild(HudStyle.Body($"Aboard: the {c.Name}, {c.Regiments.Count} regiments", BodyFont, 13, HudStyle.Gold));
			_box.AddChild(HudStyle.Body("Right-click a sea or one of our harbours to sail there; right-click a coast beside the fleet to land the troops aboard.", BodyFont, 12, HudStyle.Muted));
			return;
		}
		bool canGarrison = !a.Moving && here?.OwnerTag == gs.PlayerTag && here.Control != null;
		Order("Leave garrison", canGarrison ? $"A regiment stays behind to hold {here.Name}" : "Only in our own provinces", canGarrison, () => gs.GarrisonFromArmy(a.ProvinceId, a.Id));
		Order("Disband", "Send the survivors home to their fields", true, () => gs.DisbandArmy(a.Id));
		// a fleet of ours in this harbour or off this coast with room: board it
		Army ride = gs.Armies.Values.FirstOrDefault(f => f.IsFleet && f.OwnerTag == a.OwnerTag && !f.Moving && f.Capacity - gs.CargoUsed(f) >= a.Regiments.Count
			&& (f.ProvinceId == a.ProvinceId || here?.GetAdjacency(gs.GetProvince(f.ProvinceId))?.SharesBorder == true));
		if (ride != null && !a.Moving)
			Order($"Board the {ride.Name}", $"The army goes aboard ({ride.Capacity - gs.CargoUsed(ride)} regiments of room)", true, () => gs.Embark(a.Id, ride.Id, out _));

		// nomads here: give them battle
		Tribe t = gs.TribeOf(here);
		if (t != null && t.IsNomadic && !a.Moving && t.AlliedTag != gs.PlayerTag)
		{
			int mercs = TribeRules.Mercenaries(gs.Tribes.Values, gs.PlayerTag);
			bool can = TribeRules.CanSubjugate(t, gs.PlayerCountry, a, gs.Tribes.Values, gs.Provinces, mercs, out string why);
			var battle = HudStyle.Button(can ? $"Give battle to {t.TheName} ({TribeRules.SubjugationChance(t, gs.PlayerCountry, a, gs.Provinces, mercs):P0})" : $"Give battle to {t.TheName}", BodyFont, 13);
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
