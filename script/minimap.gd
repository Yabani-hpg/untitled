extends TextureRect
## Minimap: shows the camera's view on the map texture and pans the camera on click / drag.
## The view box is the camera's real footprint on the ground (a trapezoid when the camera tilts), drawn
## once per world copy so it continues across the date line; in globe view it outlines the visible globe.

# --- World mapping (the flat map's bounds) -------------------------------
@export var world_origin_xz: Vector2 = Vector2(-2816.0, -1158.0) # north-west corner
@export var world_size_xz:  Vector2 = Vector2(5632.0, 2316.0)    # width, height
@export var ground_y: float = 0.0

# --- Nodes ----------------------------------------------------------------
@export var camera_path: NodePath
@export var pan_receiver_path: NodePath          # the camera rig (pan_to_world_xz, globe helpers)
@onready var _camera: Camera3D = get_node_or_null(camera_path)
@onready var _rig: Node3D = get_node_or_null(pan_receiver_path) as Node3D

# --- View box -------------------------------------------------------------
@export var rect_color: Color = Color(1, 1, 1, 0.95)
@export var rect_fill: Color = Color(1, 1, 1, 0.12)
@export var rect_shadow: Color = Color(0, 0, 0, 0.55)
@export var rect_thickness: float = 1.5
@export var edge_samples: int = 6                 # points per screen edge for the footprint

# --- Interaction ----------------------------------------------------------
@export var drag_to_pan: bool = true
@export var smooth_pan: bool = true

var _footprint: PackedVector2Array = PackedVector2Array()   # in map uv, u unwrapped around the view
var _dragging: bool = false

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_STOP

func _process(_delta: float) -> void:
	if _camera == null:
		return
	_footprint = _globe_footprint() if _in_globe_view() else _flat_footprint()
	queue_redraw()

func _draw() -> void:
	if _footprint.size() < 3:
		return
	var img: Rect2 = _image_rect()
	# one copy per world repeat; clip_contents trims whatever falls outside the minimap
	for shift in [-1.0, 0.0, 1.0]:
		var poly := PackedVector2Array()
		for uv in _footprint:
			poly.append(img.position + Vector2(uv.x + shift, uv.y) * img.size)
		if Geometry2D.triangulate_polygon(poly).size() > 0:
			draw_colored_polygon(poly, rect_fill)
		var loop := poly.duplicate()
		loop.append(poly[0])
		draw_polyline(loop, rect_shadow, rect_thickness + 2.0, true)
		draw_polyline(loop, rect_color, rect_thickness, true)

# --- Footprints -------------------------------------------------------------

func _in_globe_view() -> bool:
	return _rig != null and _rig.has_method("is_globe_view") and bool(_rig.call("is_globe_view"))

# Ground points along the screen border, in map uv, unwrapped so the polygon doesn't jump at the seam.
func _flat_footprint() -> PackedVector2Array:
	var vp: Vector2 = _camera.get_viewport().get_visible_rect().size
	var pts := PackedVector2Array()
	var ref_u: float = NAN
	for p in _screen_border(vp, edge_samples):
		var hit: Variant = _ground_hit(p)
		if hit == null:
			continue
		var uv: Vector2 = _world_to_uv(hit as Vector2)
		if is_nan(ref_u):
			ref_u = uv.x
		uv.x = ref_u + wrapf(uv.x - ref_u, -0.5, 0.5)
		uv.y = clampf(uv.y, 0.0, 1.0)
		pts.append(uv)
	return _normalize_u(pts)

# Globe view: bounding box of the globe points visible on screen.
func _globe_footprint() -> PackedVector2Array:
	var vp: Vector2 = _camera.get_viewport().get_visible_rect().size
	var top: float = float(_rig.get("map_top_lat"))
	var bottom: float = float(_rig.get("map_bottom_lat"))
	var center: Vector2 = _rig.call("flat_xz_to_lonlat", _rig.global_position.x, _rig.global_position.z)
	var umin := INF
	var umax := -INF
	var vmin := INF
	var vmax := -INF
	for iy in range(10):
		for ix in range(16):
			var sp := Vector2((ix + 0.5) / 16.0 * vp.x, (iy + 0.5) / 10.0 * vp.y)
			var res: Array = _rig.call("globe_screen_lonlat", sp)
			if not bool(res[0]):
				continue
			var ll: Vector2 = res[1]
			var u: float = (center.x + wrapf(ll.x - center.x, -180.0, 180.0) + 180.0) / 360.0
			var v: float = clampf((top - ll.y) / (top - bottom), 0.0, 1.0)
			umin = min(umin, u); umax = max(umax, u)
			vmin = min(vmin, v); vmax = max(vmax, v)
	if umin == INF:
		return PackedVector2Array()
	return _normalize_u(PackedVector2Array([Vector2(umin, vmin), Vector2(umax, vmin), Vector2(umax, vmax), Vector2(umin, vmax)]))

static func _screen_border(vp: Vector2, n: int) -> PackedVector2Array:
	var pts := PackedVector2Array()
	for i in range(n):
		pts.append(Vector2(vp.x * i / n, 0.0))
	for i in range(n):
		pts.append(Vector2(vp.x, vp.y * i / n))
	for i in range(n):
		pts.append(Vector2(vp.x * (n - i) / n, vp.y))
	for i in range(n):
		pts.append(Vector2(0.0, vp.y * (n - i) / n))
	return pts

# Shift the whole polygon by whole map widths so its centre lies on the map (0..1).
static func _normalize_u(pts: PackedVector2Array) -> PackedVector2Array:
	if pts.is_empty():
		return pts
	var cu := 0.0
	for p in pts:
		cu += p.x
	var shift: float = floor(cu / pts.size())
	for i in range(pts.size()):
		pts[i].x -= shift
	return pts

# --- Mapping ----------------------------------------------------------------

# Where the texture is drawn inside the control (stretch modes may letterbox it).
func _image_rect() -> Rect2:
	if texture == null:
		return Rect2(Vector2.ZERO, size)
	var ts: Vector2 = texture.get_size()
	if stretch_mode == TextureRect.STRETCH_KEEP_ASPECT_CENTERED:
		var s: float = min(size.x / ts.x, size.y / ts.y)
		return Rect2((size - ts * s) * 0.5, ts * s)
	return Rect2(Vector2.ZERO, size)

func _world_to_uv(xz: Vector2) -> Vector2:
	return (xz - world_origin_xz) / world_size_xz

func _ground_hit(screen_pt: Vector2) -> Variant:
	var from: Vector3 = _camera.project_ray_origin(screen_pt)
	var dir: Vector3 = _camera.project_ray_normal(screen_pt)
	if dir.y > -1e-4:
		return null    # looking at or above the horizon
	var hit: Vector3 = from + dir * ((ground_y - from.y) / dir.y)
	return Vector2(hit.x, hit.z)

# --- Input ------------------------------------------------------------------

func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and (event as InputEventMouseButton).button_index == MOUSE_BUTTON_LEFT:
		var mb := event as InputEventMouseButton
		_dragging = mb.pressed
		if mb.pressed:
			_pan_to(mb.position)
		accept_event()
	elif event is InputEventMouseMotion and _dragging and drag_to_pan:
		_pan_to((event as InputEventMouseMotion).position)
		accept_event()

# Centre the camera on the map point under a minimap position, taking the shortest way around the world.
func _pan_to(local_pos: Vector2) -> void:
	if _rig == null or not _rig.has_method("pan_to_world_xz"):
		return
	var img: Rect2 = _image_rect()
	var uv: Vector2 = ((local_pos - img.position) / img.size).clamp(Vector2.ZERO, Vector2.ONE)
	var target: Vector2 = world_origin_xz + uv * world_size_xz
	target.x = _rig.global_position.x + wrapf(target.x - _rig.global_position.x, -0.5 * world_size_xz.x, 0.5 * world_size_xz.x)
	_rig.call("pan_to_world_xz", target, smooth_pan)
