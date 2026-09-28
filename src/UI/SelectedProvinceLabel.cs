using Godot;
using Untitled.Core;

namespace Untitled.UI;

/// <summary>Placeholder readout of the selected province until the real province panel exists.</summary>
public partial class SelectedProvinceLabel : Label
{
	public override void _Ready()
	{
		if (GameState.Instance == null)
			return;
		GameState.Instance.SelectedProvinceChanged += OnSelectedProvinceChanged;
		OnSelectedProvinceChanged(GameState.Instance.SelectedProvinceId);
	}

	public override void _ExitTree()
	{
		if (GameState.Instance != null)
			GameState.Instance.SelectedProvinceChanged -= OnSelectedProvinceChanged;
	}

	void OnSelectedProvinceChanged(int provinceId)
	{
		var province = GameState.Instance.GetProvince(provinceId);
		Visible = province != null;
		if (province == null)
			return;

		string owner = GameState.Instance.GetCountry(province.OwnerTag)?.Name ?? "Unowned";
		Text = $"{province.Name}  (#{province.Id})\nTerrain: {province.Terrain}\nOwner: {owner}";
	}
}
