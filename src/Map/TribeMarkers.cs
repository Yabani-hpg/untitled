using System.Collections.Generic;
using Godot;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.Map;

/// <summary>
/// Unsettled countries on the map: each tribe's camp as a small tent (nomads) or hut (settled tribes)
/// with its name, seen when zoomed in. Nomad camps move as the tribes roam.
/// </summary>
public partial class TribeMarkers : Node3D
{
	[Export] public Font LabelFont { get; set; }
	/// <summary>Camera distance beyond which camps are hidden.</summary>
	[Export] public float VisibleWithin { get; set; } = 700f;

	const string HeightmapPath = "res://map/heightmapps.png";
	const float SeaLevel = 0.5019608f, HeightScale = 35f;

	Image _heights;
	readonly Dictionary<int, Node3D> _markers = new();
	Mesh _tent, _hut, _roof;
	StandardMaterial3D _hide, _mud, _thatch;

	public override void _Ready()
	{
		GameState gs = GameState.Instance;
		if (gs?.ProvinceMap == null)
			return;
		_heights = GD.Load<Texture2D>(HeightmapPath)?.GetImage();
		if (_heights != null && _heights.IsCompressed())
			_heights.Decompress();
		_tent = new CylinderMesh { TopRadius = 0f, BottomRadius = 2.2f, Height = 3.6f, RadialSegments = 8 };
		_hut = new CylinderMesh { TopRadius = 1.8f, BottomRadius = 1.9f, Height = 1.6f, RadialSegments = 10 };
		_roof = new CylinderMesh { TopRadius = 0f, BottomRadius = 2.4f, Height = 1.8f, RadialSegments = 10 };
		_hide = new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.76f, 0.56f), Roughness = 1 };
		_mud = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.42f, 0.26f), Roughness = 1 };
		_thatch = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.66f, 0.38f), Roughness = 1 };
		gs.TribesChanged += Refresh;
		Refresh();
	}

	public override void _ExitTree()
	{
		if (GameState.Instance != null)
			GameState.Instance.TribesChanged -= Refresh;
	}

	void Refresh()
	{
		GameState gs = GameState.Instance;
		var alive = new HashSet<int>();
		foreach (Tribe t in gs.Tribes.Values)
		{
			if (t.Provinces.Count == 0 || gs.ProvinceMap.GetCentroid(t.CampProvinceId) is not Vector2 px)
				continue;
			alive.Add(t.Id);
			if (!_markers.TryGetValue(t.Id, out Node3D marker))
				_markers[t.Id] = marker = MakeMarker(t);
			marker.Position = new Vector3(px.X - gs.ProvinceMap.Width / 2f, HeightAt(px), px.Y - gs.ProvinceMap.Height / 2f);
			var label = marker.GetNode<Label3D>("Name");
			Country ally = gs.GetCountry(t.AlliedTag);
			label.Modulate = ally != null ? ally.MapColor.Lightened(0.35f) : new Color(0.95f, 0.88f, 0.72f);
			label.Text = ally != null ? $"{t.Name} ({ally.Adjective} allies)" : t.Name;
		}
		foreach (int id in new List<int>(_markers.Keys))
		{
			if (alive.Contains(id))
				continue;
			_markers[id].QueueFree();
			_markers.Remove(id);
		}
	}

	Node3D MakeMarker(Tribe t)
	{
		var marker = new Node3D { Name = $"Tribe{t.Id}" };
		AddChild(marker);
		void Mesh(Mesh mesh, Material material, Vector3 at)
		{
			marker.AddChild(new MeshInstance3D
			{
				Mesh = mesh, MaterialOverride = material, Position = at,
				VisibilityRangeEnd = VisibleWithin, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		}
		if (t.IsNomadic)
		{
			Mesh(_tent, _hide, new Vector3(0, 1.8f, 0));
			Mesh(_tent, _hide, new Vector3(3.2f, 1.4f, 1.5f));
		}
		else
		{
			Mesh(_hut, _mud, new Vector3(0, 0.8f, 0));
			Mesh(_roof, _thatch, new Vector3(0, 2.5f, 0));
		}
		var label = new Label3D
		{
			Name = "Name",
			Position = new Vector3(0, 6f, 0),
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			FixedSize = true,
			PixelSize = 0.0006f,
			FontSize = 26,
			OutlineSize = 8,
			OutlineModulate = new Color(0.05f, 0.03f, 0.02f, 0.85f),
			NoDepthTest = true,
			VisibilityRangeEnd = VisibleWithin,
		};
		if (LabelFont != null)
			label.Font = LabelFont;
		marker.AddChild(label);
		return marker;
	}

	/// <summary>Terrain height at a map pixel, as the terrain shader raises it.</summary>
	float HeightAt(Vector2 px)
	{
		if (_heights == null)
			return 1f;
		int x = Mathf.Clamp((int)px.X, 0, _heights.GetWidth() - 1);
		int y = Mathf.Clamp((int)px.Y, 0, _heights.GetHeight() - 1);
		return Mathf.Max((_heights.GetPixel(x, y).R - SeaLevel) * HeightScale, 0f) + 0.3f;
	}
}
