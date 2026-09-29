using Godot;
using Untitled.Core;

namespace Untitled.UI;

/// <summary>Short messages at the top of the screen (alliances, battles, uprisings), fading after a while.</summary>
public partial class MessageFeed : VBoxContainer
{
	[Export] public Font BodyFont { get; set; }
	[Export] public double SecondsShown { get; set; } = 12;
	const int MaxMessages = 5;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		AddThemeConstantOverride("separation", 6);
		if (GameState.Instance != null)
			GameState.Instance.MessagePosted += Post;
	}

	public override void _ExitTree()
	{
		if (GameState.Instance != null)
			GameState.Instance.MessagePosted -= Post;
	}

	public void Post(string text)
	{
		var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
		panel.AddThemeStyleboxOverride("panel", HudStyle.Panel(0.9f));
		var label = HudStyle.Body(text, BodyFont, 15);
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(460, 0);
		panel.AddChild(label);
		AddChild(panel);
		while (GetChildCount() > MaxMessages)
		{
			Node oldest = GetChild(0);
			RemoveChild(oldest);
			oldest.QueueFree();
		}
		Tween fade = panel.CreateTween();
		fade.TweenInterval(SecondsShown);
		fade.TweenProperty(panel, "modulate:a", 0.0, 1.0);
		fade.TweenCallback(Callable.From(panel.QueueFree));
	}
}
