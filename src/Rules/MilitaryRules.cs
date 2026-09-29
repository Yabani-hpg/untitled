using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Rules;

/// <summary>
/// Armies. Until standing armies exist, soldiers are levies: when the levies are raised, the country takes
/// a share of those who can serve (men, and women if its laws let them) from its provinces' population
/// units, each unit of 1000 becoming a regiment of foot, mounted or siege troops. The people levied leave
/// their fields, so they pay no tax and work no land until they are disbanded and the survivors go home.
/// </summary>
public static class MilitaryRules
{
	/// <summary>Km per pixel of the province map (about 40,000 km around the world in 5632 pixels).</summary>
	public const double KmPerPixel = 7.1;
	/// <summary>Extra days to cross a river that runs along a border.</summary>
	public const int RiverCrossingDays = 2;
	/// <summary>A regiment with fewer than this share of its men left breaks up.</summary>
	public const double BrokenBelow = 0.1;
	/// <summary>Siege engines and their crews take this much more of the losses.</summary>
	public const double SiegeVulnerability = 1.5;

	// ------------------------------------------------------------------------------------ unit types

	public static bool IsAvailable(UnitType t, IWorld world, Country c) => t.Requires == null || t.Requires.Holds(world, c);

	public static List<UnitType> AvailableTypes(Definitions defs, IWorld world, Country c) =>
		defs.UnitTypes.Where(t => IsAvailable(t, world, c)).ToList();

	/// <summary>The cheapest foot the country can always raise.</summary>
	public static UnitType BasicFoot(Definitions defs) =>
		defs.UnitTypes.Where(t => t.Category == UnitCategory.Foot && t.Requires == null).OrderBy(t => t.Cost).First();

	/// <summary>The country's levy composition: percent for each unit type it can raise now (from its own choices, or the defaults).</summary>
	public static List<(UnitType Type, int Share)> Template(Country c, Definitions defs, IWorld world)
	{
		var types = AvailableTypes(defs, world, c);
		var list = types.Select(t => (t, c.LevyTemplate.TryGetValue(t.Id, out int s) ? s : c.LevyTemplate.Count == 0 ? t.DefaultShare : 0))
			.Where(x => x.Item2 > 0).ToList();
		return list.Count > 0 ? list : new List<(UnitType, int)> { (BasicFoot(defs), 100) };
	}

	/// <summary>
	/// The arms for <paramref name="n"/> new regiments, by the template's shares; the mounted and siege
	/// regiments the treasury can't equip are raised as basic foot instead. Returns them with their cost.
	/// </summary>
	public static (List<UnitType> Types, double Cost) Arm(Country c, int n, Definitions defs, IWorld world)
	{
		var template = Template(c, defs, world);
		double total = template.Sum(x => x.Share);
		var counts = template.Select(x => (int)Math.Floor(n * x.Share / total)).ToArray();
		// the rest by largest remainder
		foreach (int i in Enumerable.Range(0, template.Count).OrderByDescending(i => n * template[i].Share / total - counts[i]).Take(n - counts.Sum()))
			counts[i]++;
		var types = new List<UnitType>();
		for (int i = 0; i < template.Count; i++)
			types.AddRange(Enumerable.Repeat(template[i].Type, counts[i]));
		// can't afford the horses and engines: the most expensive become foot first
		UnitType basic = BasicFoot(defs);
		double cost = types.Sum(t => t.Cost);
		foreach (int i in Enumerable.Range(0, types.Count).OrderByDescending(i => types[i].Cost).ToList())
		{
			if (cost <= c.Gold)
				break;
			cost -= types[i].Cost - basic.Cost;
			types[i] = basic;
		}
		// foot, then mounted, then siege
		types.Sort((a, b) => a.Category.CompareTo(b.Category));
		return (types, cost);
	}

	// ------------------------------------------------------------------------------------ levies

	/// <summary>Whether people of this group may be levied: men, and women where the law lets them serve.</summary>
	public static bool CanServe(Country c, PopGroup pop) => pop.IsMale || LawRules.Effect(c, "women_serve") > 0;

	/// <summary>Share of those who can serve that the levy obligation calls up.</summary>
	public static double LevyShare(Country c)
	{
		double share = LawRules.Effect(c, "levy_share");
		return share > 0 ? Math.Min(1, share) : 0.1;
	}

	/// <summary>
	/// How much of a population group answers the levy, 0..1: citizens in full, non-citizens as far as the
	/// law on them says; absorbed tribes less their separatism. Subjugated nomads send nobody.
	/// </summary>
	public static double LevyWeight(Country c, Province p, PopGroup pop, GameDate today)
	{
		if (p.OwnerTag != c.Tag || p.Control == null || p.Control.Kind == ControlKind.Subjugated || !CanServe(c, pop))
			return 0;
		double w = LawRules.IsCitizen(c, pop) ? 1 : Math.Clamp(LawRules.Mod(c, "noncitizen_manpower"), 0, 1);
		return w * (1 - ControlRules.Separatism(p, today, c));
	}

	/// <summary>Units at home who could be levied, weighted as <see cref="LevyWeight"/>.</summary>
	public static double CanServeAtHome(Country c, IEnumerable<Province> owned, GameDate today)
	{
		double units = 0;
		foreach (Province p in owned)
		{
			foreach (PopGroup pop in p.Pops)
				units += pop.Units * LevyWeight(c, p, pop, today);
		}
		return units;
	}

	/// <summary>Regiments under arms: in the country's armies and its garrisons.</summary>
	public static int UnderArms(Country c, IEnumerable<Army> armies, IEnumerable<Province> owned) =>
		armies.Where(a => a.OwnerTag == c.Tag).Sum(a => a.Regiments.Count)
		+ owned.Sum(p => p.Control?.Garrison.Count ?? 0);

	/// <summary>
	/// Regiments the country can still levy: its levy share of all who can serve (those at home and those
	/// already under arms), less those under arms.
	/// </summary>
	public static int AvailableLevies(Country c, IReadOnlyCollection<Province> owned, IEnumerable<Army> armies, GameDate today)
	{
		int under = UnderArms(c, armies, owned);
		double home = CanServeAtHome(c, owned, today);
		return Math.Max(0, Math.Min((int)home, (int)Math.Floor(LevyShare(c) * (home + under)) - under));
	}

	/// <summary>
	/// Takes regiments from the population: one unit of people for each, drawn from every group that can
	/// serve in proportion to how many answer the levy. The last unit of men and of women in the country
	/// always stay home. Returns the regiments, armed as <paramref name="types"/> (as many as could be raised).
	/// </summary>
	public static List<Regiment> Levy(Country c, IReadOnlyList<Province> owned, IReadOnlyList<UnitType> types, GameDate today)
	{
		int n = types.Count;
		var pool = new List<(Province P, PopGroup G, double W)>();
		foreach (Province p in owned)
		{
			foreach (PopGroup g in p.Pops)
			{
				double w = g.Units * LevyWeight(c, p, g, today);
				if (w > 0)
					pool.Add((p, g, w));
			}
		}
		var regiments = new List<Regiment>();
		double total = pool.Sum(x => x.W);
		if (total <= 0 || n <= 0)
			return regiments;

		// each group gives its share of the regiments, the remainders going to the largest fractions
		var exact = pool.Select(x => Math.Min(x.G.Units, n * x.W / total)).ToArray();
		var take = exact.Select(e => (int)Math.Floor(e)).ToArray();
		int left = n - take.Sum();
		foreach (int i in Enumerable.Range(0, pool.Count).OrderByDescending(i => exact[i] - take[i]))
		{
			if (left <= 0)
				break;
			if (take[i] < pool[i].G.Units)
			{
				take[i]++;
				left--;
			}
		}
		var remaining = new Dictionary<Sex, int>
		{
			[Sex.Male] = owned.Sum(p => p.UnitsOf(Sex.Male)),
			[Sex.Female] = owned.Sum(p => p.UnitsOf(Sex.Female)),
		};
		for (int i = 0; i < pool.Count; i++)
		{
			var (p, g, _) = pool[i];
			for (int k = 0; k < take[i] && regiments.Count < n && remaining[g.Sex] > 1 && g.Units > 0; k++)
			{
				g.Units--;
				remaining[g.Sex]--;
				regiments.Add(new Regiment
				{
					Type = types[regiments.Count],
					HomeProvinceId = p.Id,
					Culture = g.Culture,
					Religion = g.Religion,
					Occupation = g.Occupation,
					Sex = g.Sex,
				});
			}
		}
		foreach (Province p in owned)
			p.Pops.RemoveAll(g => g.Units <= 0);
		return regiments;
	}

	/// <summary>
	/// Regiments disbanded: the survivors go home and are people again. A regiment at 60% strength brings
	/// its unit home 60% of the time; the rest fell. Returns the units that came home.
	/// </summary>
	public static int SendHome(IEnumerable<Regiment> regiments, IReadOnlyList<Province> provinces, Random rng)
	{
		int home = 0;
		foreach (Regiment r in regiments)
		{
			Province p = r.HomeProvinceId > 0 && r.HomeProvinceId < provinces.Count ? provinces[r.HomeProvinceId] : null;
			if (p == null || p.IsWater || r.Culture == null || r.Religion == null || r.Occupation == null)
				continue;
			if (rng.NextDouble() < r.Strength)
			{
				p.AddPops(r.Culture, r.Religion, r.Occupation, r.Sex, 1);
				home++;
			}
		}
		return home;
	}

	// ------------------------------------------------------------------------------------ battle

	/// <summary>How well a kind of troops fights on a province's ground: horses hate mountains and forests, and love open desert and steppe.</summary>
	public static double TerrainFactor(UnitCategory category, Province p)
	{
		if (p == null)
			return 1;
		return category switch
		{
			UnitCategory.Mounted when p.HasFeature("mountains") => 0.5,
			UnitCategory.Mounted when p.HasFeature("forest") || p.HasFeature("hills") => 0.75,
			UnitCategory.Mounted when p.HasFeature("desert") || p.HasFeature("steppe") => 1.2,
			UnitCategory.Siege when p.HasFeature("mountains") => 0.7,
			_ => 1,
		};
	}

	/// <summary>Siege regiments screened by foot: one regiment of foot for each engine. The rest add nothing, and are lost with a lost battle.</summary>
	static HashSet<Regiment> Screened(IReadOnlyCollection<Regiment> regiments)
	{
		int foot = regiments.Count(r => r.Type.Category == UnitCategory.Foot);
		return regiments.Where(r => r.Type.Category == UnitCategory.Siege).OrderByDescending(r => r.Type.Attack * r.Strength).Take(foot).ToHashSet();
	}

	/// <summary>A side's regiments as a force in battle on <paramref name="where"/>'s ground.</summary>
	public static ControlRules.Force ForceOf(IReadOnlyCollection<Regiment> regiments, Province where, int diceModifier)
	{
		var screened = Screened(regiments);
		double strength = 0, defense = 0, men = 0;
		foreach (Regiment r in regiments)
		{
			men += r.Strength;
			defense += r.Strength * r.Type.Defense;
			if (r.Type.Category == UnitCategory.Siege && !screened.Contains(r))
				continue;
			strength += r.Strength * r.Type.Attack * TerrainFactor(r.Type.Category, where) * (r.Fierce ? ControlRules.FierceMultiplier : 1);
		}
		return new ControlRules.Force(regiments.Count, strength, diceModifier, men > 0 ? defense / men : 1);
	}

	/// <summary>
	/// A battle's losses on one side's regiments: each loses its share of men (less for good defense, more
	/// for siege crews); regiments left too weak break up, and so do unscreened engines if the battle was
	/// lost. Returns the regiments gone, for their armies and garrisons to drop.
	/// </summary>
	public static HashSet<Regiment> TakeLosses(IReadOnlyCollection<Regiment> regiments, double share, bool lost)
	{
		var gone = new HashSet<Regiment>();
		if (regiments.Count == 0)
			return gone;
		var screened = Screened(regiments);
		double men = regiments.Sum(r => r.Strength);
		double defense = men > 0 ? regiments.Sum(r => r.Strength * r.Type.Defense) / men : 1;
		foreach (Regiment r in regiments)
		{
			bool siege = r.Type.Category == UnitCategory.Siege;
			if (siege && lost && !screened.Contains(r))
			{
				gone.Add(r);
				continue;
			}
			double loss = share * defense / r.Type.Defense * (siege ? SiegeVulnerability : 1);
			r.Strength *= Math.Max(0, 1 - loss);
			if (r.Strength < BrokenBelow)
				gone.Add(r);
		}
		return gone;
	}

	// ------------------------------------------------------------------------------------ movement

	/// <summary>An army marches at the pace of its slowest regiment, in km a day.</summary>
	public static double Speed(Army a) => a.Regiments.Count == 0 ? 20 : a.Regiments.Min(r => r.Type.Speed);

	/// <summary>Days to march from a province into its neighbour: the distance between their centres, slowed by rough ground and river crossings.</summary>
	public static int StepDays(Province from, Province to, double speedKm, Func<int, Vector2?> centroid, float mapWidth)
	{
		double km = 50;
		if (centroid(from.Id) is Vector2 a && centroid(to.Id) is Vector2 b)
		{
			float dx = Mathf.Abs(a.X - b.X);
			dx = Mathf.Min(dx, mapWidth - dx);          // the map wraps around east to west
			km = Math.Sqrt(dx * dx + (a.Y - b.Y) * (a.Y - b.Y)) * KmPerPixel;
		}
		double ground = to.HasFeature("mountains") ? 2.0
			: to.HasFeature("hills") || to.HasFeature("forest") ? 1.4
			: to.HasFeature("desert") ? 1.25 : 1.0;
		int days = (int)Math.Ceiling(km / speedKm * ground);
		if (from.GetAdjacency(to)?.IsRiverCrossing == true)
			days += RiverCrossingDays;
		return Math.Max(1, days);
	}

	/// <summary>The quickest way over land from one province to another (not including the start), or null if there is none.</summary>
	public static List<int> FindPath(Province from, Province to, double speedKm, IReadOnlyList<Province> provinces, Func<int, Vector2?> centroid, float mapWidth)
	{
		if (from == null || to == null || to.IsWater)
			return null;
		if (from == to)
			return new List<int>();
		var best = new Dictionary<int, int> { [from.Id] = 0 };
		var previous = new Dictionary<int, int>();
		var queue = new PriorityQueue<int, int>();
		queue.Enqueue(from.Id, 0);
		while (queue.TryDequeue(out int id, out int days))
		{
			if (id == to.Id)
				break;
			if (days > best[id])
				continue;
			Province p = provinces[id];
			foreach (Adjacency link in p.Neighbors)
			{
				if (!MovementRules.CanMove(UnitDomain.Land, link))
					continue;
				int d = days + StepDays(p, link.To, speedKm, centroid, mapWidth);
				if (best.TryGetValue(link.To.Id, out int known) && known <= d)
					continue;
				best[link.To.Id] = d;
				previous[link.To.Id] = id;
				queue.Enqueue(link.To.Id, d);
			}
		}
		if (!previous.ContainsKey(to.Id))
			return null;
		var path = new List<int>();
		for (int id = to.Id; id != from.Id; id = previous[id])
			path.Add(id);
		path.Reverse();
		return path;
	}

	/// <summary>Gold a month for an army's regiments.</summary>
	public static double Upkeep(Army a) => a.Regiments.Sum(r => r.Type.Upkeep);

	/// <summary>"1st", "2nd", "3rd", "4th"...</summary>
	public static string Ordinal(int n) =>
		n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}
