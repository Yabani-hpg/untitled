using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.UI;

/// <summary>
/// The diplomacy window: every settled country with its opinion of us and ours of it, allies, truces and
/// wars, and what we can do: send a diplomat, propose or break an alliance, declare war; and the wars
/// we are in, with their score, battles and the peace table.
/// </summary>
public partial class DiplomacyPanel : PanelContainer
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }
	[Export] public NodePath CameraRigPath { get; set; }

	VBoxContainer _list;
	VBoxContainer _detail;
	Label _status;
	string _focusTag;
	readonly Dictionary<int, HashSet<int>> _peaceChoice = new();     // war id -> provinces ticked
	readonly Dictionary<int, double> _peaceGold = new();

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", HudStyle.Panel(0.97f));
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		var root = new VBoxContainer();
		root.AddThemeConstantOverride("separation", 8);
		AddChild(root);
		var header = new HBoxContainer();
		root.AddChild(header);
		var title = HudStyle.Title("Diplomacy", TitleFont, 26);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		header.AddChild(title);
		var close = HudStyle.Button("✕", BodyFont);
		close.Pressed += () => Visible = false;
		header.AddChild(close);

		var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		columns.AddThemeConstantOverride("separation", 10);
		root.AddChild(columns);
		var listScroll = new ScrollContainer { CustomMinimumSize = new Vector2(330, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_list.AddThemeConstantOverride("separation", 3);
		listScroll.AddChild(_list);
		columns.AddChild(listScroll);
		var detailScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_detail = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_detail.AddThemeConstantOverride("separation", 6);
		detailScroll.AddChild(_detail);
		columns.AddChild(detailScroll);
		_status = HudStyle.Body("", BodyFont, 13, HudStyle.Gold);
		root.AddChild(_status);

		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.DiplomacyChanged += Refresh;
		gs.WarsChanged += Refresh;
		gs.MonthAdvanced += Refresh;
		gs.PhaseChanged += Hide;
	}

	public override void _ExitTree()
	{
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.DiplomacyChanged -= Refresh;
		gs.WarsChanged -= Refresh;
		gs.MonthAdvanced -= Refresh;
		gs.PhaseChanged -= Hide;
	}

	/// <summary>Opens the window on a country (or the first war), or closes it.</summary>
	public void Toggle(string tag = null)
	{
		Visible = !Visible || tag != null && tag != _focusTag;
		if (tag != null)
			_focusTag = tag;
		_status.Text = "";
		Refresh();
	}

	public void Open(string tag)
	{
		Visible = true;
		_focusTag = tag;
		_status.Text = "";
		Refresh();
	}

	void Refresh()
	{
		GameState gs = GameState.Instance;
		Country us = gs?.PlayerCountry;
		if (!Visible || us == null)
			return;
		Clear(_list);
		Clear(_detail);
		// wars first, then every settled country, enemies and allies before the rest, by opinion
		foreach (War w in gs.WarsOf(us.Tag))
			_list.AddChild(WarRow(gs, us, w));
		var countries = gs.SettledCountries.Where(c => c != us)
			.OrderByDescending(c => gs.AtWarWith(us.Tag, c.Tag) ? 3 : us.Allies.Contains(c.Tag) ? 2 : gs.AreNeighbours(us.Tag, c.Tag) ? 1 : 0)
			.ThenByDescending(c => DiplomacyRules.Opinion(c, us.Tag)).ToList();
		if (_focusTag == null || gs.GetCountry(_focusTag) == null && !_focusTag.StartsWith("war:"))
			_focusTag = countries.FirstOrDefault()?.Tag;
		foreach (Country c in countries)
			_list.AddChild(CountryRow(gs, us, c));
		if (_focusTag != null && _focusTag.StartsWith("war:") && gs.GetWar(int.Parse(_focusTag[4..])) is War war)
			FillWar(gs, us, war);
		else if (gs.GetCountry(_focusTag) is Country them)
			FillCountry(gs, us, them);
	}

	Control WarRow(GameState gs, Country us, War w)
	{
		var b = HudStyle.Button($"⚔ {w.Name(gs.Countries)} · {w.Score * w.SideOf(us.Tag):+0;-0} {(w.Offer != null ? "· peace offered!" : "")}", BodyFont, 13);
		b.Alignment = HorizontalAlignment.Left;
		b.AddThemeColorOverride("font_color", HudStyle.Bad);
		b.Pressed += () => { _focusTag = $"war:{w.Id}"; Refresh(); };
		return b;
	}

	Control CountryRow(GameState gs, Country us, Country c)
	{
		var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
		var style = new StyleBoxFlat { BgColor = c.Tag == _focusTag ? new Color(0.4f, 0.3f, 0.15f, 0.8f) : new Color(0.2f, 0.15f, 0.09f, 0.6f) };
		style.SetCornerRadiusAll(4);
		style.SetContentMarginAll(4);
		row.AddThemeStyleboxOverride("panel", style);
		var box = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		box.AddThemeConstantOverride("separation", 8);
		row.AddChild(box);
		var flag = new TextureRect { Texture = HudStyle.Texture(c.CurrentFlag?.ImagePath), MouseFilter = MouseFilterEnum.Ignore };
		var frame = HudStyle.Framed(flag);
		frame.CustomMinimumSize = new Vector2(36, 24);
		frame.MouseFilter = MouseFilterEnum.Ignore;
		box.AddChild(frame);
		var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
		text.AddThemeConstantOverride("separation", -2);
		box.AddChild(text);
		var name = HudStyle.Body(c.Name, BodyFont, 14);
		name.MouseFilter = MouseFilterEnum.Ignore;
		text.AddChild(name);
		var standing = HudStyle.Body(Standing(gs, us, c), BodyFont, 11, HudStyle.Muted);
		standing.MouseFilter = MouseFilterEnum.Ignore;
		text.AddChild(standing);
		int o = DiplomacyRules.Opinion(c, us.Tag);
		var opinion = HudStyle.Body($"{o:+0;-0;0}", BodyFont, 14, o >= 0 ? HudStyle.Good : HudStyle.Bad);
		opinion.MouseFilter = MouseFilterEnum.Ignore;
		box.AddChild(opinion);
		string tag = c.Tag;
		row.GuiInput += e =>
		{
			if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			{
				_focusTag = tag;
				Refresh();
			}
		};
		return row;
	}

	static string Standing(GameState gs, Country us, Country c)
	{
		var parts = new List<string>();
		if (gs.AtWarWith(us.Tag, c.Tag)) parts.Add("at war with us");
		if (c.OverlordTag == us.Tag) parts.Add("our vassal");
		else if (us.OverlordTag == c.Tag) parts.Add("our overlord");
		else if (c.OverlordTag != null) parts.Add($"vassal of {gs.GetCountry(c.OverlordTag)?.Name}");
		if (us.Allies.Contains(c.Tag)) parts.Add("ally");
		if (DiplomacyRules.HasTruce(us, c.Tag, gs.Date)) parts.Add("truce");
		if (gs.AreNeighbours(us.Tag, c.Tag)) parts.Add("neighbour");
		if (us.Claims.Any(id => gs.Provinces[id]?.OwnerTag == c.Tag)) parts.Add("we have claims");
		if (us.Guarantees.Contains(c.Tag)) parts.Add("we guarantee them");
		if (c.Guarantees.Contains(us.Tag)) parts.Add("guarantees us");
		if (us.Marriages.ContainsKey(c.Tag)) parts.Add("royal marriage");
		if (us.Fabricating.Keys.Any(id => gs.Provinces[id]?.OwnerTag == c.Tag)) parts.Add("fabricating a claim");
		if (us.Rivals.Contains(c.Tag)) parts.Add("our rival");
		else if (c.Rivals.Contains(us.Tag)) parts.Add("counts us a rival");
		if (c.CoalitionAgainst == us.Tag) parts.Add("in a coalition against us");
		return parts.Count > 0 ? string.Join(" · ", parts) : $"{gs.ProvincesOf(c.Tag).Count()} provinces";
	}

	// ------------------------------------------------------------------------------------ a country

	void FillCountry(GameState gs, Country us, Country c)
	{
		var head = new HBoxContainer();
		head.AddThemeConstantOverride("separation", 12);
		_detail.AddChild(head);
		var flag = new TextureRect { Texture = HudStyle.Texture(c.CurrentFlag?.ImagePath) };
		var frame = HudStyle.Framed(flag);
		frame.CustomMinimumSize = new Vector2(96, 64);
		frame.SizeFlagsVertical = SizeFlags.ShrinkBegin;
		head.AddChild(frame);
		var portrait = new TextureRect { Texture = HudStyle.Texture(c.Ruler?.Portrait) };
		var pframe = HudStyle.Framed(portrait);
		pframe.CustomMinimumSize = new Vector2(80, 100);
		head.AddChild(pframe);
		var facts = new VBoxContainer();
		head.AddChild(facts);
		facts.AddChild(HudStyle.Title(c.Name, TitleFont, 22));
		facts.AddChild(HudStyle.Body($"{c.RulerTitle} {c.Ruler?.Name}, {c.Ruler?.Dynasty} · {c.Government}", BodyFont, 13, HudStyle.Muted));
		var owned = gs.ProvincesOf(c.Tag).ToList();
		facts.AddChild(HudStyle.Body($"{owned.Count} provinces · {owned.Sum(p => p.TotalUnits) * PopGroup.PeoplePerUnit:N0} people · capital {gs.CapitalText(c)}", BodyFont, 13));
		facts.AddChild(HudStyle.Body($"Strength {WarRules.Strength(c, gs.Provinces, gs.Armies.Values, gs.Date):0} regiments (ours {WarRules.Strength(us, gs.Provinces, gs.Armies.Values, gs.Date):0}) · prestige {c.Prestige:0}", BodyFont, 13));
		facts.AddChild(HudStyle.Body("Allies: " + (c.Allies.Count > 0 ? string.Join(", ", c.Allies.Select(t => gs.GetCountry(t)?.Name)) : "none")
			+ (c.OverlordTag != null ? $" · vassal of {gs.GetCountry(c.OverlordTag)?.Name}" : "")
			+ (gs.VassalsOf(c.Tag).Any() ? $" · vassals: {string.Join(", ", gs.VassalsOf(c.Tag).Select(v => v.Name))}" : ""), BodyFont, 13, HudStyle.Muted));
		var rivalsOf = c.Rivals.Select(t => gs.GetCountry(t)?.Name).Where(n => n != null).ToList();
		var coalition = gs.CoalitionAgainst(c.Tag).ToList();
		facts.AddChild(HudStyle.Body("Rivals: " + (rivalsOf.Count > 0 ? string.Join(", ", rivalsOf) : "none")
			+ (coalition.Count > 0 ? $" · a coalition of {coalition.Count} stands against them: {string.Join(", ", coalition.Select(m => m.Name))}" : "")
			+ (c.CoalitionAgainst != null ? $" · in the coalition against {gs.GetCountry(c.CoalitionAgainst)?.Name}" : ""), BodyFont, 13, HudStyle.Muted));
		var goTo = HudStyle.Button("Go to", BodyFont, 12);
		goTo.Pressed += () => MapCamera.FocusProvince(GetNodeOrNull<Node3D>(CameraRigPath ?? new NodePath()), c.CapitalId);
		facts.AddChild(goTo);

		// opinions both ways, with the reasons
		var opinions = Section("Opinion");
		int theirs = DiplomacyRules.Opinion(c, us.Tag), ours = DiplomacyRules.Opinion(us, c.Tag);
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 20);
		opinions.AddChild(row);
		row.AddChild(HudStyle.Body($"They think of us: {theirs:+0;-0;0}", BodyFont, 14, theirs >= 0 ? HudStyle.Good : HudStyle.Bad));
		row.AddChild(HudStyle.Body($"We think of them: {ours:+0;-0;0}", BodyFont, 14, ours >= 0 ? HudStyle.Good : HudStyle.Bad));
		bool neighbours = gs.AreNeighbours(us.Tag, c.Tag);
		opinions.AddChild(HudStyle.Body($"Where it settles: {DiplomacyRules.BaseOpinion(c, us, neighbours):+0;-0;0} ({PopulationRules.AffinityName(c.Ruler?.Culture, us.Ruler?.Culture).ToLowerInvariant()}"
			+ $"{(c.Ruler?.Religion == us.Ruler?.Religion ? ", same gods" : "")}{(neighbours ? ", neighbours" : "")}, our prestige {us.Prestige:+0;-0;0})", BodyFont, 12, HudStyle.Muted));
		foreach (OpinionModifier m in c.OpinionModifiers.GetValueOrDefault(us.Tag) ?? new())
			opinions.AddChild(HudStyle.Body($"  {m.Reason}: {m.Amount:+0;-0} (until {gs.Definitions.DefaultCalendar.Format(m.Until)})", BodyFont, 12, m.Amount >= 0 ? HudStyle.Good : HudStyle.Bad));
		if (gs.AggressionOf(c, us.Tag) > 0)
			opinions.AddChild(HudStyle.Body($"  Aggressive expansion: {-gs.AggressionOf(c, us.Tag):0} (our conquests frighten them; it fades {DiplomacyRules.AggressionDecayPerYear:0} a year; at {DiplomacyRules.CoalitionThreshold:0} they may join a coalition against us)", BodyFont, 12, HudStyle.Bad));
		if (gs.AggressionOf(us, c.Tag) > 0)
			opinions.AddChild(HudStyle.Body($"  Their aggressive expansion frightens us: {gs.AggressionOf(us, c.Tag):0}", BodyFont, 12, HudStyle.Muted));
		if (DiplomacyRules.HasTruce(us, c.Tag, gs.Date))
			opinions.AddChild(HudStyle.Body($"Truce until {gs.Definitions.DefaultCalendar.Format(us.Truces[c.Tag])}", BodyFont, 13, HudStyle.Gold));

		// actions
		var actions = Section("Actions");
		var buttons = new HFlowContainer();
		buttons.AddThemeConstantOverride("h_separation", 6);
		actions.AddChild(buttons);
		bool courting = us.ImprovingCountryRelations.Contains(c.Tag);
		var court = HudStyle.Button(courting ? "Recall diplomat" : "Improve relations", BodyFont, 13);
		court.Disabled = !courting && gs.FreeDiplomats(us) <= 0;
		court.TooltipText = courting ? $"Our diplomat raises their opinion of us by {DiplomacyRules.ImproveRelationsPerMonth} a month"
			: court.Disabled ? "All our diplomats are busy" : $"Send a diplomat: +{DiplomacyRules.ImproveRelationsPerMonth} opinion a month ({gs.FreeDiplomats(us)} free)";
		court.Pressed += () => gs.SetImprovingCountryRelations(c.Tag, !courting);
		buttons.AddChild(court);
		if (us.Allies.Contains(c.Tag))
		{
			var brk = HudStyle.Button("Break alliance", BodyFont, 13);
			brk.TooltipText = "They will think much worse of us";
			brk.Pressed += () => gs.BreakAlliance(c.Tag);
			buttons.AddChild(brk);
		}
		else
		{
			string why = DiplomacyRules.AllianceObstacle(us, c, gs.Wars.Values);
			var ally = HudStyle.Button("Propose alliance", BodyFont, 13);
			ally.Disabled = why != null;
			ally.TooltipText = why ?? (theirs >= DiplomacyRules.AllianceOpinion ? "They like us enough to consider it" : $"They must think {DiplomacyRules.AllianceOpinion} or better of us (now {theirs})");
			ally.Pressed += () => _status.Text = gs.ProposeAlliance(c.Tag, out string reason) ? $"{c.Name} is our ally." : reason;
			buttons.AddChild(ally);
		}
		if (us.Rivals.Contains(c.Tag))
		{
			var end = HudStyle.Button("End rivalry", BodyFont, 13);
			end.TooltipText = $"Costs {DiplomacyRules.EndRivalryPrestige:0} prestige";
			end.Pressed += () => gs.EndRivalry(c.Tag);
			buttons.AddChild(end);
		}
		else
		{
			string why = gs.RivalObstacle(us, c);
			var rival = HudStyle.Button("Declare rival", BodyFont, 13);
			rival.Disabled = why != null;
			rival.TooltipText = why ?? $"A rival: opinion {DiplomacyRules.RivalOpinion} both ways; humbling them in war brings {WarRules.HumiliatePrestige:0} prestige ({us.Rivals.Count} of {DiplomacyRules.MaxRivals} rivals named)";
			rival.Pressed += () => { if (!gs.SetRival(c.Tag, out string r)) _status.Text = r; };
			buttons.AddChild(rival);
		}
		if (us.CoalitionAgainst == c.Tag)
		{
			var leave = HudStyle.Button("Leave coalition", BodyFont, 13);
			leave.Pressed += () => gs.LeaveCoalition();
			buttons.AddChild(leave);
		}
		else if (gs.AggressionOf(us, c.Tag) > 0)
		{
			string why = gs.CoalitionObstacle(us, c);
			var join = HudStyle.Button("Join coalition", BodyFont, 13);
			join.Disabled = why != null;
			join.TooltipText = why ?? $"Stand with the {gs.CoalitionAgainst(c.Tag).Count()} others against their aggression: when the coalition outweighs them it goes to war together";
			join.Pressed += () => { if (!gs.JoinCoalition(c.Tag, out string r)) _status.Text = r; };
			buttons.AddChild(join);
		}
		string obstacle = gs.WarObstacle(us, c);
		CasusBelli cb = gs.BestCasusBelli(us, c);
		var war = HudStyle.Button($"Declare war ({cb switch { CasusBelli.Claim => "claim", CasusBelli.Revenge => "revenge", CasusBelli.Coalition => "coalition", _ => "no cause" }})", BodyFont, 13);
		war.Disabled = obstacle != null;
		war.TooltipText = obstacle ?? cb switch
		{
			CasusBelli.Claim => "We press our claims: their land is cheaper to take, and the neighbours understand.",
			CasusBelli.Revenge => "They wronged us: the neighbours understand.",
			_ => "War without cause: our neighbours will think worse of us, and taking land costs more.",
		};
		war.Pressed += () =>
		{
			if (gs.DeclareWar(c.Tag, out string reason))
				_focusTag = $"war:{gs.WarBetween(us.Tag, c.Tag)?.Id}";
			else
				_status.Text = reason;
		};
		buttons.AddChild(war);
		if (c.OverlordTag == us.Tag)
		{
			string why = gs.AnnexObstacle(us, c);
			var annex = HudStyle.Button("Annex", BodyFont, 13);
			annex.Disabled = why != null;
			annex.TooltipText = why ?? "Their provinces become ours (conquered, with separatism); our neighbours and other vassals think worse of us";
			annex.Pressed += () => { if (!gs.Annex(c.Tag, out string r)) _status.Text = r; };
			buttons.AddChild(annex);
			actions.AddChild(HudStyle.Body($"They pay us {gs.TributeOf(c):0.0} gold a month in tribute and follow us to war.", BodyFont, 12, HudStyle.Muted));
		}
		else
		{
			string why = gs.VassalageObstacle(us, c);
			var vassal = HudStyle.Button("Offer vassalage", BodyFont, 13);
			vassal.Disabled = why != null;
			vassal.TooltipText = why ?? "They become our vassal: tribute, and their levies in our wars";
			vassal.Pressed += () => { if (!gs.OfferVassalage(c.Tag, out string r)) _status.Text = r; };
			buttons.AddChild(vassal);
		}
		// guarantees and marriages
		if (us.Guarantees.Contains(c.Tag))
		{
			var withdraw = HudStyle.Button("Withdraw guarantee", BodyFont, 13);
			withdraw.TooltipText = "They will think worse of us";
			withdraw.Pressed += () => gs.WithdrawGuarantee(c.Tag);
			buttons.AddChild(withdraw);
		}
		else
		{
			string why = gs.GuaranteeObstacle(us, c);
			var guarantee = HudStyle.Button("Guarantee independence", BodyFont, 13);
			guarantee.Disabled = why != null;
			guarantee.TooltipText = why ?? "We join their side whenever they are attacked; they think better of us";
			guarantee.Pressed += () => { if (!gs.Guarantee(c.Tag, out string r)) _status.Text = r; };
			buttons.AddChild(guarantee);
		}
		if (us.Marriages.ContainsKey(c.Tag))
		{
			var brk = HudStyle.Button("Break royal marriage", BodyFont, 13);
			brk.TooltipText = $"Until {gs.Definitions.DefaultCalendar.Format(us.Marriages[c.Tag])} otherwise; breaking it costs prestige and their goodwill";
			brk.Pressed += () => gs.BreakMarriage(c.Tag);
			buttons.AddChild(brk);
		}
		else
		{
			string why = gs.MarriageObstacle(us, c);
			var marry = HudStyle.Button("Royal marriage", BodyFont, 13);
			marry.Disabled = why != null;
			marry.TooltipText = why ?? $"Our houses joined: opinion +{DiplomacyRules.MarriageOpinion} both ways and a little legitimacy, for {DiplomacyRules.MarriageYears} years";
			marry.Pressed += () => { if (!gs.RoyalMarriage(c.Tag, out string r)) _status.Text = r; };
			buttons.AddChild(marry);
		}
		// fabricating claims along the border
		var fabricable = gs.FabricableProvinces(us, c).OrderByDescending(p => p.TotalUnits).ToList();
		var working = us.Fabricating.Where(kv => gs.Provinces[kv.Key]?.OwnerTag == c.Tag).ToList();
		foreach (var (id, progress) in working)
		{
			var fabRow = new HBoxContainer();
			fabRow.AddThemeConstantOverride("separation", 8);
			actions.AddChild(fabRow);
			fabRow.AddChild(HudStyle.Body($"Fabricating a claim on {gs.Provinces[id].Name}: {progress:0}% (+{DiplomacyRules.FabricationPerMonth:0} a month)", BodyFont, 13, HudStyle.Gold));
			int pid = id;
			var stop = HudStyle.Button("Recall", BodyFont, 12);
			stop.Pressed += () => gs.StopFabricating(pid);
			fabRow.AddChild(stop);
		}
		if (fabricable.Count > 0)
		{
			var fabRow = new HBoxContainer();
			fabRow.AddThemeConstantOverride("separation", 8);
			actions.AddChild(fabRow);
			var pick = new OptionButton { FocusMode = FocusModeEnum.None };
			if (BodyFont != null) pick.AddThemeFontOverride("font", BodyFont);
			pick.AddThemeFontSizeOverride("font_size", 13);
			foreach (Province p in fabricable)
				pick.AddItem($"{p.Name} ({p.TotalUnits * PopGroup.PeoplePerUnit:N0} people)", p.Id);
			fabRow.AddChild(pick);
			string why = gs.FabricateObstacle(us, fabricable[0]);
			var fab = HudStyle.Button("Fabricate claim", BodyFont, 13);
			fab.Disabled = why != null;
			fab.TooltipText = why ?? $"A diplomat forges a claim: {100 / DiplomacyRules.FabricationPerMonth:0} months, then a cause for war and the land cheaper to take; they think {DiplomacyRules.FabricationOpinion} of it";
			fab.Pressed += () => { if (!gs.FabricateClaim(pick.GetSelectedId(), out string r)) _status.Text = r; };
			fabRow.AddChild(fab);
		}
		var claims = us.Claims.Where(id => gs.Provinces[id]?.OwnerTag == c.Tag).Select(id => gs.Provinces[id].Name).ToList();
		if (claims.Count > 0)
			actions.AddChild(HudStyle.Body("Our claims on their land: " + string.Join(", ", claims), BodyFont, 12, HudStyle.Muted));
	}

	// ------------------------------------------------------------------------------------ a war

	void FillWar(GameState gs, Country us, War w)
	{
		int side = w.SideOf(us.Tag);
		double score = w.Score * side;
		_detail.AddChild(HudStyle.Title(w.Name(gs.Countries), TitleFont, 22));
		_detail.AddChild(HudStyle.Body($"Since {gs.Definitions.DefaultCalendar.Format(w.Started)} · {w.CasusBelli switch { CasusBelli.Claim => "pressing claims", CasusBelli.Revenge => "revenge", _ => "without cause" }}", BodyFont, 13, HudStyle.Muted));
		var sides = new HBoxContainer();
		sides.AddThemeConstantOverride("separation", 30);
		_detail.AddChild(sides);
		foreach (var (label, tags) in new[] { ("Attackers", w.Attackers), ("Defenders", w.Defenders) })
		{
			var box = new VBoxContainer();
			sides.AddChild(box);
			box.AddChild(HudStyle.Body(label, BodyFont, 13, HudStyle.Muted));
			foreach (string t in tags)
			{
				Country c = gs.GetCountry(t);
				box.AddChild(HudStyle.Body($"{c?.Name} · {WarRules.Strength(c, gs.Provinces, gs.Armies.Values, gs.Date):0} reg.", BodyFont, 13, t == us.Tag ? HudStyle.Gold : HudStyle.Text));
			}
		}
		var scoreLabel = HudStyle.Body($"War score: {score:+0;-0;0}", BodyFont, 18, score >= 0 ? HudStyle.Good : HudStyle.Bad);
		scoreLabel.TooltipText = $"Battles: {w.BattleScore * side:+0;-0;0}\nLand we hold: +{WarRules.OccupationScore(w, side, gs.Countries, gs.Provinces):0}\nLand they hold: -{WarRules.OccupationScore(w, -side, gs.Countries, gs.Provinces):0}";
		scoreLabel.MouseFilter = MouseFilterEnum.Pass;
		_detail.AddChild(scoreLabel);
		_detail.AddChild(new ProgressBar { MinValue = -100, MaxValue = 100, Value = score, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) });

		if (w.Battles.Count > 0)
		{
			var battles = Section("Battles");
			foreach (BattleRecord b in w.Battles.AsEnumerable().Reverse().Take(6))
			{
				bool ourSide = w.SideOf(b.AttackerTag) == side;
				bool won = ourSide == b.AttackerWon;
				battles.AddChild(HudStyle.Body($"{gs.Definitions.DefaultCalendar.Format(b.Date)} {gs.GetProvince(b.ProvinceId)?.Name}: {(won ? "won" : "lost")}, "
					+ $"{gs.GetCountry(b.AttackerTag)?.Adjective} {b.AttackerMen:N0} (lost {b.AttackerLost:N0}) against {gs.GetCountry(b.DefenderTag)?.Adjective} {b.DefenderMen:N0} (lost {b.DefenderLost:N0})",
					BodyFont, 12, won ? HudStyle.Good : HudStyle.Bad));
			}
		}

		string leaderTag = side > 0 ? w.AttackerTag : w.DefenderTag;
		if (w.Offer != null)
		{
			var offer = Section("Peace offered");
			Country from = gs.GetCountry(w.Offer.FromTag);
			bool theyWin = w.Offer.WinnerSide != side;
			offer.AddChild(HudStyle.Body(w.Offer.Terms.IsWhitePeace ? $"{from?.Name} offers a white peace: nothing changes hands."
				: theyWin ? $"{from?.Name} demands {gs.DescribeTerms(w.Offer.Terms)}." : $"{from?.Name} offers {gs.DescribeTerms(w.Offer.Terms)}.", BodyFont, 14));
			var answer = new HBoxContainer();
			answer.AddThemeConstantOverride("separation", 6);
			offer.AddChild(answer);
			var yes = HudStyle.Button("Accept", BodyFont, 13);
			yes.Pressed += () => gs.AnswerPeaceOffer(w.Id, true);
			var no = HudStyle.Button("Refuse", BodyFont, 13);
			no.Pressed += () => gs.AnswerPeaceOffer(w.Id, false);
			answer.AddChild(yes);
			answer.AddChild(no);
		}
		if (leaderTag != us.Tag)
		{
			_detail.AddChild(HudStyle.Body($"{gs.GetCountry(leaderTag)?.Name} leads our side and makes the peace.", BodyFont, 13, HudStyle.Muted));
			return;
		}

		// the peace table: what we demand of them, or concede
		var peace = Section("Make peace");
		Country enemyLeader = gs.GetCountry(side > 0 ? w.DefenderTag : w.AttackerTag);
		var losers = side > 0 ? w.Defenders : w.Attackers;
		var chosen = _peaceChoice.TryGetValue(w.Id, out HashSet<int> set) ? set : _peaceChoice[w.Id] = new HashSet<int>();
		var demandable = WarRules.Demandable(w, side, gs.Provinces);
		peace.AddChild(HudStyle.Body(demandable.Count > 0 ? "Provinces we may demand (occupied, or on our border):" : "Occupy their land, or win battles, to have something to demand.", BodyFont, 13, HudStyle.Muted));
		var grid = new GridContainer { Columns = 3 };
		grid.AddThemeConstantOverride("h_separation", 10);
		peace.AddChild(grid);
		var costLabel = HudStyle.Body("", BodyFont, 14);
		var goldBox = new SpinBox { MinValue = 0, MaxValue = Math.Max(0, Math.Floor(enemyLeader?.Gold ?? 0)), Step = 5, Suffix = "gold", Value = _peaceGold.GetValueOrDefault(w.Id) };
		var vassalTick = new CheckBox { Text = $"Make {enemyLeader?.Name} our vassal", FocusMode = FocusModeEnum.None };
		if (BodyFont != null) vassalTick.AddThemeFontOverride("font", BodyFont);
		vassalTick.AddThemeFontSizeOverride("font_size", 13);
		vassalTick.Visible = enemyLeader?.OverlordTag == null && us.OverlordTag == null;
		var humbleTick = new CheckBox { Text = $"Humble {enemyLeader?.Name} before the world", FocusMode = FocusModeEnum.None };
		if (BodyFont != null) humbleTick.AddThemeFontOverride("font", BodyFont);
		humbleTick.AddThemeFontSizeOverride("font_size", 13);
		humbleTick.Visible = enemyLeader != null && (us.Rivals.Contains(enemyLeader.Tag) || w.CasusBelli == CasusBelli.Coalition);
		humbleTick.TooltipText = $"Costs {WarRules.HumiliateCost:0} score: {WarRules.HumiliatePrestige:0} prestige passes from them to us";
		PeaceTerms Terms()
		{
			var t = new PeaceTerms { Gold = goldBox.Value, Vassalize = vassalTick.ButtonPressed, Humiliate = humbleTick.ButtonPressed };
			t.Provinces.AddRange(chosen.Where(id => demandable.Any(p => p.Id == id)));
			return t;
		}
		void UpdateCost()
		{
			double cost = WarRules.TermsCost(Terms(), us, losers, gs.Countries, gs.Provinces);
			costLabel.Text = $"These terms cost {cost:0} war score; we have {score:0}.";
			costLabel.AddThemeColorOverride("font_color", cost <= score ? HudStyle.Good : HudStyle.Bad);
		}
		foreach (Province p in demandable.OrderByDescending(p => p.OccupierTag != null).ThenBy(p => p.Name))
		{
			var tick = new CheckBox { Text = p.Name, ButtonPressed = chosen.Contains(p.Id), FocusMode = FocusModeEnum.None };
			if (BodyFont != null) tick.AddThemeFontOverride("font", BodyFont);
			tick.AddThemeFontSizeOverride("font_size", 13);
			var one = new PeaceTerms();
			one.Provinces.Add(p.Id);
			tick.TooltipText = $"{(p.OccupierTag != null ? "Occupied by " + gs.GetCountry(p.OccupierTag)?.Adjective + " troops" : "On our border")} · {p.TotalUnits * PopGroup.PeoplePerUnit:N0} people"
				+ $" · costs {WarRules.TermsCost(one, us, losers, gs.Countries, gs.Provinces):0} score{(us.Claims.Contains(p.Id) ? " (our claim)" : "")}";
			int id = p.Id;
			tick.Toggled += on => { if (on) chosen.Add(id); else chosen.Remove(id); UpdateCost(); };
			grid.AddChild(tick);
		}
		vassalTick.Toggled += _ => UpdateCost();
		var vassalOnly = new PeaceTerms { Vassalize = true };
		vassalTick.TooltipText = $"They keep their land but pay tribute and follow us to war · costs {WarRules.TermsCost(vassalOnly, us, losers, gs.Countries, gs.Provinces):0} score";
		peace.AddChild(vassalTick);
		humbleTick.Toggled += _ => UpdateCost();
		peace.AddChild(humbleTick);
		var goldRow = new HBoxContainer();
		goldRow.AddThemeConstantOverride("separation", 8);
		goldRow.AddChild(HudStyle.Body("Gold:", BodyFont, 13, HudStyle.Muted));
		goldBox.ValueChanged += v => { _peaceGold[w.Id] = v; UpdateCost(); };
		goldRow.AddChild(goldBox);
		goldRow.AddChild(HudStyle.Body($"(they have {enemyLeader?.Gold:0}; 1 score per {WarRules.GoldPerScorePoint:0} gold)", BodyFont, 12, HudStyle.Muted));
		peace.AddChild(goldRow);
		peace.AddChild(costLabel);
		UpdateCost();
		var send = new HBoxContainer();
		send.AddThemeConstantOverride("separation", 6);
		peace.AddChild(send);
		var demand = HudStyle.Button("Demand these terms", BodyFont, 13);
		demand.TooltipText = "They accept when the terms cost no more than our war score (a little more once the war has dragged on)";
		demand.Pressed += () => { if (gs.OfferPeace(w.Id, Terms(), false, out string why)) { chosen.Clear(); _peaceGold.Remove(w.Id); } else _status.Text = why; };
		send.AddChild(demand);
		var white = HudStyle.Button("Offer white peace", BodyFont, 13);
		white.TooltipText = "Nothing changes hands; accepted when the war is even or has dragged on";
		white.Pressed += () => { if (!gs.OfferPeace(w.Id, new PeaceTerms(), false, out string why)) _status.Text = why; };
		send.AddChild(white);
		if (score < 0)
		{
			var concede = HudStyle.Button("Concede (give what they hold)", BodyFont, 13);
			concede.TooltipText = "We give up the provinces they occupy and ask for peace";
			concede.Pressed += () =>
			{
				var t = new PeaceTerms();
				t.Provinces.AddRange(WarRules.Demandable(w, -side, gs.Provinces).Where(p => p.OccupierTag != null && w.SideOf(p.OccupierTag) == -side).Select(p => p.Id));
				if (!gs.OfferPeace(w.Id, t, true, out string why))
					_status.Text = why;
			};
			send.AddChild(concede);
		}
	}

	VBoxContainer Section(string title)
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", HudStyle.Section());
		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 4);
		panel.AddChild(box);
		box.AddChild(HudStyle.Title(title, TitleFont, 16));
		_detail.AddChild(panel);
		return box;
	}

	static void Clear(Node box)
	{
		foreach (Node child in box.GetChildren())
		{
			box.RemoveChild(child);
			child.QueueFree();
		}
	}
}
