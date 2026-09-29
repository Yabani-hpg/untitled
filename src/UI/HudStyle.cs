using Godot;

namespace Untitled.UI;

/// <summary>Shared look of the in-game panels: dark leather with a thin gold rim, parchment text.</summary>
public static class HudStyle
{
	public static readonly Color Text = new(0.93f, 0.88f, 0.76f);
	public static readonly Color Muted = new(0.70f, 0.64f, 0.53f);
	public static readonly Color Gold = new(0.85f, 0.70f, 0.38f);
	public static readonly Color Good = new(0.60f, 0.82f, 0.48f);
	public static readonly Color Bad = new(0.90f, 0.50f, 0.40f);

	public static StyleBoxFlat Panel(float alpha = 0.93f)
	{
		var s = new StyleBoxFlat
		{
			BgColor = new Color(0.10f, 0.08f, 0.06f, alpha),
			BorderColor = new Color(0.62f, 0.49f, 0.26f, 0.9f),
			ShadowColor = new Color(0, 0, 0, 0.45f),
			ShadowSize = 6,
		};
		s.SetBorderWidthAll(1);
		s.SetCornerRadiusAll(6);
		s.SetContentMarginAll(12);
		return s;
	}

	public static StyleBoxFlat Section()
	{
		var s = new StyleBoxFlat { BgColor = new Color(1f, 0.9f, 0.7f, 0.05f), BorderColor = new Color(0.62f, 0.49f, 0.26f, 0.35f) };
		s.SetBorderWidthAll(1);
		s.SetCornerRadiusAll(4);
		s.SetContentMarginAll(8);
		return s;
	}

	public static Label Title(string text, Font font, int size)
	{
		var l = new Label { Text = text };
		if (font != null)
			l.AddThemeFontOverride("font", font);
		l.AddThemeFontSizeOverride("font_size", size);
		l.AddThemeColorOverride("font_color", Gold);
		return l;
	}

	public static Label Body(string text, Font font, int size = 15, Color? color = null)
	{
		var l = new Label { Text = text };
		if (font != null)
			l.AddThemeFontOverride("font", font);
		l.AddThemeFontSizeOverride("font_size", size);
		l.AddThemeColorOverride("font_color", color ?? Text);
		return l;
	}

	public static Button Button(string text, Font font, int size = 14)
	{
		var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
		if (font != null)
			b.AddThemeFontOverride("font", font);
		b.AddThemeFontSizeOverride("font_size", size);
		b.AddThemeColorOverride("font_color", Text);
		b.AddThemeColorOverride("font_hover_color", new Color(1f, 0.95f, 0.8f));
		b.AddThemeColorOverride("font_disabled_color", new Color(0.5f, 0.46f, 0.4f));
		foreach (var (state, bg) in new[] { ("normal", 0.22f), ("hover", 0.32f), ("pressed", 0.16f), ("disabled", 0.12f) })
		{
			var s = new StyleBoxFlat { BgColor = new Color(0.45f, 0.33f, 0.18f, bg + 0.35f), BorderColor = new Color(0.75f, 0.6f, 0.32f, state == "disabled" ? 0.3f : 0.8f) };
			s.SetBorderWidthAll(1);
			s.SetCornerRadiusAll(4);
			s.ContentMarginLeft = s.ContentMarginRight = 10;
			s.ContentMarginTop = s.ContentMarginBottom = 4;
			b.AddThemeStyleboxOverride(state, s);
		}
		return b;
	}

	/// <summary>A small square of colour, e.g. a culture's colour next to its name.</summary>
	public static ColorRect Swatch(Color color, float size = 12f) =>
		new() { Color = color, CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
}
