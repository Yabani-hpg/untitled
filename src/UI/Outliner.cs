using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.UI;

/// <summary>
/// The outliner at the right edge: alerts that want the player's eye (events waiting, a peace offered, a
/// coalition, low stability, an empty treasury, idle merchants, decisions open), then our armies and
/// fleets, our wars, and our provinces under siege. Each line is a shortcut to the thing itself.
/// </summary>
public partial class Outliner : PanelContainer
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }
	[Export] public NodePath CameraRigPath { get; set; }
	[Export] public NodePath CountryPanelPath { get; set; }
	[Export] public NodePath DiplomacyPanelPath { get; set; }

	VBoxContainer _box;
	Button _fold;
	bool _folded;
	double _sinceRefresh;

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", HudStyle.Panel(0.9f));
		MouseFilter = MouseFilterEnum.Stop;
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 4);
		AddChild(root);
		var head = new HBoxContainer();
		root.AddChild(head);
		var title = HudStyle.Title("Outliner", TitleFont, 16);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		head.AddChild(title);
		_fold = HudStyle.Button("▾", BodyFont, 12);
		_fold.Pressed += () => { _folded = !_folded; _fold.Text = _folded ? "▸" : "▾"; Refresh(); };
		head.AddChild(_fold);
		var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(292, 0) };
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		root.AddChild(scroll);
		_box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_box.AddThemeConstantOverride("separation", 2);
		scroll.AddChild(_box);
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.PhaseChanged += Refresh;
		gs.ArmiesChanged += Refresh;
		gs.WarsChanged += Refresh;
		gs.EventPending += Refresh;
		gs.DiplomacyChanged += Refresh;
		gs.DecisionsChanged += Refresh;
		gs.SelectedArmyChanged += OnSelected;
		Refresh();
	}

	public override void _ExitTree()
	{
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.PhaseChanged -= Refresh;
		gs.ArmiesChanged -= Refresh;
		gs.WarsChanged -= Refresh;
		gs.EventPending -= Refresh;
		gs.DiplomacyChanged -= Refresh;
		gs.DecisionsChanged -= Refresh;
		gs.SelectedArmyChanged -= OnSelected;
	}

	void OnSelected(int id) => Refresh();

	public override void _Process(double delta)
	{
		// marching armies and sieges move daily: a steady refresh keeps the lines true
		_sinceRefresh += delta;
		if (_sinceRefresh >= 1.0 && Visible)
		{
			_sinceRefresh = 0;
			Refresh();
		}
	}

	void Refresh()
	{
		GameState gs = GameState.Instance;
		Country c = gs?.PlayerCountry;
		// it steps aside while one of the big windows is open
		bool windowOpen = CountryPanel()?.Visible == true || Diplomacy()?.Visible == true || GetNodeOrNull<Control>("../LedgerPanel")?.Visible == true || GetNodeOrNull<Control>("../ResearchPanel")?.Visible == true;
		Visible = c != null && gs.Phase == GamePhase.Playing && !windowOpen;
		foreach (Node child in _box.GetChildren())
		{
			_box.RemoveChild(child);
			child.QueueFree();
		}
		if (!Visible || _folded)
			return;
		var alerts = new List<(string Text, Color Color, string Tip, System.Action Action)>();
		if (gs.PendingEvents.Count > 0)
			alerts.Add(($"{gs.PendingEvents.Count} event{(gs.PendingEvents.Count > 1 ? "s" : "")} await our answer", HudStyle.Gold, "The game waits until each is answered", null));
		foreach (War w in gs.WarsOf(c.Tag).Where(w => w.Offer != null))
			alerts.Add(($"Peace offered in the {w.Name(gs.Countries)}", HudStyle.Gold, "Answer it in the war window", () => Diplomacy()?.Open($"war:{w.Id}")));
		var coalition = gs.CoalitionAgainst(c.Tag).ToList();
		if (coalition.Count > 0)
			alerts.Add(($"A coalition of {coalition.Count} stands against us", HudStyle.Bad, string.Join(", ", coalition.Select(m => m.Name)), () => Diplomacy()?.Open(coalition[0].Tag)));
		if (c.Stability < 0)
			alerts.Add(($"Stability {c.Stability:+0;-0}", HudStyle.Bad, "Unrest grows and tax falls; raise it in the country window", () => CountryPanel()?.Toggle(0)));
		foreach (Estate e in gs.Definitions.Estates.Where(e => EstateRules.IsDisloyal(c, e)))
			alerts.Add(($"{e.Name} are disloyal", HudStyle.Bad, "Grant them a privilege in the Estates tab", () => CountryPanel()?.Toggle(5)));
		if (c.IsBankrupt(gs.Date))
			alerts.Add(("Bankrupt", HudStyle.Bad, $"Tax suffers and no one lends until {gs.Definitions.DefaultCalendar.Format(c.BankruptUntil)}", () => CountryPanel()?.Toggle(0)));
		if (c.Gold < 0)
			alerts.Add(($"The treasury is in debt: {c.Gold:0} gold", HudStyle.Bad, "Mercenaries go unpaid", () => CountryPanel()?.Toggle(0)));
		else if (c.LastLedger.Balance < 0)
			alerts.Add(($"We lose {-c.LastLedger.Balance:0.0} gold a month", HudStyle.Bad, "Armies, garrisons and mercenaries cost more than we take", () => CountryPanel()?.Toggle(0)));
		if (c.WarExhaustion >= 5)
			alerts.Add(($"War exhaustion {c.WarExhaustion:0.0}", HudStyle.Bad, "The people tire of war: unrest and lower tax", () => CountryPanel()?.Toggle(0)));
		if (gs.FreeMerchants(c) > 0)
			alerts.Add(($"{gs.FreeMerchants(c)} merchant{(gs.FreeMerchants(c) > 1 ? "s" : "")} idle", HudStyle.Muted, "Send them to a trade node", () => CountryPanel()?.Toggle(4)));
		int decisions = gs.VisibleDecisions(c).Count(d => gs.CanTakeDecision(c, d, out _));
		if (decisions > 0)
			alerts.Add(($"{decisions} decision{(decisions > 1 ? "s" : "")} we can take", HudStyle.Gold, "See the Decisions tab", () => CountryPanel()?.Toggle(3)));
		if (gs.AtWar(c.Tag) && gs.AvailableLevies(c) > 0)
			alerts.Add(($"{gs.AvailableLevies(c)} regiments of levies can be raised", HudStyle.Muted, "The Military tab calls them up", () => CountryPanel()?.Toggle(2)));
		int diplomats = gs.FreeDiplomats(c);
		if (diplomats > 0 && !gs.AtWar(c.Tag))
			alerts.Add(($"{diplomats} diplomat{(diplomats > 1 ? "s" : "")} idle", HudStyle.Muted, "Improve relations with a neighbour", () => Diplomacy()?.Toggle()));
		if (c.IdeaGroups.Count > 0 && c.IdeaGroups.Any(id => gs.BuyObstacle(c, gs.Definitions.GetIdeaGroup(id)) == null))
			alerts.Add(("An idea can be bought", HudStyle.Gold, "Idea points enough for the next idea", () => GetNodeOrNull<ResearchPanel>("../ResearchPanel")?.ShowIdeas()));
		else if (c.IdeaGroups.Count < IdeaRules.Slots(c))
			alerts.Add(("An idea slot is free", HudStyle.Muted, "Take up an idea group", () => GetNodeOrNull<ResearchPanel>("../ResearchPanel")?.ShowIdeas()));
		int emptySlots = AdvisorRules.Categories.Count(cat => AdvisorRules.Hired(c, cat) == null && c.Candidates.Any(a => a.Type.Category == cat && gs.HireObstacle(c, a) == null));
		if (emptySlots > 0)
			alerts.Add(($"{emptySlots} advisor seat{(emptySlots > 1 ? "s" : "")} empty", HudStyle.Muted, "Candidates wait in the Court tab", () => CountryPanel()?.Toggle(6)));
		if (ReformRules.NextTier(c, gs.Definitions) is ReformTier tier && c.ReformProgress >= ReformRules.Cost(tier.Index))
			alerts.Add(($"A reform can be enacted: {tier.Name}", HudStyle.Gold, "Choose it in the Laws tab", () => CountryPanel()?.Toggle(1)));
		foreach (string id in c.ActiveDisasters.Keys)
			alerts.Add(($"Disaster: {gs.Definitions.GetDisaster(id)?.Name}", HudStyle.Bad, gs.Definitions.GetDisaster(id)?.Description, () => CountryPanel()?.Toggle(0)));
		foreach (var (id, progress) in c.DisasterProgress.Where(kv => kv.Value >= 25))
			alerts.Add(($"{gs.Definitions.GetDisaster(id)?.Name} brews: {progress:0}%", HudStyle.Gold, gs.Definitions.GetDisaster(id)?.Description, () => CountryPanel()?.Toggle(0)));
		if (c.ResearchQueue.Count == 0)
			alerts.Add(("Nothing is being researched", HudStyle.Bad, "Choose a technology", () => GetNodeOrNull<ResearchPanel>("../ResearchPanel")?.Toggle()));
		if (alerts.Count > 0)
		{
			Header("Alerts");
			foreach (var (text, color, tip, action) in alerts)
				Row(text, color, tip, action);
		}

		if (c.Fabricating.Count > 0)
		{
			Header($"Claims being fabricated ({c.Fabricating.Count})");
			foreach (var (id, progress) in c.Fabricating)
			{
				Province p = gs.GetProvince(id);
				string owner = p?.OwnerTag;
				Row($"{p?.Name} ({gs.GetCountry(owner)?.Name}) · {progress:0}%", HudStyle.Text, "Open the diplomacy window", () => Diplomacy()?.Open(owner));
			}
		}
		var armies = gs.ArmiesOf(c.Tag).OrderBy(a => a.IsFleet).ThenBy(a => a.Name).ToList();
		Header($"Armies and fleets ({armies.Count})");
		if (armies.Count == 0)
			Row("None under arms", HudStyle.Muted, "Levies are raised in the Military tab", () => CountryPanel()?.Toggle(2));
		foreach (Army a in armies)
		{
			Province at = gs.GetProvince(a.ProvinceId);
			Army ship = gs.GetArmy(a.AboardFleetId);
			string state = ship != null ? $"aboard the {ship.Name}" : a.Moving ? $"to {gs.GetProvince(a.Destination)?.Name}" : at?.Name;
			string text = $"{(a.IsFleet ? "⚓ " : "")}{a.Name} · {a.Regiments.Count} · {state}";
			int id = a.Id;
			Row(text, a.Id == gs.SelectedArmyId ? HudStyle.Gold : HudStyle.Text, a.IsFleet ? $"{a.Regiments.Count} ships" : $"{a.Men:N0} men", () => { gs.SelectArmy(id); Focus(a.ProvinceId); });
		}

		var wars = gs.WarsOf(c.Tag).ToList();
		if (wars.Count > 0)
		{
			Header($"Wars ({wars.Count})");
			foreach (War w in wars)
			{
				double score = w.Score * w.SideOf(c.Tag);
				Row($"{w.Name(gs.Countries)} · {score:+0;-0}", score >= 0 ? HudStyle.Good : HudStyle.Bad, "Open the war window", () => Diplomacy()?.Open($"war:{w.Id}"));
			}
		}
		var sieges = gs.ProvincesOf(c.Tag).Where(p => p.OccupierTag != null || p.SiegerTag != null && p.SiegerTag != c.Tag).ToList();
		if (sieges.Count > 0)
		{
			Header($"Our land under attack ({sieges.Count})");
			foreach (Province p in sieges.Take(8))
			{
				string text = p.OccupierTag != null ? $"{p.Name} · occupied by {gs.GetCountry(p.OccupierTag)?.Adjective}" : $"{p.Name} · siege {p.SiegeProgress / WarRules.SiegeNeeded(p):P0}";
				int id = p.Id;
				Row(text, HudStyle.Bad, "Go there", () => { gs.SelectProvince(id); Focus(id); });
			}
		}
	}

	void Header(string text)
	{
		var l = HudStyle.Body(text, BodyFont, 12, HudStyle.Gold);
		l.AddThemeConstantOverride("margin_top", 4);
		_box.AddChild(l);
	}

	void Row(string text, Color color, string tip, System.Action action)
	{
		var b = HudStyle.Button(text, BodyFont, 12);
		b.Alignment = HorizontalAlignment.Left;
		b.ClipText = true;
		b.CustomMinimumSize = new Vector2(280, 0);
		b.AddThemeColorOverride("font_color", color);
		b.TooltipText = tip ?? "";
		b.Disabled = action == null;
		if (action != null)
			b.Pressed += () => action();
		_box.AddChild(b);
	}

	void Focus(int provinceId) => MapCamera.FocusProvince(GetNodeOrNull<Node3D>(CameraRigPath ?? new NodePath()), provinceId);
	CountryPanel CountryPanel() => GetNodeOrNull<CountryPanel>(CountryPanelPath ?? new NodePath());
	DiplomacyPanel Diplomacy() => GetNodeOrNull<DiplomacyPanel>(DiplomacyPanelPath ?? new NodePath());
}
