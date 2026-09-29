using System;
using System.Collections.Generic;
using Godot;
using Untitled.Data;

namespace Untitled.UI;

/// <summary>
/// Builds the 3D scene of a province's city from simple shapes, the way the original Civilization showed
/// a city: the houses of its people in their culture's art style, a landmark in the middle, the production
/// buildings around it, fields or herds for its food, and the tents of its nomads outside town.
/// Everything is laid out from the province id, so a city looks the same each time it is opened.
/// </summary>
public sealed class CityBuilder
{
	public const float Ground = 0f;

	readonly Node3D _root;
	readonly Random _rng;
	readonly Dictionary<Color, StandardMaterial3D> _materials = new();
	readonly List<(Vector2 Pos, float Radius)> _occupied = new();
	readonly Font _font;
	float _seaZ = float.NegativeInfinity;     // the sea covers everything beyond this z (coastal cities)
	Vector2 _campCentre;

	/// <summary>Radius of the built-up town.</summary>
	public float TownRadius { get; private set; }

	CityBuilder(Node3D root, int seed, Font font)
	{
		_root = root;
		_rng = new Random(seed);
		_font = font;
	}

	public static Node3D Build(Province p, Font font)
	{
		var root = new Node3D { Name = "City" };
		var b = new CityBuilder(root, p.Id * 7919 + 17, font);
		b.BuildCity(p);
		// where the camera should look, and how far the interesting part reaches
		// (the camp centre is zero when there is no camp, or nothing but the camp)
		Vector2 focus = b._campCentre * 0.3f;
		root.SetMeta("focus", new Vector3(focus.X, 0, focus.Y));
		root.SetMeta("extent", b.TownRadius + 14f + b._campCentre.Length() * 0.5f);
		return root;
	}

	// ------------------------------------------------------------------------------------------ layout

	void BuildCity(Province p)
	{
		Culture culture = p.MainCulture;
		string style = culture?.ArtStyle ?? "european";
		var byOccupation = new Dictionary<bool, int>();   // nomadic -> units
		foreach (PopGroup pop in p.Pops)
			byOccupation[pop.Occupation.Nomadic] = byOccupation.GetValueOrDefault(pop.Occupation.Nomadic) + pop.Units;
		int settled = byOccupation.GetValueOrDefault(false);
		int nomads = byOccupation.GetValueOrDefault(true);

		// grows with the square root, so a village of 6000 still looks like a village and a city of 300 000 fits the view
		int houses = settled > 0 ? Math.Clamp((int)MathF.Round(4f * MathF.Sqrt(settled)), 3, 80) : 0;
		TownRadius = 7f + MathF.Sqrt(houses) * 2.6f;

		BuildTerrain(p);
		if (settled > 0 || p.Buildings.Count > 0)
		{
			BuildLandmark(style, settled);
			BuildStreets();
			for (int i = 0; i < houses; i++)
				PlaceHouse(style, i, houses);
		}
		BuildProductionBuildings(p);
		BuildFood(p, settled);
		if (nomads > 0)
			BuildCamp(style, nomads, settled == 0 && p.Buildings.Count == 0, p.HasFeature("river"));
		BuildPeople(p, settled, nomads);
		BuildScenery(p);
	}

	void BuildTerrain(Province p)
	{
		Color ground = new(0.36f, 0.48f, 0.22f);
		if (p.HasFeature("steppe")) ground = new Color(0.55f, 0.55f, 0.28f);
		if (p.HasFeature("forest")) ground = new Color(0.24f, 0.38f, 0.17f);
		if (p.HasFeature("desert")) ground = new Color(0.80f, 0.68f, 0.45f);
		if (p.HasFeature("tundra")) ground = new Color(0.78f, 0.80f, 0.78f);
		AddMesh(new CylinderMesh { TopRadius = 600f, BottomRadius = 600f, Height = 0.4f, RadialSegments = 64 },
			new Vector3(0, -0.2f, 0), ground);

		if (p.HasFeature("coast"))
		{
			// the sea along one side, with a beach
			AddMesh(new BoxMesh { Size = new Vector3(1200f, 0.3f, 600f) }, new Vector3(0, -0.05f, -TownRadius - 324f), new Color(0.16f, 0.36f, 0.52f));
			AddMesh(new BoxMesh { Size = new Vector3(1200f, 0.3f, 5f) }, new Vector3(0, -0.1f, -TownRadius - 23f), new Color(0.83f, 0.76f, 0.56f));
			_seaZ = -TownRadius - 24f;
		}
		if (p.HasFeature("river"))
		{
			// a river past the town, with a bridge on the main street
			float z = TownRadius + 6f;
			if (p.HasFeature("desert"))   // the flood plain: a green belt through the desert
				AddMesh(new BoxMesh { Size = new Vector3(1200f, 0.3f, 2f * TownRadius + 70f) }, new Vector3(0, -0.08f, z - 8f), new Color(0.45f, 0.55f, 0.26f));
			AddMesh(new BoxMesh { Size = new Vector3(1200f, 0.3f, 5f) }, new Vector3(0, -0.02f, z), new Color(0.22f, 0.45f, 0.60f));
			AddMesh(new BoxMesh { Size = new Vector3(3.2f, 0.5f, 7f) }, new Vector3(0, 0.25f, z), new Color(0.47f, 0.36f, 0.24f));
			for (float x = -110f; x < 110f; x += 6f)
				Reserve(new Vector2(x, z), 4f);
		}
	}

	void BuildStreets()
	{
		Color street = new(0.55f, 0.49f, 0.38f);
		float len = TownRadius * 2f + 6f;
		AddMesh(new BoxMesh { Size = new Vector3(len, 0.06f, 2.2f) }, new Vector3(0, 0.02f, 0), street);
		AddMesh(new BoxMesh { Size = new Vector3(2.2f, 0.06f, len) }, new Vector3(0, 0.02f, 0), street);
		AddMesh(new CylinderMesh { TopRadius = 6.5f, BottomRadius = 6.5f, Height = 0.07f, RadialSegments = 32 }, new Vector3(0, 0.02f, 0), street);
		for (float t = -TownRadius; t <= TownRadius; t += 2f)
		{
			Reserve(new Vector2(t, 0), 1.6f);
			Reserve(new Vector2(0, t), 1.6f);
		}
	}

	void PlaceHouse(string style, int index, int count)
	{
		// sunflower spiral from the plaza outward, jittered, skipping streets and taken ground
		for (int attempt = 0; attempt < 12; attempt++)
		{
			float t = (index + 0.5f + attempt * 0.37f) / Math.Max(count, 1);
			float r = 7.5f + (TownRadius - 7.5f) * MathF.Sqrt(t) + Jitter(1.2f);
			float a = index * 2.39996f + attempt * 0.9f + Jitter(0.2f);
			var pos = new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
			if (!IsFree(pos, 1.9f))
				continue;
			Reserve(pos, 1.9f);
			float facing = MathF.Atan2(pos.X, pos.Y);   // face the plaza
			var house = new Node3D { Position = new Vector3(pos.X, Ground, pos.Y), Rotation = new Vector3(0, facing, 0) };
			_root.AddChild(house);
			BuildHouse(house, style);
			return;
		}
	}

	void BuildProductionBuildings(Province p)
	{
		int n = p.Buildings.Count;
		for (int i = 0; i < n; i++)
		{
			Building b = p.Buildings[i];
			// ring just outside town, starting on the side away from the sea
			float t = (i + 0.5f) / Math.Max(n, 1);
			float a = float.IsNegativeInfinity(_seaZ)
				? MathF.PI * 0.5f + t * MathF.Tau + 0.3f
				: -0.08f * MathF.PI + t * 1.16f * MathF.PI;        // the landward half only
			float r = TownRadius + 11f;
			var pos = new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
			for (int k = 0; k < 8 && !IsFree(pos, 4.5f); k++)
			{
				a += 0.18f;
				r += 1.5f;
				pos = new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
			}
			Reserve(pos, 5f);
			var node = new Node3D { Position = new Vector3(pos.X, Ground, pos.Y), Rotation = new Vector3(0, MathF.Atan2(pos.X, pos.Y), 0) };
			_root.AddChild(node);
			float scale = 1.5f + 0.15f * (b.Level - 1);
			node.Scale = new Vector3(scale, scale, scale);
			BuildProductionModel(node, b.Type.Model, b.Level);
			AddLabel($"{b.Type.Name}{(b.Level > 1 ? $"  ({b.Level})" : "")}", new Vector3(pos.X, 5.5f * scale, pos.Y), 36, new Color(1f, 0.93f, 0.75f));
		}
	}

	void BuildFood(Province p, int settled)
	{
		if (p.Food == null)
			return;
		bool herd = p.Food.WorkedBy.Count > 0 && p.Food.WorkedBy[0].Nomadic || p.Food.Id is "cattle" or "sheep" or "horses";
		int patches = Math.Clamp(settled / 3 + 2, 2, 18);
		float r0 = TownRadius + 20f;
		if (p.Food.Id == "fish")
		{
			// boats on the water (or the river), nets drying on the shore
			float z = p.HasFeature("coast") ? -TownRadius - 32f : TownRadius + 6f;
			for (int i = 0; i < Math.Min(patches, 9); i++)
			{
				var boat = new Node3D { Position = new Vector3(-24f + i * 6f + Jitter(1.5f), 0.1f, z + Jitter(p.HasFeature("coast") ? 8f : 0.6f)), Rotation = new Vector3(0, Jitter(0.6f), 0) };
				_root.AddChild(boat);
				AddMesh(boat, new BoxMesh { Size = new Vector3(0.9f, 0.4f, 2.6f) }, new Vector3(0, 0.2f, 0), new Color(0.40f, 0.28f, 0.16f));
				AddMesh(boat, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 1.8f }, new Vector3(0, 1.2f, 0), new Color(0.3f, 0.22f, 0.14f));
			}
			return;
		}
		for (int i = 0; i < patches; i++)
		{
			float a = i * MathF.Tau / patches + Jitter(0.12f) + 0.2f;
			float r = r0 + Jitter(4f) + (i % 2) * 9f;
			var pos = new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
			if (!IsFree(pos, 5f))
				continue;
			Reserve(pos, 5f);
			var field = new Node3D { Position = new Vector3(pos.X, Ground, pos.Y), Rotation = new Vector3(0, a, 0) };
			_root.AddChild(field);
			if (herd)
				BuildPasture(field, p.Food.Id);
			else
				BuildField(field, p.Food);
		}
	}

	void BuildField(Node3D field, ResourceType food)
	{
		Color crop = food.Color;
		if (food.Id == "rice")
			crop = new Color(0.45f, 0.62f, 0.35f);
		// furrowed strips alternating with soil, or flooded paddies for rice
		Color soil = food.Id == "rice" ? new Color(0.35f, 0.5f, 0.55f) : new Color(0.42f, 0.32f, 0.2f);
		AddMesh(field, new BoxMesh { Size = new Vector3(8f, 0.08f, 7f) }, new Vector3(0, 0.04f, 0), soil);
		for (int s = 0; s < 4; s++)
		{
			float h = food.Id is "corn" ? 0.9f : food.Id == "dates" ? 0.2f : 0.35f;
			AddMesh(field, new BoxMesh { Size = new Vector3(1.3f, h, 6.4f) }, new Vector3(-3f + s * 2f, h * 0.5f, 0), crop.Darkened(0.08f * (s % 2)));
		}
		if (food.Id == "dates")
		{
			for (int t = 0; t < 3; t++)
				BuildPalm(field, new Vector3(-3f + t * 3f, 0, Jitter(2f)));
		}
	}

	void BuildPasture(Node3D field, string animal)
	{
		AddMesh(field, new BoxMesh { Size = new Vector3(9f, 0.05f, 8f) }, new Vector3(0, 0.03f, 0), new Color(0.40f, 0.52f, 0.25f));
		// a fence of posts
		for (int i = 0; i < 16; i++)
		{
			float a = i * MathF.Tau / 16f;
			AddMesh(field, new BoxMesh { Size = new Vector3(0.12f, 0.8f, 0.12f) }, new Vector3(MathF.Cos(a) * 4.4f, 0.4f, MathF.Sin(a) * 3.9f), new Color(0.35f, 0.25f, 0.15f));
		}
		int count = 5;
		for (int i = 0; i < count; i++)
			BuildAnimal(field, animal, new Vector3(Jitter(3f), 0, Jitter(2.8f)), Jitter(3f));
	}

	void BuildAnimal(Node3D parent, string animal, Vector3 pos, float yaw)
	{
		var node = new Node3D { Position = pos, Rotation = new Vector3(0, yaw, 0) };
		parent.AddChild(node);
		(Color body, float size, float legs) = animal switch
		{
			"sheep" => (new Color(0.93f, 0.91f, 0.85f), 0.55f, 0.35f),
			"horses" => (new Color(0.45f, 0.30f, 0.18f), 0.65f, 0.75f),
			_ => (new Color(0.50f, 0.34f, 0.22f), 0.75f, 0.5f),
		};
		AddMesh(node, new BoxMesh { Size = new Vector3(size, size * 0.8f, size * 1.8f) }, new Vector3(0, legs + size * 0.4f, 0), body);
		AddMesh(node, new BoxMesh { Size = new Vector3(size * 0.5f, size * 0.55f, size * 0.6f) }, new Vector3(0, legs + size * 0.8f, size * 1.1f), body.Darkened(0.2f));
		foreach (float x in new[] { -0.3f, 0.3f })
		{
			foreach (float z in new[] { -0.6f, 0.6f })
				AddMesh(node, new BoxMesh { Size = new Vector3(0.12f, legs, 0.12f) }, new Vector3(x * size, legs * 0.5f, z * size), body.Darkened(0.3f));
		}
	}

	void BuildCamp(string style, int nomads, bool alone, bool river)
	{
		int tents = Math.Clamp(nomads / 2 + 2, 3, 36);
		// the camp sits off to one side of town, beyond the fields
		// on land: away from the sea (negative z) and on the town's side of the river (positive z)
		bool coast = !float.IsNegativeInfinity(_seaZ);
		float campAngle = (coast ? (river ? 0f : 0.55f) : river ? -0.55f : 0.55f) + Jitter(0.15f);
		float campR = TownRadius + 42f;
		var centre = new Vector2(MathF.Cos(campAngle) * campR, MathF.Sin(campAngle) * campR);
		if (alone)
			centre = Vector2.Zero;   // nothing but the camp
		_campCentre = centre;
		float spread = 5f + MathF.Sqrt(tents) * 2.2f;
		AddMesh(new CylinderMesh { TopRadius = 1.2f, BottomRadius = 1.4f, Height = 0.3f }, new Vector3(centre.X, 0.15f, centre.Y), new Color(0.3f, 0.28f, 0.26f));
		AddMesh(new SphereMesh { Radius = 0.6f, Height = 1.2f }, new Vector3(centre.X, 0.5f, centre.Y), new Color(1f, 0.55f, 0.15f), emissive: true);
		AddLabel("Nomad camp", new Vector3(centre.X, 6f, centre.Y), 38, new Color(0.95f, 0.85f, 0.65f));
		for (int i = 0; i < tents; i++)
		{
			float a = i * 2.39996f;
			float r = 3.5f + spread * MathF.Sqrt((i + 0.5f) / tents);
			var pos = centre + new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
			if (!IsFree(pos, 1.4f))
				continue;
			Reserve(pos, 1.4f);
			var tent = new Node3D { Position = new Vector3(pos.X, Ground, pos.Y), Rotation = new Vector3(0, a, 0) };
			_root.AddChild(tent);
			BuildTent(tent, style);
		}
		// their herds graze around the camp
		for (int i = 0; i < Math.Clamp(nomads / 2, 3, 18); i++)
		{
			float a = Jitter(MathF.PI);
			float r = spread + 4f + Jitter(3f);
			BuildAnimal(_root, i % 3 == 0 ? "horses" : i % 3 == 1 ? "sheep" : "cattle",
				new Vector3(centre.X + MathF.Cos(a) * r, 0, centre.Y + MathF.Sin(a) * r), Jitter(3f));
		}
	}

	void BuildPeople(Province p, int settled, int nomads)
	{
		// one figure for every two units, dressed in their occupation's colour, in the streets and the camp
		var groups = new List<(Occupation Occ, int Figures)>();
		foreach (PopGroup pop in p.Pops)
			groups.Add((pop.Occupation, Math.Clamp(pop.Units / 2, 1, 30)));
		foreach (var (occ, figures) in groups)
		{
			for (int i = 0; i < figures; i++)
			{
				Vector2 pos;
				if (occ.Nomadic)
				{
					float a = Jitter(MathF.PI);
					pos = _campCentre + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (4f + Jitter(1.5f));
				}
				else
				{
					// along the streets and the plaza
					float t = Jitter(TownRadius);
					pos = i % 3 == 0 ? new Vector2(Jitter(5f), Jitter(5f)) : i % 2 == 0 ? new Vector2(t, Jitter(0.8f)) : new Vector2(Jitter(0.8f), t);
				}
				BuildPerson(pos, occ.Color);
			}
		}
	}

	void BuildPerson(Vector2 pos, Color clothes)
	{
		var node = new Node3D { Position = new Vector3(pos.X, 0, pos.Y), Rotation = new Vector3(0, Jitter(3f), 0) };
		_root.AddChild(node);
		AddMesh(node, new CapsuleMesh { Radius = 0.2f, Height = 0.9f, RadialSegments = 6, Rings = 2 }, new Vector3(0, 0.45f, 0), clothes);
		AddMesh(node, new SphereMesh { Radius = 0.15f, Height = 0.3f, RadialSegments = 6, Rings = 3 }, new Vector3(0, 1.05f, 0), new Color(0.72f, 0.52f, 0.38f));
	}

	void BuildScenery(Province p)
	{
		bool forest = p.HasFeature("forest");
		int trees = forest ? 90 : p.HasFeature("desert") || p.HasFeature("tundra") ? 8 : 30;
		for (int i = 0; i < trees; i++)
		{
			float a = Jitter(MathF.PI);
			float r = TownRadius + 14f + MathF.Abs(Jitter(60f));
			var pos = new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
			if (!IsFree(pos, 1.5f))
				continue;
			Reserve(pos, 1.2f);
			var at = new Vector3(pos.X, 0, pos.Y);
			if (p.HasFeature("desert"))
				BuildPalm(_root, at);
			else if (p.HasFeature("tundra") || MathF.Abs(a) > 1.2f && forest)
				BuildConifer(_root, at);
			else
				BuildBroadleaf(_root, at);
		}

		if (p.HasFeature("mountains") || p.HasFeature("hills"))
		{
			bool high = p.HasFeature("mountains");
			for (int i = 0; i < 9; i++)
			{
				float a = MathF.PI * 0.55f + i * 0.28f + Jitter(0.08f);
				float r = 85f + Jitter(10f);
				var at = new Vector3(MathF.Cos(a) * r, 0, MathF.Sin(a) * r);
				float h = high ? 26f + Jitter(10f) : 8f + Jitter(3f);
				float w = high ? 18f + Jitter(5f) : 16f + Jitter(4f);
				if (high)
				{
					AddMesh(new CylinderMesh { TopRadius = 0f, BottomRadius = w, Height = h, RadialSegments = 7 }, at + new Vector3(0, h * 0.5f, 0), new Color(0.45f, 0.42f, 0.38f));
					AddMesh(new CylinderMesh { TopRadius = 0f, BottomRadius = w * 0.3f, Height = h * 0.3f, RadialSegments = 7 }, at + new Vector3(0, h * 0.85f + 0.05f, 0), new Color(0.95f, 0.95f, 0.97f));
				}
				else
				{
					AddMesh(new SphereMesh { Radius = w, Height = h * 2f, IsHemisphere = true, RadialSegments = 16, Rings = 6 }, at, new Color(0.34f, 0.46f, 0.22f));
				}
			}
		}
	}

	// ---------------------------------------------------------------------------------- art styles

	void BuildHouse(Node3D house, string style)
	{
		switch (style)
		{
			case "asian":
			{
				Color wall = Pick(new Color(0.88f, 0.84f, 0.74f), new Color(0.62f, 0.42f, 0.28f));
				AddMesh(house, new BoxMesh { Size = new Vector3(2.3f, 1.3f, 2.3f) }, new Vector3(0, 0.65f, 0), wall);
				Color roof = Pick(new Color(0.22f, 0.25f, 0.28f), new Color(0.30f, 0.34f, 0.30f));
				FlaredRoof(house, 1.3f, 2.3f, roof);
				break;
			}
			case "african":
			{
				Color mud = Pick(new Color(0.66f, 0.43f, 0.26f), new Color(0.72f, 0.52f, 0.33f));
				AddMesh(house, new CylinderMesh { TopRadius = 1.05f, BottomRadius = 1.1f, Height = 1.3f, RadialSegments = 12 }, new Vector3(0, 0.65f, 0), mud);
				AddMesh(house, new CylinderMesh { TopRadius = 0f, BottomRadius = 1.45f, Height = 1.5f, RadialSegments = 12 }, new Vector3(0, 2.05f, 0), new Color(0.78f, 0.66f, 0.38f));
				AddMesh(house, new BoxMesh { Size = new Vector3(0.5f, 0.8f, 0.1f) }, new Vector3(0, 0.4f, 1.08f), new Color(0.2f, 0.13f, 0.08f));
				break;
			}
			case "north_american":
			{
				// bark longhouse with a barrel roof
				Color bark = Pick(new Color(0.45f, 0.36f, 0.26f), new Color(0.52f, 0.42f, 0.30f));
				AddMesh(house, new BoxMesh { Size = new Vector3(4.2f, 1.0f, 1.9f) }, new Vector3(0, 0.5f, 0), bark);
				var roof = AddMesh(house, new CylinderMesh { TopRadius = 0.95f, BottomRadius = 0.95f, Height = 4.2f, RadialSegments = 12 }, new Vector3(0, 1.0f, 0), bark.Darkened(0.1f));
				roof.Rotation = new Vector3(0, 0, MathF.PI / 2f);
				AddMesh(house, new BoxMesh { Size = new Vector3(0.1f, 0.8f, 0.5f) }, new Vector3(2.12f, 0.4f, 0), new Color(0.15f, 0.1f, 0.06f));
				break;
			}
			case "south_american":
			{
				// adobe blocks with flat roofs, some two storeys
				Color adobe = Pick(new Color(0.78f, 0.62f, 0.44f), new Color(0.70f, 0.50f, 0.36f));
				AddMesh(house, new BoxMesh { Size = new Vector3(2.4f, 1.4f, 2.4f) }, new Vector3(0, 0.7f, 0), adobe);
				AddMesh(house, new BoxMesh { Size = new Vector3(2.6f, 0.15f, 2.6f) }, new Vector3(0, 1.47f, 0), adobe.Darkened(0.15f));
				if (_rng.NextDouble() < 0.35)
					AddMesh(house, new BoxMesh { Size = new Vector3(1.4f, 1.0f, 1.4f) }, new Vector3(0.4f, 2.0f, -0.3f), adobe.Lightened(0.05f));
				AddMesh(house, new BoxMesh { Size = new Vector3(0.5f, 0.8f, 0.1f) }, new Vector3(0, 0.4f, 1.2f), new Color(0.25f, 0.16f, 0.1f));
				break;
			}
			default:
			{
				// european: stone and whitewash under red tile gables
				Color wall = Pick(new Color(0.86f, 0.82f, 0.72f), new Color(0.70f, 0.66f, 0.58f));
				AddMesh(house, new BoxMesh { Size = new Vector3(2.2f, 1.5f, 2.8f) }, new Vector3(0, 0.75f, 0), wall);
				var roof = AddMesh(house, new PrismMesh { Size = new Vector3(2.6f, 1.1f, 3.1f) }, new Vector3(0, 2.05f, 0), Pick(new Color(0.66f, 0.26f, 0.17f), new Color(0.58f, 0.22f, 0.15f)));
				roof.Rotation = Vector3.Zero;
				AddMesh(house, new BoxMesh { Size = new Vector3(0.5f, 0.9f, 0.1f) }, new Vector3(0, 0.45f, 1.42f), new Color(0.3f, 0.2f, 0.12f));
				break;
			}
		}
	}

	void BuildLandmark(string style, int settled)
	{
		float s = Math.Clamp(0.8f + settled / 80f, 0.8f, 1.6f);
		var node = new Node3D { Scale = new Vector3(s, s, s) };
		_root.AddChild(node);
		Reserve(Vector2.Zero, 7f);
		switch (style)
		{
			case "asian":
			{
				// pagoda: five tiers under flared roofs
				AddMesh(node, new BoxMesh { Size = new Vector3(5f, 0.6f, 5f) }, new Vector3(0, 0.3f, 0), new Color(0.6f, 0.58f, 0.55f));
				float y = 0.6f;
				for (int t = 0; t < 5; t++)
				{
					float w = 3.4f - t * 0.45f;
					AddMesh(node, new BoxMesh { Size = new Vector3(w, 1.3f, w) }, new Vector3(0, y + 0.65f, 0), new Color(0.62f, 0.14f, 0.10f));
					y += 1.3f;
					FlaredRoof(node, y, w + 0.6f, new Color(0.18f, 0.22f, 0.24f));
					y += 0.55f;
				}
				AddMesh(node, new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.12f, Height = 2f }, new Vector3(0, y + 1f, 0), new Color(0.75f, 0.6f, 0.2f));
				break;
			}
			case "african":
			{
				// great round hall and a baobab
				AddMesh(node, new CylinderMesh { TopRadius = 3f, BottomRadius = 3.1f, Height = 2.6f, RadialSegments = 20 }, new Vector3(0, 1.3f, 0), new Color(0.62f, 0.38f, 0.22f));
				AddMesh(node, new CylinderMesh { TopRadius = 0f, BottomRadius = 3.9f, Height = 4.2f, RadialSegments = 20 }, new Vector3(0, 4.7f, 0), new Color(0.74f, 0.62f, 0.34f));
				AddMesh(node, new CylinderMesh { TopRadius = 0.9f, BottomRadius = 1.2f, Height = 5f, RadialSegments = 10 }, new Vector3(5.2f, 2.5f, 2.5f), new Color(0.52f, 0.44f, 0.36f));
				AddMesh(node, new SphereMesh { Radius = 3f, Height = 2.6f }, new Vector3(5.2f, 5.8f, 2.5f), new Color(0.32f, 0.45f, 0.2f));
				break;
			}
			case "north_american":
			{
				// earth lodge and a totem pole
				AddMesh(node, new SphereMesh { Radius = 4f, Height = 4.4f, IsHemisphere = true, RadialSegments = 20, Rings = 6 }, Vector3.Zero, new Color(0.46f, 0.38f, 0.26f));
				AddMesh(node, new BoxMesh { Size = new Vector3(1.2f, 1.3f, 2.5f) }, new Vector3(0, 0.65f, 3.8f), new Color(0.40f, 0.32f, 0.22f));
				Color[] paint = { new(0.62f, 0.18f, 0.12f), new(0.12f, 0.35f, 0.42f), new(0.85f, 0.75f, 0.45f), new(0.15f, 0.12f, 0.1f) };
				for (int t = 0; t < 5; t++)
					AddMesh(node, new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.5f, Height = 1.3f, RadialSegments = 8 }, new Vector3(5.5f, 0.65f + t * 1.3f, 0), paint[t % paint.Length]);
				var wings = AddMesh(node, new BoxMesh { Size = new Vector3(3.2f, 0.3f, 0.5f) }, new Vector3(5.5f, 6.2f, 0), paint[0]);
				wings.Rotation = new Vector3(0, MathF.PI / 2f, 0);
				break;
			}
			case "south_american":
			{
				// stepped pyramid with a shrine on top
				float y = 0f;
				for (int t = 0; t < 5; t++)
				{
					float w = 9f - t * 1.6f;
					AddMesh(node, new BoxMesh { Size = new Vector3(w, 1.2f, w) }, new Vector3(0, y + 0.6f, 0), new Color(0.72f, 0.66f, 0.56f).Darkened(t * 0.03f));
					y += 1.2f;
				}
				AddMesh(node, new BoxMesh { Size = new Vector3(1.6f, y, 9f * 0.55f) }, new Vector3(0, y * 0.5f, 2.2f), new Color(0.62f, 0.56f, 0.48f));   // stairway
				AddMesh(node, new BoxMesh { Size = new Vector3(2.4f, 1.6f, 2.4f) }, new Vector3(0, y + 0.8f, 0), new Color(0.66f, 0.28f, 0.18f));
				AddMesh(node, new BoxMesh { Size = new Vector3(2.8f, 0.3f, 2.8f) }, new Vector3(0, y + 1.75f, 0), new Color(0.3f, 0.45f, 0.4f));
				break;
			}
			default:
			{
				// temple: podium, a ring of columns, a gabled roof
				Color marble = new(0.92f, 0.90f, 0.84f);
				AddMesh(node, new BoxMesh { Size = new Vector3(6.4f, 0.8f, 9f) }, new Vector3(0, 0.4f, 0), marble.Darkened(0.08f));
				for (int i = 0; i < 6; i++)
				{
					foreach (float x in new[] { -2.6f, 2.6f })
						AddMesh(node, new CylinderMesh { TopRadius = 0.28f, BottomRadius = 0.32f, Height = 3.2f, RadialSegments = 10 }, new Vector3(x, 2.4f, -3.6f + i * 1.44f), marble);
				}
				AddMesh(node, new BoxMesh { Size = new Vector3(4f, 3f, 6f) }, new Vector3(0, 2.3f, 0), marble.Darkened(0.04f));
				AddMesh(node, new BoxMesh { Size = new Vector3(6.2f, 0.5f, 8.6f) }, new Vector3(0, 4.25f, 0), marble);
				AddMesh(node, new PrismMesh { Size = new Vector3(6.4f, 1.4f, 8.8f) }, new Vector3(0, 5.2f, 0), new Color(0.66f, 0.30f, 0.20f));
				break;
			}
		}
	}

	void BuildTent(Node3D tent, string style)
	{
		switch (style)
		{
			case "asian":
				// yurt
				AddMesh(tent, new CylinderMesh { TopRadius = 1.1f, BottomRadius = 1.1f, Height = 1.0f, RadialSegments = 14 }, new Vector3(0, 0.5f, 0), new Color(0.90f, 0.88f, 0.80f));
				AddMesh(tent, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 1.2f, Height = 0.6f, RadialSegments = 14 }, new Vector3(0, 1.3f, 0), new Color(0.82f, 0.78f, 0.68f));
				AddMesh(tent, new BoxMesh { Size = new Vector3(0.45f, 0.7f, 0.1f) }, new Vector3(0, 0.35f, 1.08f), new Color(0.62f, 0.18f, 0.12f));
				break;
			case "north_american":
			{
				// tipi with poles through the top
				AddMesh(tent, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 1.1f, Height = 2.6f, RadialSegments = 10 }, new Vector3(0, 1.3f, 0), new Color(0.84f, 0.74f, 0.56f));
				AddMesh(tent, new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.02f, Height = 0.7f, RadialSegments = 6 }, new Vector3(0, 2.8f, 0), new Color(0.35f, 0.25f, 0.15f));
				AddMesh(tent, new BoxMesh { Size = new Vector3(0.9f, 0.12f, 0.02f) }, new Vector3(0, 1.1f, 0.62f), new Color(0.6f, 0.2f, 0.12f));
				break;
			}
			case "african":
				AddMesh(tent, new SphereMesh { Radius = 1.0f, Height = 1.5f, IsHemisphere = true, RadialSegments = 12, Rings = 4 }, Vector3.Zero, new Color(0.72f, 0.62f, 0.40f));
				break;
			default:
			{
				// ridge tent of hides
				Color hide = Pick(new Color(0.60f, 0.46f, 0.30f), new Color(0.52f, 0.40f, 0.28f));
				AddMesh(tent, new PrismMesh { Size = new Vector3(1.8f, 1.3f, 2.2f) }, new Vector3(0, 0.65f, 0), hide);
				break;
			}
		}
	}

	void BuildProductionModel(Node3D n, string model, int level)
	{
		Color wood = new(0.45f, 0.32f, 0.2f);
		Color darkWood = new(0.32f, 0.22f, 0.14f);
		Color stone = new(0.6f, 0.58f, 0.54f);
		switch (model)
		{
			case "woodcutter":
				Shed(n, new Vector3(-1.5f, 0, 0), wood, darkWood);
				for (int i = 0; i < 2 + level; i++)
				{
					var log = AddMesh(n, new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 3f, RadialSegments = 8 }, new Vector3(2f, 0.25f + (i / 3) * 0.45f, -0.6f + (i % 3) * 0.5f), new Color(0.55f, 0.4f, 0.25f));
					log.Rotation = new Vector3(0, 0, MathF.PI / 2f);
				}
				BuildConifer(n, new Vector3(2.5f, 0, 3f));
				BuildConifer(n, new Vector3(-3f, 0, 2.5f));
				break;
			case "trapper":
				Shed(n, new Vector3(-1f, 0, 0), darkWood, darkWood.Darkened(0.2f));
				AddMesh(n, new BoxMesh { Size = new Vector3(0.12f, 1.8f, 0.12f) }, new Vector3(1.5f, 0.9f, -1f), wood);
				AddMesh(n, new BoxMesh { Size = new Vector3(0.12f, 1.8f, 0.12f) }, new Vector3(1.5f, 0.9f, 1f), wood);
				AddMesh(n, new BoxMesh { Size = new Vector3(0.1f, 0.1f, 2.2f) }, new Vector3(1.5f, 1.75f, 0), wood);
				for (int i = 0; i < 3; i++)
					AddMesh(n, new BoxMesh { Size = new Vector3(0.05f, 0.8f, 0.45f) }, new Vector3(1.5f, 1.3f, -0.6f + i * 0.6f), new Color(0.42f, 0.32f, 0.24f));
				break;
			case "tannery":
				Shed(n, new Vector3(0, 0, -1f), new Color(0.6f, 0.5f, 0.38f), darkWood, 4f);
				for (int i = 0; i < 3; i++)
				{
					AddMesh(n, new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.55f, Height = 0.7f, RadialSegments = 12 }, new Vector3(-1.4f + i * 1.4f, 0.35f, 1.6f), wood);
					AddMesh(n, new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 0.05f, RadialSegments = 12 }, new Vector3(-1.4f + i * 1.4f, 0.7f, 1.6f), new Color(0.35f, 0.24f, 0.1f));
				}
				break;
			case "weaver":
				Shed(n, new Vector3(0, 0, -0.8f), new Color(0.82f, 0.76f, 0.62f), new Color(0.55f, 0.3f, 0.2f), 3.4f);
				AddMesh(n, new BoxMesh { Size = new Vector3(0.1f, 1.6f, 0.1f) }, new Vector3(-0.8f, 0.8f, 1.8f), wood);
				AddMesh(n, new BoxMesh { Size = new Vector3(0.1f, 1.6f, 0.1f) }, new Vector3(0.8f, 0.8f, 1.8f), wood);
				AddMesh(n, new BoxMesh { Size = new Vector3(1.5f, 1.2f, 0.04f) }, new Vector3(0, 0.9f, 1.8f), new Color(0.72f, 0.25f, 0.2f));
				break;
			case "kiln":
				AddMesh(n, new SphereMesh { Radius = 1.6f, Height = 2.4f, IsHemisphere = true, RadialSegments = 14, Rings = 5 }, Vector3.Zero, new Color(0.68f, 0.40f, 0.26f));
				AddMesh(n, new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.4f, Height = 1.6f }, new Vector3(0, 2.3f, 0), new Color(0.55f, 0.33f, 0.22f));
				AddMesh(n, new BoxMesh { Size = new Vector3(0.6f, 0.5f, 0.2f) }, new Vector3(0, 0.3f, 1.55f), new Color(1f, 0.5f, 0.15f), emissive: true);
				for (int i = 0; i < 4 + level; i++)
					AddMesh(n, new SphereMesh { Radius = 0.3f, Height = 0.7f, RadialSegments = 8, Rings = 4 }, new Vector3(-2f + i * 0.6f, 0.35f, 2.4f), new Color(0.72f, 0.42f, 0.25f));
				break;
			case "smithy":
				AddMesh(n, new BoxMesh { Size = new Vector3(3.4f, 2f, 3f) }, new Vector3(0, 1f, 0), stone);
				AddMesh(n, new PrismMesh { Size = new Vector3(3.8f, 1.2f, 3.4f) }, new Vector3(0, 2.6f, 0), new Color(0.3f, 0.28f, 0.27f));
				AddMesh(n, new BoxMesh { Size = new Vector3(0.7f, 3.8f, 0.7f) }, new Vector3(1.2f, 1.9f, -0.9f), stone.Darkened(0.15f));
				AddMesh(n, new BoxMesh { Size = new Vector3(1f, 0.7f, 0.2f) }, new Vector3(0, 0.6f, 1.51f), new Color(1f, 0.45f, 0.1f), emissive: true);
				AddMesh(n, new BoxMesh { Size = new Vector3(0.6f, 0.5f, 0.3f) }, new Vector3(-1f, 0.25f, 2.1f), new Color(0.2f, 0.2f, 0.22f));
				break;
			case "mine":
				AddMesh(n, new SphereMesh { Radius = 4f, Height = 5f, IsHemisphere = true, RadialSegments = 12, Rings = 5 }, new Vector3(0, 0, -1.5f), new Color(0.48f, 0.42f, 0.36f));
				AddMesh(n, new BoxMesh { Size = new Vector3(1.4f, 1.6f, 0.6f) }, new Vector3(0, 0.8f, 2.2f), new Color(0.08f, 0.07f, 0.06f));
				AddMesh(n, new BoxMesh { Size = new Vector3(0.2f, 1.9f, 0.2f) }, new Vector3(-0.8f, 0.95f, 2.5f), wood);
				AddMesh(n, new BoxMesh { Size = new Vector3(0.2f, 1.9f, 0.2f) }, new Vector3(0.8f, 0.95f, 2.5f), wood);
				AddMesh(n, new BoxMesh { Size = new Vector3(1.9f, 0.2f, 0.25f) }, new Vector3(0, 1.9f, 2.5f), wood);
				AddMesh(n, new BoxMesh { Size = new Vector3(0.8f, 0.5f, 1.1f) }, new Vector3(1.6f, 0.4f, 3.2f), darkWood);
				break;
			case "quarry":
				for (int i = 0; i < 3; i++)
					AddMesh(n, new BoxMesh { Size = new Vector3(5f - i * 1.2f, 1f, 3.4f - i * 0.6f) }, new Vector3(0, 0.5f + i, -1f - i * 0.4f), stone.Darkened(i * 0.05f));
				for (int i = 0; i < 2 + level; i++)
					AddMesh(n, new BoxMesh { Size = new Vector3(0.8f, 0.6f, 0.6f) }, new Vector3(-1.5f + i * 1f, 0.3f, 2.2f), stone.Lightened(0.1f));
				break;
			case "pit":
				AddMesh(n, new CylinderMesh { TopRadius = 1.8f, BottomRadius = 1.8f, Height = 0.06f, RadialSegments = 16 }, new Vector3(0, 0.03f, 0), new Color(0.05f, 0.04f, 0.04f));
				AddMesh(n, new BoxMesh { Size = new Vector3(0.15f, 2.4f, 0.15f) }, new Vector3(-1.2f, 1.2f, 0), wood);
				AddMesh(n, new BoxMesh { Size = new Vector3(0.15f, 2.4f, 0.15f) }, new Vector3(1.2f, 1.2f, 0), wood);
				AddMesh(n, new BoxMesh { Size = new Vector3(2.6f, 0.15f, 0.15f) }, new Vector3(0, 2.4f, 0), wood);
				for (int i = 0; i < 3; i++)
					AddMesh(n, new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.8f }, new Vector3(2.4f, 0.4f, -0.8f + i * 0.8f), darkWood);
				break;
			default:
				Shed(n, Vector3.Zero, wood, darkWood);
				break;
		}
	}

	// ------------------------------------------------------------------------------------- shapes

	void Shed(Node3D n, Vector3 at, Color wall, Color roof, float length = 2.8f)
	{
		AddMesh(n, new BoxMesh { Size = new Vector3(length, 1.6f, 2.2f) }, at + new Vector3(0, 0.8f, 0), wall);
		var r = AddMesh(n, new PrismMesh { Size = new Vector3(2.6f, 1f, length + 0.3f) }, at + new Vector3(0, 2.1f, 0), roof);
		r.Rotation = new Vector3(0, MathF.PI / 2f, 0);
	}

	/// <summary>A wide, low hipped roof with a steeper cap: the upturned look of an East Asian roof.</summary>
	void FlaredRoof(Node3D parent, float y, float width, Color color)
	{
		float half = width * 0.5f * 1.414f;
		var lower = AddMesh(parent, new CylinderMesh { TopRadius = half * 0.55f, BottomRadius = half * 1.25f, Height = 0.35f, RadialSegments = 4 }, new Vector3(0, y + 0.17f, 0), color);
		lower.Rotation = new Vector3(0, MathF.PI / 4f, 0);
		var upper = AddMesh(parent, new CylinderMesh { TopRadius = 0.05f, BottomRadius = half * 0.6f, Height = 0.6f, RadialSegments = 4 }, new Vector3(0, y + 0.64f, 0), color);
		upper.Rotation = new Vector3(0, MathF.PI / 4f, 0);
	}

	void BuildConifer(Node3D parent, Vector3 at)
	{
		float s = 0.8f + (float)_rng.NextDouble() * 0.6f;
		AddMesh(parent, new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.2f, Height = 1f * s, RadialSegments = 6 }, at + new Vector3(0, 0.5f * s, 0), new Color(0.35f, 0.24f, 0.15f));
		AddMesh(parent, new CylinderMesh { TopRadius = 0f, BottomRadius = 1.2f * s, Height = 3.2f * s, RadialSegments = 8 }, at + new Vector3(0, 2.4f * s, 0), new Color(0.15f, 0.30f, 0.17f));
	}

	void BuildBroadleaf(Node3D parent, Vector3 at)
	{
		float s = 0.8f + (float)_rng.NextDouble() * 0.6f;
		AddMesh(parent, new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.25f, Height = 1.6f * s, RadialSegments = 6 }, at + new Vector3(0, 0.8f * s, 0), new Color(0.38f, 0.27f, 0.17f));
		AddMesh(parent, new SphereMesh { Radius = 1.3f * s, Height = 2.2f * s, RadialSegments = 10, Rings = 5 }, at + new Vector3(0, 2.4f * s, 0), new Color(0.24f, 0.42f, 0.18f).Darkened((float)_rng.NextDouble() * 0.15f));
	}

	void BuildPalm(Node3D parent, Vector3 at)
	{
		AddMesh(parent, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.2f, Height = 3.2f, RadialSegments = 6 }, at + new Vector3(0, 1.6f, 0), new Color(0.5f, 0.38f, 0.24f));
		for (int i = 0; i < 5; i++)
		{
			float a = i * MathF.Tau / 5f;
			var leaf = AddMesh(parent, new BoxMesh { Size = new Vector3(0.35f, 0.06f, 1.8f) }, at + new Vector3(MathF.Sin(a) * 0.8f, 3.1f, MathF.Cos(a) * 0.8f), new Color(0.25f, 0.45f, 0.18f));
			leaf.Rotation = new Vector3(0.35f, a, 0);
		}
	}

	MeshInstance3D AddMesh(Mesh mesh, Vector3 position, Color color, bool emissive = false) =>
		AddMesh(_root, mesh, position, color, emissive);

	MeshInstance3D AddMesh(Node3D parent, Mesh mesh, Vector3 position, Color color, bool emissive = false)
	{
		var mi = new MeshInstance3D { Mesh = mesh, Position = position, MaterialOverride = Material(color, emissive) };
		parent.AddChild(mi);
		return mi;
	}

	StandardMaterial3D Material(Color color, bool emissive)
	{
		Color key = emissive ? new Color(color.R, color.G, color.B, 0.5f) : color;
		if (_materials.TryGetValue(key, out StandardMaterial3D m))
			return m;
		m = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.95f };
		if (emissive)
		{
			m.EmissionEnabled = true;
			m.Emission = color;
			m.EmissionEnergyMultiplier = 1.5f;
		}
		_materials[key] = m;
		return m;
	}

	void AddLabel(string text, Vector3 position, int fontSize, Color color)
	{
		var label = new Label3D
		{
			Text = text,
			Position = position,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			FontSize = fontSize,
			OutlineSize = 10,
			Modulate = color,
			OutlineModulate = new Color(0.05f, 0.03f, 0.02f, 0.85f),
			PixelSize = 0.0005f,
			NoDepthTest = true,
			FixedSize = true,      // same size on screen at any zoom
		};
		if (_font != null)
			label.Font = _font;
		_root.AddChild(label);
	}

	/// <summary>The city's name and population above the landmark.</summary>
	public static void AddTitle(Node3D city, Province p, Font font)
	{
		var b = new CityBuilder(city, 0, font);
		int people = p.TotalUnits * PopGroup.PeoplePerUnit;
		float height = p.Pops.TrueForAll(g => g.Occupation.Nomadic) ? 10f : 22f;
		b.AddLabel($"{p.Name}\n{people:N0} people", new Vector3(0, height, 0), 56, new Color(1f, 0.95f, 0.8f));
	}

	float Jitter(float amount) => ((float)_rng.NextDouble() * 2f - 1f) * amount;

	Color Pick(Color a, Color b) => a.Lerp(b, (float)_rng.NextDouble());

	bool IsFree(Vector2 pos, float radius)
	{
		if (pos.Y - radius < _seaZ)
			return false;
		foreach (var (p, r) in _occupied)
		{
			if (p.DistanceTo(pos) < r + radius)
				return false;
		}
		return true;
	}

	void Reserve(Vector2 pos, float radius) => _occupied.Add((pos, radius));
}
