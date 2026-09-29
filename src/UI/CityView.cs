using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Untitled.Core;
using Untitled.Data;

namespace Untitled.UI;

/// <summary>
/// Full-screen overlay showing a province's city in 3D (built by <see cref="CityBuilder"/>) in its own
/// world, so the map scene is untouched. Drag to turn around the city, scroll to zoom, Esc to close.
/// </summary>
public partial class CityView : Control
{
	[Export] public Font TitleFont { get; set; }
	[Export] public Font BodyFont { get; set; }

	SubViewport _viewport;
	Node3D _world;
	Node3D _city;
	Camera3D _camera;
	Label _title;
	Label _legend;

	float _yaw = 0.6f;
	float _pitch = 0.62f;
	float _distance = 70f;
	float _targetDistance = 70f;
	bool _dragging;
	bool _autoTurn = true;
	int _provinceId = -1;
	Vector3 _focus;

	public override void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;

		var container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
		container.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(container);
		_viewport = new SubViewport
		{
			OwnWorld3D = true,
			Msaa3D = Viewport.Msaa.Msaa2X,
			RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible,
		};
		container.AddChild(_viewport);

		_world = new Node3D { Name = "CityWorld" };
		_viewport.AddChild(_world);
		var sky = new ProceduralSkyMaterial
		{
			SkyTopColor = new Color(0.30f, 0.50f, 0.78f),
			SkyHorizonColor = new Color(0.78f, 0.80f, 0.78f),
			GroundBottomColor = new Color(0.42f, 0.46f, 0.40f),
			GroundHorizonColor = new Color(0.72f, 0.76f, 0.74f),
		};
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky { SkyMaterial = sky },
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = new Color(0.62f, 0.66f, 0.72f),
			AmbientLightEnergy = 0.45f,
		};
		_world.AddChild(new WorldEnvironment { Environment = env });
		_world.AddChild(new DirectionalLight3D
		{
			RotationDegrees = new Vector3(-48f, -35f, 0f),
			LightEnergy = 1.15f,
			LightColor = new Color(1f, 0.95f, 0.86f),
			ShadowEnabled = true,
			DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
			DirectionalShadowMaxDistance = 220f,
		});
		_camera = new Camera3D { Fov = 50f, Near = 0.5f, Far = 1500f, Current = true };
		_world.AddChild(_camera);

		BuildOverlay();
	}

	void BuildOverlay()
	{
		var top = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
		top.AddThemeStyleboxOverride("panel", HudStyle.Panel());
		top.SetAnchorsPreset(LayoutPreset.TopLeft);
		top.Position = new Vector2(16, 16);
		AddChild(top);
		var box = new VBoxContainer();
		top.AddChild(box);

		var header = new HBoxContainer();
		box.AddChild(header);
		_title = HudStyle.Title("", TitleFont, 24);
		header.AddChild(_title);
		header.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill });
		var close = HudStyle.Button("Back to map", BodyFont);
		close.Pressed += Close;
		header.AddChild(close);

		_legend = HudStyle.Body("", BodyFont, 15);
		box.AddChild(_legend);
		box.AddChild(HudStyle.Body("Drag to look around · scroll to zoom · Esc to close", BodyFont, 13, HudStyle.Muted));
	}

	public void Open(int provinceId)
	{
		Province p = GameState.Instance?.GetProvince(provinceId);
		if (p == null || p.IsWater)
			return;
		_provinceId = provinceId;
		Rebuild();
		Visible = true;
		_autoTurn = true;
	}

	public void Close()
	{
		Visible = false;
		_city?.QueueFree();
		_city = null;
		_provinceId = -1;
	}

	/// <summary>Rebuilds the city, e.g. after a building was built while the view is open.</summary>
	public void Rebuild()
	{
		Province p = GameState.Instance?.GetProvince(_provinceId);
		if (p == null)
			return;
		_city?.QueueFree();
		var builder = CityBuilder.Build(p, TitleFont);
		_city = builder;
		CityBuilder.AddTitle(_city, p, TitleFont);
		_world.AddChild(_city);

		_focus = (Vector3)_city.GetMeta("focus") + new Vector3(0, 2f, 0);
		float extent = (float)_city.GetMeta("extent");
		_targetDistance = _distance = Math.Clamp(extent * 1.7f, 35f, 140f);

		Culture culture = p.MainCulture;
		_title.Text = $"{p.Name}";
		var lines = new List<string>();
		string owner = GameState.Instance.GetCountry(p.OwnerTag)?.Name ?? "Unowned";
		lines.Add($"{owner} · {culture?.Name ?? "No"} style");
		foreach (PopGroup pop in p.Pops.OrderByDescending(g => g.Units))
			lines.Add($"  {pop.People:N0} {pop.Culture.Name} {pop.Occupation.Name.ToLowerInvariant()}");
		if (p.Pops.Count == 0)
			lines.Add("  Uninhabited");
		if (p.Buildings.Count > 0)
			lines.Add("Buildings: " + string.Join(", ", p.Buildings.Select(b => b.Level > 1 ? $"{b.Type.Name} ({b.Level})" : b.Type.Name)));
		if (p.Food != null)
			lines.Add($"Food: {p.Food.Name}");
		_legend.Text = string.Join("\n", lines);
	}

	public override void _Process(double delta)
	{
		if (!Visible)
			return;
		if (_autoTurn)
			_yaw += (float)delta * 0.08f;
		_distance = Mathf.Lerp(_distance, _targetDistance, 1f - MathF.Exp(-8f * (float)delta));
		var offset = new Vector3(MathF.Sin(_yaw) * MathF.Cos(_pitch), MathF.Sin(_pitch), MathF.Cos(_yaw) * MathF.Cos(_pitch)) * _distance;
		_camera.Position = _focus + offset;
		_camera.LookAt(_focus, Vector3.Up);
	}

	public override void _GuiInput(InputEvent e)
	{
		if (e is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex is MouseButton.Left or MouseButton.Right)
			{
				_dragging = mb.Pressed;
				_autoTurn = false;
			}
			else if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
				_targetDistance = Math.Max(15f, _targetDistance * 0.88f);
			else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
				_targetDistance = Math.Min(180f, _targetDistance / 0.88f);
			AcceptEvent();
		}
		else if (e is InputEventMouseMotion mm && _dragging)
		{
			_yaw -= mm.Relative.X * 0.006f;
			_pitch = Math.Clamp(_pitch + mm.Relative.Y * 0.004f, 0.12f, 1.35f);
			AcceptEvent();
		}
		else if (e is InputEventPanGesture pg)
		{
			_targetDistance = Math.Clamp(_targetDistance * (1f + pg.Delta.Y * 0.05f), 15f, 180f);
			AcceptEvent();
		}
	}

	public override void _Input(InputEvent e)
	{
		if (!Visible)
			return;
		// the map camera listens to keys too; keep them for the city while it is open
		if (e is InputEventKey key)
		{
			if (key.Pressed && key.Keycode == Key.Escape)
				Close();
			GetViewport().SetInputAsHandled();
		}
	}
}
