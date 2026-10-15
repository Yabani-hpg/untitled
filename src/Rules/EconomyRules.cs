using System;
using System.Collections.Generic;
using System.Linq;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>A settled country's monthly accounts, in gold.</summary>
public readonly record struct Ledger(double Tax, double Garrisons, double Mercenaries, double Armies = 0, double Trade = 0, double Production = 0, double Interest = 0, double Advisors = 0, double Missionaries = 0)
{
	public double Income => Tax + Trade + Production;
	public double Balance => Income - Garrisons - Mercenaries - Armies - Interest - Advisors - Missionaries;
}

/// <summary>An unsettled country's monthly food accounts.</summary>
public readonly record struct FoodLedger(double Surplus, double WarBands, double Barter)
{
	public double Balance => Surplus + Barter - WarBands;
}

/// <summary>
/// Treasuries. Settled countries keep gold, taxed from their settled people (peasants); tribesmen and
/// nomads living in their provinces pay no tax, and nor do people away under arms. Gold pays armies,
/// garrisons, mercenaries and buildings.
/// Unsettled countries keep food: the surplus of their herds and fields, which feeds their war bands and
/// is what they barter with others. Gold paid to them (for mercenaries, as gifts) they barter into food.
/// </summary>
public static class EconomyRules
{
	/// <summary>Gold a month from each population unit (1000 people) of settled people.</summary>
	public const double TaxPerUnit = 0.025;
	/// <summary>Gold a month for each regiment standing in a garrison.</summary>
	public const double GarrisonUpkeep = 0.2;
	/// <summary>Gold a month for each regiment of hired tribal warriors.</summary>
	public const double MercenaryPay = 1.5;
	/// <summary>Gold for a building's first level; each further level costs this much more.</summary>
	public const double BuildCostPerLevel = 25;
	public const double StartingGold = 200;
	/// <summary>Loans: yearly interest, years until due, how many at once, and what a bankruptcy costs.</summary>
	public const double LoanRate = 0.04, BankruptTaxFactor = 0.6;
	public const int LoanYears = 5, MaxLoans = 5, BankruptcyYears = 5;
	/// <summary>A loan is sized to the realm: this many months of its income, at least a minimum.</summary>
	public static double LoanSize(Country c) => Math.Max(50, Math.Round(6 * Math.Max(0, c.LastLedger.Income)));

	/// <summary>Share of an unsettled people's food that is surplus, beyond what they eat.</summary>
	public const double FoodSurplusShare = 0.25;
	/// <summary>Food a month to keep a regiment of warriors ready.</summary>
	public const double WarBandUpkeep = 0.1;
	/// <summary>Food an unsettled people get for a piece of gold.</summary>
	public const double FoodPerGold = 2;
	/// <summary>Months of surplus a tribe has stored at the start.</summary>
	public const double StartingStoreMonths = 12;

	/// <summary>Gifts: gold sent to a tribe, and the relations it buys.</summary>
	public const double GiftGold = 25;
	public const int GiftRelations = 8;

	// ------------------------------------------------------------------------------------------ gold

	/// <summary>
	/// A province's tax to its controller: its settled people, less its separatism (unrest keeps the
	/// tax collectors out).
	/// </summary>
	public static double Tax(Province p, GameDate today, Country owner)
	{
		if (p.Control == null || p.OccupierTag != null)
			return 0;                         // an occupied province's tax goes nowhere
		double noncitizens = 1 + LawRules.Mod(owner, "noncitizen_tax");
		double tax = 0;
		foreach (PopGroup pop in p.Pops)
		{
			if (pop.Occupation.Taxed)
				tax += pop.Units * TaxPerUnit * (LawRules.IsCitizen(owner, pop) ? 1 : noncitizens);
		}
		return Math.Max(0, tax * (1 + LawRules.Mod(owner, "tax") + WorkRules.Mod(p, "tax")) * StateRules.TaxFactor(owner) * (1 - ControlRules.Separatism(p, today, owner)) * (owner.IsBankrupt(today) ? BankruptTaxFactor : 1));
	}

	public static Ledger MonthlyLedger(Country c, IEnumerable<Province> owned, IEnumerable<Tribe> tribes, IEnumerable<Army> armies, GameDate today, TradeReport trade = null)
	{
		double tax = 0, garrisons = 0;
		foreach (Province p in owned)
		{
			tax += Tax(p, today, c);
			garrisons += (p.Control?.Garrison.Count ?? 0) * GarrisonUpkeep + p.FortLevel * FortRules.UpkeepPerLevel;
		}
		double upkeep = armies.Where(a => a.OwnerTag == c.Tag).Sum(MilitaryRules.Upkeep);
		return new Ledger(tax, garrisons, TribeRules.Mercenaries(tribes, c.Tag) * MercenaryPayOf(c), upkeep,
			trade?.Income.GetValueOrDefault(c.Tag) ?? 0, trade?.Production.GetValueOrDefault(c.Tag) ?? 0, c.Loans.Sum(l => l.Amount * l.Rate / 12), AdvisorRules.Salaries(c),
			c.Converting.Count * ReligionRules.MissionaryUpkeep);
	}

	/// <summary>Gold a month a country pays each regiment of mercenaries (its laws may make them cheaper).</summary>
	public static double MercenaryPayOf(Country c) => MercenaryPay * Math.Max(0, 1 + LawRules.Mod(c, "mercenary_pay"));

	/// <summary>Gold for the government to build a building, or its next level.</summary>
	public static double BuildCost(Province p, BuildingType type, Country owner) =>
		BuildCostPerLevel * ((p.GetBuilding(type)?.Level ?? 0) + 1) * Math.Max(0.2, 1 + LawRules.Mod(owner, "building_cost"));

	// ------------------------------------------------------------------------------------------ food

	/// <summary>The food surplus of a tribe's lands: what their herds and fields yield, beyond what they eat.</summary>
	public static double FoodSurplus(Tribe t, IReadOnlyList<Province> provinces, Definitions defs)
	{
		double food = 0;
		foreach (int id in t.Provinces)
			food += ProductionRules.Compute(provinces[id], defs).FoodOutput;
		return food * FoodSurplusShare;
	}

	/// <summary>
	/// A tribe's food accounts: surplus, the upkeep of its war bands (those at home; mercenaries are paid
	/// by their employer) and what its mercenaries' gold barters for.
	/// </summary>
	public static FoodLedger MonthlyFood(Tribe t, IReadOnlyList<Province> provinces, Definitions defs)
	{
		int atHome = 0;
		foreach (int id in t.Provinces)
			atHome += ControlRules.Warriors(provinces[id]);
		atHome = Math.Max(0, atHome - t.HiredRegiments);
		return new FoodLedger(FoodSurplus(t, provinces, defs), atHome * WarBandUpkeep, t.HiredRegiments * MercenaryPay * FoodPerGold);
	}

	/// <summary>Warriors fight at half strength when their people have no food left to keep them.</summary>
	public static bool Starving(Tribe t) => t.Food <= 0;

	/// <summary>
	/// One month for every treasury: settled countries collect tax and pay their garrisons and mercenaries;
	/// those that can't pay their mercenaries see them go home. Tribes add their food balance.
	/// Returns the countries whose mercenaries left unpaid.
	/// </summary>
	public static List<Country> MonthlyStep(IReadOnlyDictionary<string, Country> countries, IReadOnlyList<Province> provinces,
		ICollection<Tribe> tribes, IReadOnlyCollection<Army> armies, Definitions defs, GameDate today, TradeReport trade = null)
	{
		var unpaid = new List<Country>();
		var owned = provinces.Where(p => p?.OwnerTag != null).GroupBy(p => p.OwnerTag).ToDictionary(g => g.Key, g => g.ToList());
		foreach (Country c in countries.Values)
		{
			if (!owned.TryGetValue(c.Tag, out List<Province> list))
				continue;
			Ledger ledger = MonthlyLedger(c, list, tribes, armies, today, trade);
			c.LastLedger = ledger;
			c.Gold += ledger.Balance;
			// loans fall due: paid off when the treasury can, else renewed at a higher rate
			for (int i = 0; i < c.Loans.Count; i++)
			{
				Loan loan = c.Loans[i];
				if (loan.Due > today)
					continue;
				if (c.Gold >= loan.Amount)
				{
					c.Gold -= loan.Amount;
					c.Loans.RemoveAt(i--);
				}
				else
					c.Loans[i] = loan with { Rate = loan.Rate + 0.01, Due = today.AddDays((long)(LoanYears * 365.2425)) };
			}
			if (c.Gold < 0 && ledger.Mercenaries > 0)
			{
				// no pay, no mercenaries
				foreach (Tribe t in tribes.Where(t => t.AlliedTag == c.Tag))
					t.HiredRegiments = 0;
				unpaid.Add(c);
			}
		}
		foreach (Tribe t in tribes)
		{
			if (t.Provinces.Count == 0)
				continue;
			FoodLedger food = MonthlyFood(t, provinces, defs);
			t.LastFood = food;
			t.Food = Math.Max(0, t.Food + food.Balance);
		}
		return unpaid;
	}
}
