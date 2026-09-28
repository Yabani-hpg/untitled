using Godot;
using Untitled.Core;

namespace Untitled.Map;

/// <summary>Feeds the selected province to the terrain shader, which tints and outlines it.</summary>
public partial class ProvinceHighlight : Node
{
	/// <summary>MeshInstance3D whose mesh material uses shaders/main.gdshader. Wrap-tile clones share the material.</summary>
	[Export] public NodePath FlatWorldPath { get; set; }

	static readonly StringName ProvinceMapParam = "province_map";
	static readonly StringName SelectedColorParam = "selected_province_color";
	static readonly Vector3 NoSelection = new(-1f, -1f, -1f);

	ShaderMaterial _material;

	public override void _Ready()
	{
		var flatWorld = GetNodeOrNull<MeshInstance3D>(FlatWorldPath);
		_material = (flatWorld?.MaterialOverride ?? flatWorld?.Mesh?.SurfaceGetMaterial(0)) as ShaderMaterial;
		GameState state = GameState.Instance;
		if (_material == null || state == null)
		{
			GD.PushError($"{nameof(ProvinceHighlight)}: needs FlatWorldPath with a ShaderMaterial and the GameState autoload");
			return;
		}

		_material.SetShaderParameter(ProvinceMapParam, state.ProvinceMapTexture);
		state.SelectedProvinceChanged += OnSelectedProvinceChanged;
		OnSelectedProvinceChanged(state.SelectedProvinceId);
	}

	public override void _ExitTree()
	{
		if (GameState.Instance != null && _material != null)
			GameState.Instance.SelectedProvinceChanged -= OnSelectedProvinceChanged;
	}

	void OnSelectedProvinceChanged(int provinceId)
	{
		var province = GameState.Instance.GetProvince(provinceId);
		Vector3 color = province == null
			? NoSelection
			: new Vector3(province.MapColor.R, province.MapColor.G, province.MapColor.B);
		_material.SetShaderParameter(SelectedColorParam, color);
	}
}
