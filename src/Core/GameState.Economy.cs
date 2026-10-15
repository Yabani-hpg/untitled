using System;
using System.Linq;
using Godot;
using Untitled.Data;
using Untitled.Rules;

namespace Untitled.Core;

// Loans and bankruptcy.
public partial class GameState
{
	public string LoanObstacle(Country c)
	{
		if (c == null)
			return "No country";
		if (c.IsBankrupt(Date))
			return $"Bankrupt: no one lends to us until {Definitions.DefaultCalendar.Format(c.BankruptUntil)}";
		if (c.Loans.Count >= EconomyRules.MaxLoans)
			return $"No one will lend us more: we have {EconomyRules.MaxLoans} loans already";
		return null;
	}

	/// <summary>Borrows a loan sized to the realm's income, at interest, due in a few years.</summary>
	public bool TakeLoan(out string reason) => TakeLoanFor(PlayerCountry, out reason);

	bool TakeLoanFor(Country c, out string reason)
	{
		reason = LoanObstacle(c);
		if (reason != null)
			return false;
		double amount = EconomyRules.LoanSize(c);
		c.Loans.Add(new Loan(amount, EconomyRules.LoanRate + 0.01 * c.Loans.Count, Date.AddDays((long)(EconomyRules.LoanYears * 365.2425))));
		c.Gold += amount;
		if (c.Tag == PlayerTag)
			Notify($"We borrow {amount:0} gold at {c.Loans[^1].Rate:P0} a year, due in {EconomyRules.LoanYears} years.");
		EmitSignal(SignalName.LawsChanged);
		return true;
	}

	/// <summary>Repays the oldest loan, if the treasury holds its amount.</summary>
	public bool RepayLoan(out string reason)
	{
		Country c = PlayerCountry;
		reason = null;
		if (c == null || c.Loans.Count == 0)
			reason = "We owe nothing";
		else if (c.Gold < c.Loans[0].Amount)
			reason = $"The loan is {c.Loans[0].Amount:0} gold; we have {c.Gold:0}";
		if (reason != null)
			return false;
		c.Gold -= c.Loans[0].Amount;
		c.Loans.RemoveAt(0);
		EmitSignal(SignalName.LawsChanged);
		return true;
	}

	/// <summary>The crown repudiates its debts: the loans are gone, and so is its credit, its stability and much of its standing.</summary>
	public bool DeclareBankruptcy(out string reason) => DeclareBankruptcyFor(PlayerCountry, out reason);

	bool DeclareBankruptcyFor(Country c, out string reason)
	{
		reason = c == null ? "No country" : c.Loans.Count == 0 ? "We owe nothing" : c.IsBankrupt(Date) ? "Already bankrupt" : null;
		if (reason != null)
			return false;
		c.Loans.Clear();
		c.Gold = Math.Max(0, c.Gold);
		StateRules.ChangeStability(c, -3);
		c.Legitimacy = Math.Max(0, c.Legitimacy - 20);
		c.Prestige = Math.Clamp(c.Prestige - 30, -100, 100);
		c.BankruptUntil = Date.AddDays((long)(EconomyRules.BankruptcyYears * 365.2425));
		foreach (Country o in SettledCountries.Where(o => o != c && AreNeighbours(o.Tag, c.Tag)))
			DiplomacyRules.AddModifier(o, c.Tag, "Repudiated its debts", -20, Date, 10);
		if (c.Tag == PlayerTag)
			Notify($"The crown is bankrupt: our debts are repudiated, the realm shaken, and tax falls for {EconomyRules.BankruptcyYears} years.");
		else if (AreNeighbours(PlayerTag, c.Tag))
			Notify($"{c.Name} is bankrupt.");
		EmitSignal(SignalName.LawsChanged);
		return true;
	}

	/// <summary>A country run by the game borrows when its treasury runs dry, and goes bankrupt when it can borrow no more.</summary>
	void AiFinances(Country c)
	{
		if (c.Gold >= 0)
			return;
		if (LoanObstacle(c) == null)
			TakeLoanFor(c, out _);
		else if (c.Gold < -EconomyRules.LoanSize(c) && c.Loans.Count > 0)
			DeclareBankruptcyFor(c, out _);
	}
}
