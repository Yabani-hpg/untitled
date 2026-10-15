using System;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Ideas: taking up groups, buying ideas, and the game's countries doing the same.
public partial class GameState
{
	public string IdeaGroupObstacle(Country c, IdeaGroup g)
	{
		if (c == null || g == null)
			return "No such group";
		if (c.IdeaGroups.Contains(g.Id))
			return "Already taken up";
		if (c.IdeaGroups.Count >= IdeaRules.Slots(c))
			return $"No free slot: {IdeaRules.Slots(c)} of {IdeaRules.MaxGroups}, one more for every {IdeaRules.TechsPerSlot} technologies known";
		return null;
	}

	public bool ChooseIdeaGroup(string id, out string reason)
	{
		Country c = PlayerCountry;
		IdeaGroup g = Definitions.GetIdeaGroup(id);
		reason = IdeaGroupObstacle(c, g);
		if (reason != null)
			return false;
		c.IdeaGroups.Add(id);
		EmitSignal(SignalName.ResearchChanged);
		return true;
	}

	/// <summary>Gives up a group: its ideas are lost.</summary>
	public void AbandonIdeaGroup(string id)
	{
		Country c = PlayerCountry;
		IdeaGroup g = Definitions.GetIdeaGroup(id);
		if (c == null || g == null || !c.IdeaGroups.Remove(id))
			return;
		foreach (Idea i in g.Ideas)
			c.Ideas.Remove(i.Id);
		EmitSignal(SignalName.ResearchChanged);
		EmitSignal(SignalName.LawsChanged);
	}

	public string BuyObstacle(Country c, IdeaGroup g)
	{
		if (c == null || g == null || !c.IdeaGroups.Contains(g.Id))
			return "Not one of our groups";
		if (IdeaRules.NextIdea(c, g) == null)
			return "Every idea of the group is ours";
		if (c.IdeaPoints < IdeaRules.Cost(c))
			return $"The next idea costs {IdeaRules.Cost(c):0} idea points; we have {c.IdeaPoints:0}";
		return null;
	}

	public bool BuyIdea(string groupId, out string reason) => BuyIdeaFor(PlayerCountry, Definitions.GetIdeaGroup(groupId), out reason);

	bool BuyIdeaFor(Country c, IdeaGroup g, out string reason)
	{
		reason = BuyObstacle(c, g);
		if (reason != null)
			return false;
		Idea idea = IdeaRules.NextIdea(c, g);
		c.IdeaPoints -= IdeaRules.Cost(c);
		c.Ideas.Add(idea.Id);
		if (c.Tag == PlayerTag)
		{
			string national = c.Ideas.Count % IdeaRules.IdeasPerNational == 0 && c.Definition?.NationalIdeas != null && IdeaRules.NationalUnlocked(c) <= 7
				? $" {c.Definition.NationalIdeas.Ideas[IdeaRules.NationalUnlocked(c) - 1].Name} is ours too." : "";
			Notify($"{idea.Name}: {IdeaRules.Describe(idea.Modifiers)}.{national}");
		}
		EmitSignal(SignalName.ResearchChanged);
		EmitSignal(SignalName.LawsChanged);
		return true;
	}

	/// <summary>The share of research set aside for ideas, 0 to 1.</summary>
	public void SetIdeaShare(double share)
	{
		if (PlayerCountry == null)
			return;
		PlayerCountry.IdeaShare = Math.Clamp(share, 0, 1);
		EmitSignal(SignalName.ResearchChanged);
	}

	static readonly string[] AiGroupOrder = { "administrative", "offensive", "trade", "economic", "diplomatic", "defensive", "expansion", "religious" };

	/// <summary>The game's countries set aside a share for ideas, take up groups as slots open, and buy what they can afford.</summary>
	void RunIdeaStep()
	{
		foreach (Country c in SettledCountries.Where(c => c.Tag != PlayerTag))
		{
			if (c.IdeaShare <= 0)
				c.IdeaShare = IdeaRules.DefaultShare;
			if (c.IdeaGroups.Count < IdeaRules.Slots(c))
			{
				string pick = AiGroupOrder.FirstOrDefault(id => !c.IdeaGroups.Contains(id) && Definitions.GetIdeaGroup(id) != null);
				if (pick != null)
					c.IdeaGroups.Add(pick);
			}
			foreach (string id in c.IdeaGroups)
			{
				if (BuyIdeaFor(c, Definitions.GetIdeaGroup(id), out _))
					break;
			}
		}
	}
}
