using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Events, decisions and missions: what happens to countries, what they may choose, and what they strive for.
public partial class GameState
{
	/// <summary>An event waits for the player's answer (the game pauses), or the queue emptied.</summary>
	[Signal]
	public delegate void EventPendingEventHandler();

	/// <summary>A decision or mission became available or was done.</summary>
	[Signal]
	public delegate void DecisionsChangedEventHandler();

	/// <summary>Events waiting for the player's answer, the first to answer first.</summary>
	public IReadOnlyList<GameEvent> PendingEvents => _pendingEvents;

	readonly List<GameEvent> _pendingEvents = new();

	// ------------------------------------------------------------------------------------ effects

	/// <summary>Applies an option's, decision's or mission's effects to a country.</summary>
	public void ApplyEffects(Country c, Effects fx)
	{
		if (fx == null)
			return;
		c.Gold += fx.Gold;
		if (fx.Stability != 0)
			StateRules.ChangeStability(c, fx.Stability);
		c.Prestige = Math.Clamp(c.Prestige + fx.Prestige, -100, 100);
		c.Legitimacy = Math.Clamp(c.Legitimacy + fx.Legitimacy, 0, 100);
		c.WarExhaustion = Math.Clamp(c.WarExhaustion + fx.WarExhaustion, 0, StateRules.MaxExhaustion);
		if (fx.Research != 0)
		{
			c.ResearchStockpile = Math.Max(0, c.ResearchStockpile + fx.Research);
			PourStockpile(c);
		}
		foreach (string f in fx.SetFlags) c.EventFlags.Add(f);
		foreach (string f in fx.ClearFlags) c.EventFlags.Remove(f);
		foreach (string f in fx.SetWorldFlags) SetWorldFlag(f);
		foreach (int id in fx.Claims)
		{
			if (_provinces[id]?.OwnerTag != c.Tag)
				c.Claims.Add(id);
		}
		foreach (var (who, amount) in fx.Opinion)
		{
			IEnumerable<Country> targets = who == "neighbours" ? SettledCountries.Where(o => o != c && AreNeighbours(o.Tag, c.Tag))
				: who == "all" ? SettledCountries.Where(o => o != c)
				: GetCountry(who) is Country one ? new[] { one } : Array.Empty<Country>();
			foreach (Country o in targets)
				DiplomacyRules.AddModifier(o, c.Tag, "Our doings", amount, Date, 10);
		}
		if (fx.CapitalPops != 0 && GetProvince(c.CapitalId) is Province capital)
		{
			if (fx.CapitalPops > 0)
			{
				PopGroup like = capital.Pops.OrderByDescending(g => g.Units).FirstOrDefault();
				if (like != null)
					capital.AddPeople(like.Culture, like.Religion, like.Occupation, fx.CapitalPops, true);
			}
			else
			{
				int left = -fx.CapitalPops;
				foreach (PopGroup g in capital.Pops.OrderByDescending(g => g.Units))
				{
					int k = Math.Min(left, Math.Max(0, g.Units - 1));
					g.Units -= k;
					left -= k;
				}
				capital.Pops.RemoveAll(g => g.Units <= 0);
			}
			EmitSignal(SignalName.ProvinceChanged, capital.Id);
		}
		if (fx.Tech != null && Definitions.GetTech(fx.Tech) is Tech t && !c.Techs.Contains(t.Id))
		{
			TechRules.Learn(c, t);
			EmitSignal(SignalName.ResearchChanged);
		}
		if (fx.Levies > 0 && GetProvince(c.CapitalId) != null)
		{
			var types = Enumerable.Repeat(MilitaryRules.BasicFoot(Definitions), fx.Levies).ToList();
			var raised = MilitaryRules.Levy(c, ProvincesOf(c.Tag).ToList(), types, Date);
			if (raised.Count > 0)
			{
				Army army = NewArmy(c, c.CapitalId);
				army.Regiments.AddRange(raised);
				EmitSignal(SignalName.ArmiesChanged);
			}
		}
		if (fx.RulerDies && c.Ruler != null)
			Succeed(c);
		if (fx.Message != null && c.Tag == PlayerTag)
			Notify(fx.Message);
		RefreshFlags();
	}

	// ------------------------------------------------------------------------------------ events

	/// <summary>The monthly chance an event with a mean time fires: on average once in that many years.</summary>
	static double MonthlyChance(double meanTimeYears) =>
		meanTimeYears <= 0 ? 1 : Math.Min(1, 1.0 / (meanTimeYears * 12));

	/// <summary>Each month, every settled country's events are tried; the player's wait for an answer, the game's choose at once.</summary>
	void RunEventStep()
	{
		foreach (Country c in SettledCountries.ToList())
		{
			foreach (GameEvent ev in Definitions.Events)
			{
				if (ev.Once && c.Done.Contains(ev.Id))
					continue;
				if (c.Tag == PlayerTag && _pendingEvents.Contains(ev))
					continue;
				if (ev.Trigger != null && !ev.Trigger.Holds(this, c))
					continue;
				if (_rng.NextDouble() >= MonthlyChance(ev.MeanTimeYears))
					continue;
				if (ev.Once)
					c.Done.Add(ev.Id);
				if (c.Tag == PlayerTag)
				{
					_pendingEvents.Add(ev);
					SetPaused(true);
					EmitSignal(SignalName.EventPending);
				}
				else
					ApplyEffects(c, AiChoice(ev).Effects);
			}
			// missions done this month
			foreach (Mission m in c.Definition?.Missions ?? new())
			{
				if (c.Done.Contains(m.Id) || m.After != null && !c.Done.Contains(m.After) || !m.Requires.Holds(this, c))
					continue;
				c.Done.Add(m.Id);
				ApplyEffects(c, m.Reward);
				if (c.Tag == PlayerTag)
					Notify($"Mission accomplished: {m.Title}.");
			}
			// the game's countries take the decisions open to them, now and then
			if (c.Tag != PlayerTag && _rng.NextDouble() < 0.15)
			{
				Decision d = Definitions.Decisions.FirstOrDefault(x => CanTakeDecision(c, x, out _));
				if (d != null)
					TakeDecisionFor(c, d);
			}
		}
		EmitSignal(SignalName.DecisionsChanged);
	}

	EventOption AiChoice(GameEvent ev)
	{
		double total = ev.Options.Sum(o => o.AiWeight);
		double r = _rng.NextDouble() * total;
		foreach (EventOption o in ev.Options)
		{
			r -= o.AiWeight;
			if (r < 0)
				return o;
		}
		return ev.Options[^1];
	}

	/// <summary>The player answers the first pending event.</summary>
	public void AnswerEvent(int option)
	{
		if (_pendingEvents.Count == 0 || PlayerCountry == null)
			return;
		GameEvent ev = _pendingEvents[0];
		_pendingEvents.RemoveAt(0);
		ApplyEffects(PlayerCountry, ev.Options[Math.Clamp(option, 0, ev.Options.Count - 1)].Effects);
		EmitSignal(SignalName.EventPending);
		EmitSignal(SignalName.LawsChanged);
		EmitSignal(SignalName.DiplomacyChanged);
	}

	// ------------------------------------------------------------------------------------ decisions

	public bool CanTakeDecision(Country c, Decision d, out string reason)
	{
		reason = null;
		if (d.Once && c.Done.Contains(d.Id))
			reason = "Already taken";
		else if (d.Visible != null && !d.Visible.Holds(this, c))
			reason = "Not for us";
		else if (d.Requires != null && !d.Requires.Holds(this, c))
			reason = $"Requires: {d.RequiresText ?? "conditions we don't meet"}";
		else if (d.Effects.Gold < 0 && c.Gold < -d.Effects.Gold)
			reason = $"Costs {-d.Effects.Gold:0} gold; we have {c.Gold:0}";
		return reason == null;
	}

	/// <summary>Decisions the country can see: those it may take, and those it could if it met their conditions.</summary>
	public IEnumerable<Decision> VisibleDecisions(Country c) =>
		Definitions.Decisions.Where(d => (d.Visible == null || d.Visible.Holds(this, c)) && !(d.Once && c.Done.Contains(d.Id)));

	public bool TakeDecision(string id, out string reason)
	{
		Country c = PlayerCountry;
		Decision d = Definitions.GetDecision(id);
		reason = null;
		if (c == null || d == null)
			reason = "Unknown decision";
		else if (!CanTakeDecision(c, d, out reason))
			return false;
		if (reason != null)
			return false;
		TakeDecisionFor(c, d);
		return true;
	}

	void TakeDecisionFor(Country c, Decision d)
	{
		if (d.Once)
			c.Done.Add(d.Id);
		ApplyEffects(c, d.Effects);
		if (c.Tag == PlayerTag)
			Notify($"{d.Title}: done.");
		else if (AreNeighbours(PlayerTag, c.Tag))
			Notify($"{c.Name}: {d.Title}.");
		EmitSignal(SignalName.DecisionsChanged);
		EmitSignal(SignalName.LawsChanged);
	}
}
