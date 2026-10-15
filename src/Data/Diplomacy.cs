using System.Collections.Generic;
using Untitled.Core;

namespace Untitled.Data;

/// <summary>Why a war is declared. A claim makes conquest cheaper and keeps the neighbours calmer.</summary>
public enum CasusBelli
{
	/// <summary>A claim on the enemy's land (the country file's "claims").</summary>
	Claim,
	/// <summary>Conquest with no claim: the world thinks worse of it.</summary>
	Conquest,
	/// <summary>A war against a country that humiliated or attacked us before: full score from battles.</summary>
	Revenge,
}

/// <summary>A war between settled countries, with the allies each side called in.</summary>
public sealed class War
{
	public int Id { get; init; }
	/// <summary>The country that declared it, and the one it was declared on: the war leaders.</summary>
	public string AttackerTag { get; init; }
	public string DefenderTag { get; init; }
	public CasusBelli CasusBelli { get; init; }
	public GameDate Started { get; init; }
	/// <summary>Everyone fighting on each side, the leaders first.</summary>
	public List<string> Attackers { get; } = new();
	public List<string> Defenders { get; } = new();
	/// <summary>War score from the attacker's side, -100..100: battles won and land occupied.</summary>
	public double Score { get; set; }
	/// <summary>Score from battles alone (occupation is counted live).</summary>
	public double BattleScore { get; set; }
	/// <summary>Battles fought, most recent last.</summary>
	public List<BattleRecord> Battles { get; } = new();
	/// <summary>A peace a country run by the game has offered the player, awaiting an answer; null if none.</summary>
	public PeaceOffer Offer { get; set; }

	public bool IsAttacker(string tag) => Attackers.Contains(tag);
	public bool IsDefender(string tag) => Defenders.Contains(tag);
	public bool Includes(string tag) => IsAttacker(tag) || IsDefender(tag);
	/// <summary>The side of a country, or 0 if it is not in the war.</summary>
	public int SideOf(string tag) => IsAttacker(tag) ? 1 : IsDefender(tag) ? -1 : 0;
	public bool AreEnemies(string a, string b) => SideOf(a) * SideOf(b) < 0;
	public string Name(IReadOnlyDictionary<string, Country> countries) =>
		$"{(countries.TryGetValue(AttackerTag, out Country a) ? a.Adjective : AttackerTag)} war on {(countries.TryGetValue(DefenderTag, out Country d) ? d.Name : DefenderTag)}";
}

public sealed record BattleRecord(GameDate Date, int ProvinceId, string AttackerTag, string DefenderTag, bool AttackerWon, int AttackerMen, int DefenderMen, int AttackerLost, int DefenderLost);

/// <summary>A peace offered to the player: the terms the winning side (<see cref="WinnerSide"/>) gets.</summary>
public sealed record PeaceOffer(string FromTag, int WinnerSide, PeaceTerms Terms, GameDate Expires);

/// <summary>What a side asks for in a peace: provinces, gold, or nothing (a white peace).</summary>
public sealed class PeaceTerms
{
	/// <summary>Provinces the winner takes (ids).</summary>
	public List<int> Provinces { get; } = new();
	public double Gold { get; set; }
	public bool IsWhitePeace => Provinces.Count == 0 && Gold <= 0;
}
