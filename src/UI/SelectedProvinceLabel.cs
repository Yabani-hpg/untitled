using System.Collections.Generic;
using System.Text;
using Godot;
using Untitled.Core;
using Untitled.Data;

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
		var text = new StringBuilder($"{province.Name}  (#{province.Id})\nTerrain: {province.Terrain}\nOwner: {owner}");

		var crossings = new SortedSet<string>();
		var navigable = new SortedSet<string>();
		foreach (Adjacency link in province.Neighbors)
		{
			if (link.IsRiverCrossing)
				crossings.Add(link.CrossingRiver);
			if (link.IsNavigableRiver)
				navigable.Add(link.NavigableRiver);
		}
		if (crossings.Count > 0)
			text.Append($"\nRiver borders: {string.Join(", ", crossings)}");
		if (navigable.Count > 0)
			text.Append($"\nNavigable: {string.Join(", ", navigable)}");
		Text = text.ToString();
	}
}
