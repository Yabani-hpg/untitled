using Godot;

namespace Untitled.Data;

public sealed class Country
{
	public string Tag { get; }
	public string Name { get; }
	public Color MapColor { get; }

	public Country(string tag, string name, Color mapColor)
	{
		Tag = tag;
		Name = name;
		MapColor = mapColor;
	}
}
