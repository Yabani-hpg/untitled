using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Map;

/// <summary>
/// Armies on the map: a banner in their country's colour with the number of regiments, drawn between
/// provinces as they march. Left click selects an army; with one of ours selected, right click on a
/// province marches it there, and its road is drawn ahead of it.
/// </summary>
public partial class ArmyMarkers : Node3D
{
	[Export] public Font LabelFont { get; set; }
	[Export] public NodePath FlatWorldPath { get; set; }
	[Export] public NodePath PickerPath { get; set; }
	/// <summary>Screen distance within which a click picks an army.</summary>
	[Export] public float PickRadiusPx { get; set; } = 26f;

	const string HeightmapPath = "res://map/heightmapps.png";
	const float SeaLevel = 0.5019608f, HeightScale = 35f;

	Image _heights;
	Node3D _flatWorld;
	ProvincePicker _picker;
	readonly Dictionary<int, Node3D> _markers = new();
	Mesh _pole, _banner, _border, _base, _ring;
	StandardMaterial3D _wood, _gold, _frame;
	MultiMeshInstance3D _road;
	CylinderMesh _dot;
	Vector2 _pressPosition;
	float _scale = 1f;

	public override void _Ready()
	{
		GameState gs = GameState.Instance;
		if (gs?.ProvinceMap == null)
			return;
		_flatWorld = GetNodeOrNull<Node3D>(FlatWorldPath ?? new NodePath());
		_picker = GetNodeOrNull<ProvincePicker>(PickerPath ?? new NodePath());
		_heights = GD.Load<Texture2D>(HeightmapPath)?.GetImage();
		if (_heights != null && _heights.IsCompressed())
			_heights.Decompress();
		_pole = new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 5f, RadialSegments = 6 };
		// the banner always faces the camera: a dark frame with the country's colour inside
		_banner = new QuadMesh { Size = new Vector2(2.6f, 1.7f) };
		_border = new QuadMesh { Size = new Vector2(3.1f, 2.2f) };
		_frame = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.1f, 0.07f, 0.04f),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
			BillboardKeepScale = true,
			NoDepthTest = true,
			RenderPriority = 1,
		};
		_base = new CylinderMesh { TopRadius = 1.3f, BottomRadius = 1.5f, Height = 0.4f, RadialSegments = 12 };
		_ring = new TorusMesh { InnerRadius = 1.8f, OuterRadius = 2.3f, Rings = 24, RingSegments = 6 };
		_wood = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.24f, 0.14f), Roughness = 1 };
		_gold = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.82f, 0.35f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		// the road ahead of the selected army: a trail of dots
		_dot = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 0.1f, RadialSegments = 8 };
		_road = new MultiMeshInstance3D
		{
			Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = _dot },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(1f, 0.85f, 0.4f),
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				NoDepthTest = true,
			},
		};
		AddChild(_road);
		gs.ArmiesChanged += Refresh;
		gs.ArmiesMoved += Refresh;
		gs.SelectedArmyChanged += OnSelected;
		gs.PhaseChanged += Refresh;
		Refresh();
	}

	public override void _ExitTree()
	{
		GameState gs = GameState.Instance;
		if (gs == null)
			return;
		gs.ArmiesChanged -= Refresh;
		gs.ArmiesMoved -= Refresh;
		gs.SelectedArmyChanged -= OnSelected;
		gs.PhaseChanged -= Refresh;
	}

	void OnSelected(int id) => Refresh();

	public override void _Process(double delta)
	{
		// the globe view hides the flat map, and the armies with it
		Visible = _flatWorld == null || _flatWorld.IsVisibleInTree();
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null || !Visible)
			return;
		// banners grow with the camera's distance, so armies can be seen from afar
		foreach (Node3D marker in _markers.Values)
		{
			float scale = Mathf.Clamp(camera.GlobalPosition.DistanceTo(marker.GlobalPosition) / 90f, 1.5f, 40f);
			marker.GetNode<Node3D>("Body").Scale = Vector3.One * scale;
			marker.GetNode<Label3D>("Count").Position = new Vector3(0, 5.2f * scale, 0);
			marker.GetNode<Label3D>("Name").Position = new Vector3(0, 5.2f * scale, 0);
		}
		float dots = Mathf.Clamp(camera.GlobalPosition.Y / 90f, 1.5f, 40f);
		if (!Mathf.IsEqualApprox(dots, _scale))
		{
			_scale = dots;
			_dot.TopRadius = _dot.BottomRadius = 0.45f * dots;
			DrawRoad(GameState.Instance);
		}
	}

	void Refresh()
	{
		GameState gs = GameState.Instance;
		var alive = new HashSet<int>();
		var perProvince = new Dictionary<int, int>();
		foreach (Army a in gs.Armies.Values)
		{
			if (ArmyPosition(gs, a) is not Vector3 at)
				continue;
			alive.Add(a.Id);
			if (!_markers.TryGetValue(a.Id, out Node3D marker))
				_markers[a.Id] = marker = MakeMarker(a);
			// armies standing together fan out a little
			int n = a.Moving ? 0 : perProvince.GetValueOrDefault(a.ProvinceId);
			if (!a.Moving)
				perProvince[a.ProvinceId] = n + 1;
			marker.Position = at + new Vector3(n * 3.5f, 0, n * 1.5f);
			Country owner = gs.GetCountry(a.OwnerTag);
			var banner = marker.GetNode<MeshInstance3D>("Body/Banner");
			((StandardMaterial3D)banner.MaterialOverride).AlbedoColor = owner?.MapColor ?? Colors.Gray;
			marker.GetNode<Node3D>("Body/Ring").Visible = a.Id == gs.SelectedArmyId;
			var label = marker.GetNode<Label3D>("Count");
			label.Text = $"{a.Regiments.Count}";
			label.Modulate = a.OwnerTag == gs.PlayerTag ? new Color(1f, 0.95f, 0.8f) : new Color(1f, 0.7f, 0.6f);
			var name = marker.GetNode<Label3D>("Name");
			name.Text = a.Name;
			name.Visible = a.Id == gs.SelectedArmyId;
		}
		foreach (int id in _markers.Keys.ToList())
		{
			if (alive.Contains(id))
				continue;
			_markers[id].QueueFree();
			_markers.Remove(id);
		}
		DrawRoad(gs);
	}

	/// <summary>Where an army is drawn: its province, or on the way to the next one as it marches.</summary>
	Vector3? ArmyPosition(GameState gs, Army a)
	{
		if (gs.ProvinceMap.GetCentroid(a.ProvinceId) is not Vector2 from)
			return null;
		Vector2 px = from;
		if (a.Moving && a.StepDays > 0 && gs.ProvinceMap.GetCentroid(a.Path[0]) is Vector2 to)
			px = from.Lerp(Unwrapped(gs, from, to), (float)a.DaysMarched / a.StepDays);
		return ToWorld(gs, px);
	}

	/// <summary>The copy of <paramref name="to"/> nearest <paramref name="from"/>, across the east-west wrap.</summary>
	static Vector2 Unwrapped(GameState gs, Vector2 from, Vector2 to)
	{
		float w = gs.ProvinceMap.Width;
		if (to.X - from.X > w / 2) to.X -= w;
		else if (from.X - to.X > w / 2) to.X += w;
		return to;
	}

	Vector3 ToWorld(GameState gs, Vector2 px) =>
		new(px.X - gs.ProvinceMap.Width / 2f, HeightAt(px), px.Y - gs.ProvinceMap.Height / 2f);

	/// <summary>The selected army's road ahead, as a trail of dots over the map.</summary>
	void DrawRoad(GameState gs)
	{
		var points = new List<Vector3>();
		Army a = gs.GetArmy(gs.SelectedArmyId);
		if (a != null && a.Moving && ArmyPosition(gs, a) is Vector3 start)
		{
			Vector2 last = new(start.X + gs.ProvinceMap.Width / 2f, start.Z + gs.ProvinceMap.Height / 2f);
			float spacing = 2.2f * _scale;
			foreach (int id in a.Path)
			{
				if (gs.ProvinceMap.GetCentroid(id) is not Vector2 px)
					continue;
				px = Unwrapped(gs, last, px);
				int n = Mathf.Max(1, Mathf.CeilToInt(last.DistanceTo(px) / spacing));
				for (int i = 1; i <= n; i++)
				{
					Vector2 at = last.Lerp(px, (float)i / n);
					points.Add(ToWorld(gs, at) + Vector3.Up * 0.5f);
				}
				last = px;
			}
		}
		MultiMesh mm = _road.Multimesh;
		mm.InstanceCount = points.Count;
		for (int i = 0; i < points.Count; i++)
			mm.SetInstanceTransform(i, new Transform3D(Basis.Identity, points[i]));
	}

	Node3D MakeMarker(Army a)
	{
		var marker = new Node3D { Name = $"Army{a.Id}" };
		AddChild(marker);
		var body = new Node3D { Name = "Body" };
		marker.AddChild(body);
		MeshInstance3D Mesh(string name, Mesh mesh, Material material, Vector3 at)
		{
			var m = new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = material, Position = at, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			body.AddChild(m);
			return m;
		}
		Mesh("Base", _base, _wood, new Vector3(0, 0.2f, 0));
		Mesh("Pole", _pole, _wood, new Vector3(0, 2.5f, 0));
		Mesh("Border", _border, _frame, new Vector3(0, 5.2f, 0));
		Mesh("Banner", _banner, new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
			BillboardKeepScale = true,
			NoDepthTest = true,
			RenderPriority = 2,
		}, new Vector3(0, 5.2f, 0));
		Mesh("Ring", _ring, _gold, new Vector3(0, 0.3f, 0)).Visible = false;
		var label = new Label3D
		{
			Name = "Count",
			Position = new Vector3(0, 5.2f, 0),
			RenderPriority = 3,
			OutlineRenderPriority = 2,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			FixedSize = true,
			PixelSize = 0.0007f,
			FontSize = 28,
			OutlineSize = 9,
			OutlineModulate = new Color(0.05f, 0.03f, 0.02f, 0.9f),
			NoDepthTest = true,
		};
		if (LabelFont != null)
			label.Font = LabelFont;
		marker.AddChild(label);
		var name = (Label3D)label.Duplicate();
		name.Name = "Name";
		name.FontSize = 24;
		name.Modulate = new Color(1f, 0.85f, 0.45f);
		name.Offset = new Vector2(0, 46);         // above the banner, on screen
		marker.AddChild(name);
		return marker;
	}

	float HeightAt(Vector2 px)
	{
		if (_heights == null)
			return 1f;
		int w = _heights.GetWidth();
		int x = ((int)px.X % w + w) % w;
		int y = Mathf.Clamp((int)px.Y, 0, _heights.GetHeight() - 1);
		return Mathf.Max((_heights.GetPixel(x, y).R - SeaLevel) * HeightScale, 0f) + 0.3f;
	}

	// ---------------------------------------------------------------------------------------- input

	/// <summary>The army whose banner is under a screen position, or null.</summary>
	Army ArmyAtScreen(Vector2 screen)
	{
		GameState gs = GameState.Instance;
		Camera3D camera = GetViewport().GetCamera3D();
		if (camera == null || !Visible)
			return null;
		Army best = null;
		float bestDistance = PickRadiusPx;
		foreach (var (id, marker) in _markers)
		{
			Vector3 at = marker.GlobalPosition + Vector3.Up * 3f * marker.GetNode<Node3D>("Body").Scale.Y;
			if (camera.IsPositionBehind(at))
				continue;
			float d = camera.UnprojectPosition(at).DistanceTo(screen);
			if (d < bestDistance)
			{
				best = gs.GetArmy(id);
				bestDistance = d;
			}
		}
		return best;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		GameState gs = GameState.Instance;
		if (gs == null || gs.Phase != GamePhase.Playing || @event is not InputEventMouseButton mb)
		{
			if (@event.IsActionPressed("ui_cancel"))
				gs?.SelectArmy(0);
			return;
		}
		if (mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Pressed)
			{
				_pressPosition = mb.Position;
				return;
			}
			if (mb.Position.DistanceTo(_pressPosition) > 6f)
				return;
			Army clicked = ArmyAtScreen(mb.Position);
			if (clicked != null)
			{
				gs.SelectArmy(clicked.Id);
				GetViewport().SetInputAsHandled();
			}
			else
				gs.SelectArmy(0);     // the click goes on to select the province
		}
		else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
		{
			Army a = gs.GetArmy(gs.SelectedArmyId);
			if (a == null || a.OwnerTag != gs.PlayerTag || _picker == null)
				return;
			int target = _picker.ProvinceAtScreen(mb.Position);
			if (gs.GetProvince(target) != null)
			{
				if (!gs.MoveArmy(a.Id, target, out string why))
					gs.Notify(why);
			}
			GetViewport().SetInputAsHandled();
		}
	}
}
