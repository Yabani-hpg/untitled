using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Settled countries and the unsettled ones around them (tribes and nomads, see <see cref="Tribe"/>).
///
/// A settled country bordering a tribe's lands can court it: improve relations, propose an alliance,
/// hire its fierce warriors as mercenaries while allied, and once relations are high and the alliance
/// old enough, absorb it. Its lands then come under the country's control, with separatism for
/// <see cref="ControlRules.YearsToCore"/> years. Nomads can also be subjugated by force.
/// </summary>
public static class TribeRules
{
	// relations
	public const int MinRelation = -100, MaxRelation = 100;
	/// <summary>Relations gained a month while a diplomat works on a tribe.</summary>
	public const int ImproveRelationsPerMonth = 3;
	/// <summary>How many tribes a country's diplomats can court at once.</summary>
	public const int Diplomats = 2;

	// alliance
	public const int MinRelationForAlliance = 25;
	public const int YearsBeforeAskingAgain = 2;
	/// <summary>Allies falling below this relation end the alliance.</summary>
	public const int AllianceBreaksBelow = 0;

	// mercenaries
	/// <summary>Share of a tribe's warriors it will lend its ally.</summary>
	public const double HireableShare = 0.5;

	// absorption
	public const int MinRelationToAbsorb = 75;
	public const int YearsAlliedToAbsorb = 5;

	// nomads roaming
	public const double CampMoveChance = 0.2;

	static double YearsBetween(GameDate from, GameDate to) => (to.Day - from.Day) / 365.2425;

	/// <summary>Where relations settle without diplomacy: kin like each other, strangers are wary.</summary>
	public static int BaseRelation(Tribe t, Country c) =>
		(int)Math.Round(40 * PopulationRules.Affinity(t.Culture, c?.Ruler?.Culture) - 20 + LawRules.Mod(c, "tribe_relations"));

	public static void ChangeRelation(Tribe t, string tag, int delta) =>
		t.Relations[tag] = Math.Clamp(t.RelationWith(tag) + delta, MinRelation, MaxRelation);

	/// <summary>
	/// Regiments the tribe can put in the field (those hired out are away). With no food left to keep
	/// its war bands, only half of them turn out.
	/// </summary>
	public static int Warriors(Tribe t, IReadOnlyList<Province> provinces)
	{
		int w = 0;
		foreach (int id in t.Provinces)
			w += ControlRules.Warriors(provinces[id]);
		w = Math.Max(0, w - t.HiredRegiments);
		return EconomyRules.Starving(t) ? w / 2 : w;
	}

	public static int People(Tribe t, IReadOnlyList<Province> provinces) => t.Provinces.Sum(id => provinces[id].TotalUnits);

	/// <summary>Whether the tribe's lands touch land the country controls.</summary>
	public static bool Borders(Tribe t, IReadOnlyList<Province> provinces, string tag) =>
		t.Provinces.Any(id => ControlRules.Borders(provinces[id], tag));

	// ------------------------------------------------------------------------------------- alliance

	public static double AllianceChance(Tribe t, Country c) =>
		Math.Clamp(0.35 + t.RelationWith(c.Tag) / 200.0 + LawRules.Mod(c, "alliance_chance"), 0, 0.95);

	public static bool CanProposeAlliance(Tribe t, Country c, IReadOnlyList<Province> provinces, GameDate today, out string reason)
	{
		reason = null;
		if (t.AlliedTag == c.Tag)
			reason = "Already our allies";
		else if (t.AlliedTag != null)
			reason = "They are allied with another country";
		else if (!Borders(t, provinces, c.Tag))
			reason = "Their lands must border ours";
		else if (t.RefusedTag == c.Tag && today < t.RefusedUntil)
			reason = "They refused us; they will listen again in time";
		else if (t.RelationWith(c.Tag) < MinRelationForAlliance)
			reason = $"They need to think well of us: relations {MinRelationForAlliance} or more";
		return reason == null;
	}

	public static bool ProposeAlliance(Tribe t, Country c, IReadOnlyList<Province> provinces, GameDate today, Random rng)
	{
		if (!CanProposeAlliance(t, c, provinces, today, out _))
			return false;
		if (rng.NextDouble() < AllianceChance(t, c))
		{
			t.AlliedTag = c.Tag;
			t.AlliedSince = today;
			ChangeRelation(t, c.Tag, 10);
			return true;
		}
		t.RefusedTag = c.Tag;
		t.RefusedUntil = today.AddDays((long)(YearsBeforeAskingAgain * 365.2425));
		return false;
	}

	public static void EndAlliance(Tribe t, Country ally)
	{
		if (ally != null && t.HiredRegiments > 0)
			t.HiredRegiments = 0;           // the mercenaries go home
		t.AlliedTag = null;
	}

	// ---------------------------------------------------------------------------------- mercenaries

	/// <summary>Regiments the tribe would still lend its ally.</summary>
	public static int Hireable(Tribe t, IReadOnlyList<Province> provinces)
	{
		int all = Warriors(t, provinces) + t.HiredRegiments;
		return Math.Max(0, (int)(all * HireableShare) - t.HiredRegiments);
	}

	public static bool CanHire(Tribe t, Country c, IReadOnlyList<Province> provinces, int regiments, out string reason)
	{
		reason = null;
		if (t.AlliedTag != c.Tag)
			reason = "Only our allies fight for us";
		else if (c.Gold < regiments * EconomyRules.MercenaryPayOf(c))
			reason = "We can't pay them";
		else if (regiments < 1)
			reason = "Hire at least one regiment";
		else if (regiments > Hireable(t, provinces))
			reason = $"They will lend at most {Hireable(t, provinces)} more regiments";
		return reason == null;
	}

	public static void Hire(Tribe t, int regiments) => t.HiredRegiments += regiments;

	public static void Dismiss(Tribe t) => t.HiredRegiments = 0;

	/// <summary>All the mercenaries a country has hired, from all its allied tribes.</summary>
	public static int Mercenaries(IEnumerable<Tribe> tribes, string tag) =>
		tribes.Where(t => t.AlliedTag == tag).Sum(t => t.HiredRegiments);

	/// <summary>Takes fallen mercenaries from the tribes that lent them (their people die).</summary>
	static void LoseMercenaries(IEnumerable<Tribe> tribes, string tag, int lost, IReadOnlyList<Province> provinces)
	{
		foreach (Tribe t in tribes.Where(t => t.AlliedTag == tag && t.HiredRegiments > 0).ToList())
		{
			int k = Math.Min(lost, t.HiredRegiments);
			t.HiredRegiments -= k;
			lost -= k;
			int left = k;
			foreach (int id in t.Provinces)
			{
				if (left <= 0)
					break;
				int before = provinces[id].TotalUnits;
				ControlRules.KillWarriors(provinces[id], left);
				left -= before - provinces[id].TotalUnits;
			}
			if (lost <= 0)
				break;
		}
	}

	// ------------------------------------------------------------------------------------ absorption

	/// <summary>The conditions for absorbing a tribe, each with whether it is met.</summary>
	public static List<(string Condition, bool Met)> AbsorbConditions(Tribe t, Country c, IReadOnlyList<Province> provinces, IEnumerable<Army> armies, GameDate today)
	{
		bool allied = t.AlliedTag == c.Tag;
		var list = new List<(string, bool)>
		{
			("Allied with us", allied),
			($"Allied for {YearsAlliedToAbsorb} years", allied && YearsBetween(t.AlliedSince, today) >= YearsAlliedToAbsorb),
			($"Relations {MinRelationToAbsorb} or more", t.RelationWith(c.Tag) >= MinRelationToAbsorb),
			("Their lands border ours", Borders(t, provinces, c.Tag)),
		};
		if (t.IsNomadic)
		{
			// nomads only join a country they respect: one whose army outnumbers their warriors
			int ours = MilitaryRules.UnderArms(c, armies, provinces.Where(p => p?.OwnerTag == c.Tag));
			list.Add(($"Our regiments outnumber their {Warriors(t, provinces) + t.HiredRegiments} warriors", ours > Warriors(t, provinces) + t.HiredRegiments));
		}
		return list;
	}

	public static bool CanAbsorb(Tribe t, Country c, IReadOnlyList<Province> provinces, IEnumerable<Army> armies, GameDate today) =>
		AbsorbConditions(t, c, provinces, armies, today).TrueForAll(x => x.Met);

	/// <summary>The tribe joins the country: its lands come under control, its mercenaries join the army.</summary>
	public static void Absorb(Tribe t, Country c, IReadOnlyList<Province> provinces, GameDate today)
	{
		foreach (int id in t.Provinces)
		{
			Province p = provinces[id];
			p.OwnerTag = c.Tag;
			p.Control = new ProvinceControl { Kind = ControlKind.Absorbed, Since = today };
			p.TribeId = 0;
		}
		c.Gold += t.Food / EconomyRules.FoodPerGold;     // their stores, bartered into the treasury
		t.Food = 0;
		t.HiredRegiments = 0;
		t.AlliedTag = null;
		t.Provinces.Clear();
	}

	// ---------------------------------------------------------------------------------- subjugation

	/// <summary>The nomads defending: all their warriors, fierce, on their home ground where our army stands.</summary>
	public static ControlRules.Force Defenders(Tribe t, Army army, IReadOnlyList<Province> provinces) =>
		ControlRules.Force.Of(0, Warriors(t, provinces), ControlRules.HomeGroundModifier(provinces[army?.ProvinceId > 0 ? army.ProvinceId : t.CampProvinceId]));

	/// <summary>Our army, with the mercenaries sent along, in the nomads' lands.</summary>
	public static ControlRules.Force Attackers(Army army, int mercenaries, IReadOnlyList<Province> provinces) =>
		MilitaryRules.ForceOf(army.Regiments, provinces[army.ProvinceId], 0).Plus(ControlRules.Force.Of(0, mercenaries, 0));

	/// <summary>The country's armies standing (not marching) in the tribe's lands, strongest first.</summary>
	public static List<Army> ArmiesIn(Tribe t, Country c, IEnumerable<Army> armies) =>
		armies.Where(a => a.OwnerTag == c.Tag && !a.Moving && a.Regiments.Count > 0 && t.Provinces.Contains(a.ProvinceId))
			.OrderByDescending(a => a.Regiments.Count).ToList();

	public static bool CanSubjugate(Tribe t, Country c, Army army, IEnumerable<Tribe> tribes, IReadOnlyList<Province> provinces, int mercenaries, out string reason)
	{
		reason = null;
		if (!t.IsNomadic)
			reason = "Settled tribes are won by alliance, not by force";
		else if (t.AlliedTag == c.Tag)
			reason = "They are our allies";
		else if (!Borders(t, provinces, c.Tag))
			reason = "Their lands must border ours";
		else if (army == null || army.OwnerTag != c.Tag || army.Regiments.Count == 0)
			reason = "March an army into their lands first";
		else if (army.Moving || !t.Provinces.Contains(army.ProvinceId))
			reason = $"The {army.Name} must stand in their lands";
		else if (mercenaries < 0 || mercenaries > Mercenaries(tribes, c.Tag))
			reason = $"We have only {Mercenaries(tribes, c.Tag)} mercenary regiments";
		return reason == null;
	}

	public static double SubjugationChance(Tribe t, Army army, IReadOnlyList<Province> provinces, int mercenaries) =>
		ControlRules.WinChance(Attackers(army, mercenaries, provinces), Defenders(t, army, provinces));

	/// <summary>
	/// The army gives battle to the nomads. Winning breaks the horde: all its lands come under control as
	/// subjugated provinces, each held by a regiment of the army left behind as its garrison. Losing makes the
	/// nomads hate us. The fallen on both sides are gone from the population.
	/// </summary>
	public static ControlRules.BattleResult Subjugate(Tribe t, Country c, Army army, IEnumerable<Tribe> tribes, IReadOnlyList<Province> provinces,
		int mercenaries, GameDate today, Random rng)
	{
		ControlRules.BattleResult r = ControlRules.Battle(Attackers(army, mercenaries, provinces), Defenders(t, army, provinces), rng);

		// the mercenaries lose their share, the army's regiments theirs
		LoseMercenaries(tribes, c.Tag, (int)Math.Round(mercenaries * r.AttackerShare), provinces);
		army.Regiments.RemoveAll(MilitaryRules.TakeLosses(army.Regiments, r.AttackerShare, !r.AttackerWon).Contains);
		ControlRules.KillWarriors(provinces[army.ProvinceId], r.DefenderLosses);

		if (!r.AttackerWon)
		{
			ChangeRelation(t, c.Tag, -30);
			return r;
		}
		// a regiment stays behind in each of their provinces, foot before horses and engines
		var lands = t.Provinces.OrderBy(id => id == army.ProvinceId ? 0 : 1).ToList();
		foreach (int id in lands)
		{
			Province p = provinces[id];
			p.OwnerTag = c.Tag;
			p.Control = new ProvinceControl { Kind = ControlKind.Subjugated, Since = today };
			p.TribeId = 0;
			Regiment garrison = army.Regiments.OrderBy(x => x.Type.Category).ThenByDescending(x => x.Strength).FirstOrDefault();
			if (garrison != null)
			{
				army.Regiments.Remove(garrison);
				p.Control.Garrison.Add(garrison);
			}
		}
		t.Provinces.Clear();
		return r;
	}

	// --------------------------------------------------------------------------------- monthly step

	public enum EventKind { AllianceEnded, CampMoved }

	public readonly record struct TribeEvent(EventKind Kind, Tribe Tribe, string Tag);

	/// <summary>
	/// One month: relations drift toward where kinship puts them (or rise where diplomats work), alliances
	/// that soured or lost their border end, and nomads move their camps, taking empty land they roam into.
	/// </summary>
	public static List<TribeEvent> MonthlyStep(IEnumerable<Tribe> tribes, IReadOnlyDictionary<string, Country> countries,
		IReadOnlyList<Province> provinces, Random rng)
	{
		var events = new List<TribeEvent>();
		var settled = countries.Values.Where(c => c.CapitalId > 0).ToList();
		foreach (Tribe t in tribes)
		{
			if (t.Provinces.Count == 0)
				continue;
			foreach (Country c in settled)
			{
				int current = t.RelationWith(c.Tag);
				if (c.ImprovingRelations.Contains(t.Id))
					ChangeRelation(t, c.Tag, ImproveRelationsPerMonth);
				else
				{
					int target = BaseRelation(t, c) + (t.AlliedTag == c.Tag ? 20 : 0);
					if (current != target)
						ChangeRelation(t, c.Tag, Math.Sign(target - current));
				}
			}
			if (t.AlliedTag != null && (t.RelationWith(t.AlliedTag) < AllianceBreaksBelow || !Borders(t, provinces, t.AlliedTag)))
			{
				string ally = t.AlliedTag;
				EndAlliance(t, countries.GetValueOrDefault(ally));
				events.Add(new TribeEvent(EventKind.AllianceEnded, t, ally));
			}

			if (t.IsNomadic && rng.NextDouble() < CampMoveChance)
			{
				// the camp moves to a neighbouring pasture of theirs, or out into empty land, which becomes theirs
				Province camp = provinces[t.CampProvinceId];
				var options = camp.Neighbors
					.Where(l => l.SharesBorder && !l.To.IsWater && l.To.OwnerTag == null
						&& (l.To.TribeId == t.Id || l.To.TribeId == 0 && l.To.Inhabitants == Inhabitants.Empty))
					.Select(l => l.To).ToList();
				if (options.Count > 0)
				{
					Province next = options[rng.Next(options.Count)];
					if (next.TribeId == 0)
					{
						next.TribeId = t.Id;
						t.Provinces.Add(next.Id);
					}
					t.CampProvinceId = next.Id;
					events.Add(new TribeEvent(EventKind.CampMoved, t, null));
				}
			}
		}
		// diplomats stop courting tribes that are gone or already allied
		foreach (Country c in settled)
			c.ImprovingRelations.RemoveWhere(id => !tribes.Any(t => t.Id == id && t.Provinces.Count > 0));
		return events;
	}
}
