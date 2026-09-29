using Untitled.Core;

namespace Untitled.Data;

/// <summary>
/// A person: for now, the rulers of countries. Every character belongs to a population, a group of
/// one culture, religion and occupation living in a province.
/// </summary>
public sealed class Character
{
	public int Id { get; init; }
	public string Name { get; set; }
	/// <summary>The full regnal name ("Userkheperure Setepenre Seti Merenptah"), or null.</summary>
	public string FullName { get; set; }
	public string Dynasty { get; set; }
	public GameDate Birth { get; set; }
	/// <summary>res:// path of the portrait PNG.</summary>
	public string Portrait { get; set; }

	// the population the character belongs to
	public int ProvinceId { get; set; }
	public Culture Culture { get; set; }
	public Religion Religion { get; set; }
	public Occupation Occupation { get; set; }

	/// <summary>Age in whole years on <paramref name="date"/>.</summary>
	public int AgeOn(GameDate date)
	{
		var (by, bm, bd) = Birth.Holocene;
		var (y, m, d) = date.Holocene;
		int age = y - by;
		if (m < bm || m == bm && d < bd)
			age--;
		return age;
	}

	/// <summary>The population group the character belongs to, if it still lives in its province.</summary>
	public PopGroup Population(Province province)
	{
		if (province == null)
			return null;
		foreach (PopGroup pop in province.Pops)
		{
			if (pop.Culture == Culture && pop.Religion == Religion && pop.Occupation == Occupation)
				return pop;
		}
		return null;
	}
}
