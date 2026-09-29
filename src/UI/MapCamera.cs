using Godot;
using Untitled.Core;

namespace Untitled.UI;

/// <summary>Moves the map camera (scenes/camera_rig.gd) to a province.</summary>
public static class MapCamera
{
	/// <summary>Camera distance for a regional view of a country.</summary>
	public const float RegionZoom = 750f;

	public static void FocusProvince(Node3D rig, int provinceId, float zoom = RegionZoom)
	{
		GameState gs = GameState.Instance;
		if (gs?.ProvinceMap == null || rig == null || !GodotObject.IsInstanceValid(rig) || !rig.HasMethod("focus"))
			return;
		if (gs.ProvinceMap.GetCentroid(provinceId) is Vector2 px)
			rig.Call("focus", new Vector3(px.X - gs.ProvinceMap.Width / 2f, 0f, px.Y - gs.ProvinceMap.Height / 2f), zoom, false);
	}
}
