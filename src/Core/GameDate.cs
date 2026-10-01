using System;

namespace Untitled.Core;

/// <summary>
/// A day in the game, counted as a Julian Day Number so that every calendar converts from one integer.
/// The Holocene calendar governs the game: dates in data and saves are Holocene dates (year = astronomical
/// year + 10000, Gregorian months and leap years), so 1 January 8201 HE is 1 January 1800 BC.
/// </summary>
public readonly record struct GameDate(long Day) : IComparable<GameDate>
{
	public const int HoloceneOffset = 10000;

	public static GameDate FromHolocene(int year, int month, int day) =>
		new(GregorianToDay(year - HoloceneOffset, month, day));

	/// <summary>Holocene year, month (1-12) and day (1-31).</summary>
	public (int Year, int Month, int Day) Holocene
	{
		get
		{
			var (y, m, d) = DayToGregorian(Day);
			return (y + HoloceneOffset, m, d);
		}
	}

	public GameDate AddDays(long days) => new(Day + days);

	public int CompareTo(GameDate other) => Day.CompareTo(other.Day);

	public static bool operator <(GameDate a, GameDate b) => a.Day < b.Day;
	public static bool operator >(GameDate a, GameDate b) => a.Day > b.Day;
	public static bool operator <=(GameDate a, GameDate b) => a.Day <= b.Day;
	public static bool operator >=(GameDate a, GameDate b) => a.Day >= b.Day;

	// --- proleptic Gregorian calendar with astronomical years (year 0 = 1 BC) <-> Julian Day Number ---

	public static long GregorianToDay(long year, int month, int day)
	{
		long a = (14 - month) / 12;
		long y = year + 4800 - a;
		long m = month + 12 * a - 3;
		return day + (153 * m + 2) / 5 + 365 * y + FloorDiv(y, 4) - FloorDiv(y, 100) + FloorDiv(y, 400) - 32045;
	}

	/// <summary>Richards' algorithm; exact for every day from 4713 BC on.</summary>
	public static (int Year, int Month, int Day) DayToGregorian(long jdn)
	{
		long a = jdn + 32044;
		long b = (4 * a + 3) / 146097;
		long c = a - 146097 * b / 4;
		long d = (4 * c + 3) / 1461;
		long e = c - 1461 * d / 4;
		long m = (5 * e + 2) / 153;
		int day = (int)(e - (153 * m + 2) / 5 + 1);
		int month = (int)(m + 3 - 12 * (m / 10));
		int year = (int)(100 * b + d - 4800 + m / 10);
		return (year, month, day);
	}

	public static int DaysInMonth(long year, int month) => month switch
	{
		2 => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0) ? 29 : 28,
		4 or 6 or 9 or 11 => 30,
		_ => 31,
	};

	static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}
