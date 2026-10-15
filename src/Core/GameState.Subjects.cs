using System;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Kinds of subject beyond the plain vassal: marches made and revoked, tributaries, subjects released or breaking free.
public partial class GameState
{
	/// <summary>Why a vassal can't be made a march, or null: it must be our vassal and fond of us.</summary>
	public string MarchObstacle(Country us, Country them)
	{
		if (them == null || them.OverlordTag != us.Tag)
			return "Not our subject";
		if (them.Subject == SubjectKind.March)
			return "Already our march";
		if (them.Subject != SubjectKind.Vassal)
			return $"A {SubjectRules.Word(them.Subject)} cannot be made a march";
		if (DiplomacyRules.Opinion(them, us.Tag) < SubjectRules.MarchOpinionNeeded)
			return $"They must think {SubjectRules.MarchOpinionNeeded} or better of us (now {DiplomacyRules.Opinion(them, us.Tag)})";
		return null;
	}

	/// <summary>The player makes a vassal a march: no tribute, never annexed, but it holds the border.</summary>
	public bool MakeMarch(string tag, out string reason)
	{
		Country us = PlayerCountry, them = GetCountry(tag);
		reason = us == null ? "No country" : MarchObstacle(us, them);
		if (reason != null)
			return false;
		MakeMarchOf(us, them);
		return true;
	}

	void MakeMarchOf(Country us, Country them)
	{
		them.Subject = SubjectKind.March;
		DiplomacyRules.AddModifier(them, us.Tag, "Made us a march", 20, Date, 20);
		if (us.Tag == PlayerTag || them.Tag == PlayerTag)
			Notify($"{them.Name} is now a march of {us.Name}.");
		EmitSignal(SignalName.DiplomacyChanged);
	}

	/// <summary>The player takes a march's standing away: it is a plain vassal again, and resents it.</summary>
	public bool RevokeMarch(string tag, out string reason)
	{
		Country us = PlayerCountry, them = GetCountry(tag);
		reason = us == null ? "No country" : them?.OverlordTag != us.Tag ? "Not our subject" : them.Subject != SubjectKind.March ? "Not a march" : null;
		if (reason != null)
			return false;
		them.Subject = SubjectKind.Vassal;
		DiplomacyRules.AddModifier(them, us.Tag, "Revoked our march", SubjectRules.RevokeMarchOpinion, Date);
		Notify($"{them.Name} is a plain vassal of ours again.");
		EmitSignal(SignalName.DiplomacyChanged);
		return true;
	}

	/// <summary>The player frees a subject of any kind; it thinks well of us for it.</summary>
	public bool ReleaseSubject(string tag, out string reason)
	{
		Country us = PlayerCountry, them = GetCountry(tag);
		reason = us == null ? "No country" : them?.OverlordTag != us.Tag ? "Not our subject" : null;
		if (reason != null)
			return false;
		FreeSubject(them, us, "Freed us", SubjectRules.ReleaseOpinion);
		Notify($"{them.Name} is free of us.");
		return true;
	}

	void FreeSubject(Country subject, Country overlord, string reason, int opinion)
	{
		subject.OverlordTag = null;
		subject.Subject = SubjectKind.Vassal;
		DiplomacyRules.AddModifier(subject, overlord.Tag, reason, opinion, Date);
		EmitSignal(SignalName.DiplomacyChanged);
	}

	/// <summary>Each month: tributaries run by the game that outgrow and dislike their overlord break free; overlords run by the game make border vassals into marches.</summary>
	void RunSubjectStep()
	{
		foreach (Country c in SettledCountries.Where(c => c.OverlordTag != null).ToList())
		{
			Country overlord = GetCountry(c.OverlordTag);
			if (overlord == null || overlord.CapitalId <= 0)
				continue;
			if (c.Subject == SubjectKind.Tributary && c.Tag != PlayerTag && DiplomacyRules.Opinion(c, overlord.Tag) < 0 && _rng.NextDouble() < SubjectRules.BreakFreeChance
				&& WarRules.Strength(c, _provinces, _armies.Values, Date) > WarRules.Strength(overlord, _provinces, _armies.Values, Date) * SubjectRules.BreakFreeStrength)
			{
				FreeSubject(c, overlord, "Threw off our tribute", 0);
				DiplomacyRules.AddModifier(overlord, c.Tag, "Stopped paying us tribute", -40, Date);
				if (overlord.Tag == PlayerTag || c.Tag == PlayerTag || AreNeighbours(PlayerTag, c.Tag))
					Notify($"{c.Name} stops paying tribute to {overlord.Name}.");
			}
			else if (c.Subject == SubjectKind.Vassal && overlord.Tag != PlayerTag && _rng.NextDouble() < 0.03 && MarchObstacle(overlord, c) == null
				&& overlord.Rivals.Any(r => AreNeighbours(c.Tag, r)))
				MakeMarchOf(overlord, c);
		}
	}
}
