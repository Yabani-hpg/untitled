using Godot;
using Untitled.Core;

namespace Untitled.Map;

/// <summary>
/// Turns left clicks on the flat map into province selection. Right click or ui_cancel clears it.
/// Middle-drag belongs to the camera, and clicks that turn into a drag are ignored.
/// </summary>
public partial class ProvincePicker : Node
{
	/// <summary>The MeshInstance3D with the PlaneMesh that shows map/provinces.png 1:1.</summary>
	[Export] public NodePath FlatWorldPath { get; set; }
	[Export] public float ClickDragTolerancePx { get; set; } = 6f;

	MeshInstance3D _flatWorld;
	Vector2 _mapSize;
	Vector2 _pressPosition;
	bool _leftPressed;

	public override void _Ready()
	{
		_flatWorld = GetNodeOrNull<MeshInstance3D>(FlatWorldPath);
		if (_flatWorld?.Mesh is not PlaneMesh plane)
		{
			GD.PushError($"{nameof(ProvincePicker)}: FlatWorldPath must point to a MeshInstance3D with a PlaneMesh");
			SetProcessUnhandledInput(false);
			return;
		}
		_mapSize = plane.Size;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Left)
			{
				if (mb.Pressed)
				{
					_leftPressed = true;
					_pressPosition = mb.Position;
				}
				else if (_leftPressed)
				{
					_leftPressed = false;
					if (mb.Position.DistanceTo(_pressPosition) <= ClickDragTolerancePx)
						GameState.Instance?.SelectProvince(ProvinceAtScreen(mb.Position));
				}
			}
			else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
			{
				GameState.Instance?.SelectProvince(ProvinceMap.NoProvince);
			}
		}
		else if (@event.IsActionPressed("ui_cancel"))
		{
			GameState.Instance?.SelectProvince(ProvinceMap.NoProvince);
		}
	}

	/// <summary>Province under a screen position, or <see cref="ProvinceMap.NoProvince"/>.</summary>
	public int ProvinceAtScreen(Vector2 screenPos)
	{
		ProvinceMap map = GameState.Instance?.ProvinceMap;
		Camera3D camera = GetViewport().GetCamera3D();
		// The globe view hides the flat map; picking on the globe is not supported yet.
		if (map == null || camera == null || !_flatWorld.IsVisibleInTree())
			return ProvinceMap.NoProvince;

		// Intersect with the map's base plane. Terrain height is only a vertex displacement in the
		// shader, so clicks on steep mountains can land a few pixels off; fine for province-sized targets.
		Transform3D toLocal = _flatWorld.GlobalTransform.AffineInverse();
		Vector3 origin = toLocal * camera.ProjectRayOrigin(screenPos);
		Vector3 dir = toLocal.Basis * camera.ProjectRayNormal(screenPos);
		if (Mathf.Abs(dir.Y) < 1e-6f)
			return ProvinceMap.NoProvince;
		float t = -origin.Y / dir.Y;
		if (t < 0f)
			return ProvinceMap.NoProvince;

		Vector3 hit = origin + dir * t;
		// PlaneMesh UV (0,0) is the -X,-Z corner; x beyond the mesh lands on a wrap tile and wraps in the lookup.
		var uv = new Vector2(hit.X / _mapSize.X + 0.5f, hit.Z / _mapSize.Y + 0.5f);
		return map.GetIdAtUv(uv);
	}
}
