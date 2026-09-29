using System.Collections.Generic;
using Untitled.Core;

namespace Untitled.Data;

public enum CalendarKind
{
	/// <summary>Gregorian months and leap years; the year is shifted by <see cref="Calendar.YearOffset"/>.</summary>
	Solar,
	/// <summary>The tabular Islamic lunar calendar, counted from the Hijra (16 July 622 AD, Julian).</summary>
	Hijri,
}

/// <summary>A date as one calendar writes it.</summary>
public readonly record struct CalendarDate(int Day, string Month, long Year, string Era)
{
	/// <summary>"17 November 2026 AD"</summary>
	public override string ToString() => $"{Day} {Month} {Year} {Era}";
}

/// <summary>
/// A way of writing the date. The game counts time in the Holocene calendar (<see cref="GameDate"/>);
/// each calendar converts from it. Some only come into use after a world event (the Hijri calendar after
/// the Hijra) and are adopted by the countries of certain religions.
/// </summary>
public sealed class Calendar
{
	public static readonly string[] GregorianMonths =
	{
		"January", "February", "March", "April", "May", "June",
		"July", "August", "September", "October", "November", "December",
	};

	/// <summary>Julian Day Number of 1 Muharram 1 AH in the tabular (civil) Hijri calendar.</summary>
	const long HijriEpoch = 1948440;

	public string Id { get; init; }
	public string Name { get; init; }
	public CalendarKind Kind { get; init; }
	/// <summary>Solar calendars: year = astronomical year (1 BC = 0) + offset.</summary>
	public long YearOffset { get; init; }
	public string Era { get; init; }
	/// <summary>Era for years before 1, counted backwards with no year 0 ("1200 BC"); null to keep counting down.</summary>
	public string EraBefore { get; init; }
	public IReadOnlyList<string> Months { get; init; } = GregorianMonths;
	/// <summary>World flag that enables the calendar, or null if it is always available.</summary>
	public string RequiresFlag { get; init; }
	/// <summary>Religions whose countries use this calendar.</summary>
	public IReadOnlyList<string> AdoptedByReligions { get; init; } = new List<string>();
	public bool IsDefault { get; init; }

	public bool IsEnabled(IReadOnlySet<string> worldFlags) => RequiresFlag == null || worldFlags.Contains(RequiresFlag);

	public CalendarDate Convert(GameDate date)
	{
		if (Kind == CalendarKind.Hijri)
		{
			var (hy, hm, hd) = DayToHijri(date.Day);
			return hy < 1 && EraBefore != null
				? new CalendarDate(hd, Months[hm - 1], 1 - hy, EraBefore)
				: new CalendarDate(hd, Months[hm - 1], hy, Era);
		}
		var (y, m, d) = GameDate.DayToGregorian(date.Day);
		long year = y + YearOffset;
		if (year < 1 && EraBefore != null)
			return new CalendarDate(d, Months[m - 1], 1 - year, EraBefore);
		return new CalendarDate(d, Months[m - 1], year, Era);
	}

	public string Format(GameDate date) => Convert(date).ToString();

	/// <summary>Tabular Islamic calendar (30-year cycle of 11 leap years) from a Julian Day Number.</summary>
	static (long Year, int Month, int Day) DayToHijri(long jdn)
	{
		long l = jdn - HijriEpoch + 10632;
		long n = FloorDiv(l - 1, 10631);
		l = l - 10631 * n + 354;
		long j = (10985 - l) / 5316 * (50 * l / 17719) + l / 5670 * (43 * l / 15238);
		l = l - (30 - j) / 15 * (17719 * j / 50) - j / 16 * (15238 * j / 43) + 29;
		long m = 24 * l / 709;
		long d = l - 709 * m / 24;
		long y = 30 * n + j - 30;
		return (y, (int)m, (int)d);
	}

	static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}
