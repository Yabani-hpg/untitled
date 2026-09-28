extends Node3D
## Paradox-style map camera with seamless horizontal wrapping (multi-tile).
## Zoom is multiplicative per wheel notch and keeps the point under the cursor fixed; the camera looks
## almost straight down when far out and tilts toward the horizon only near the ground; pan speed
## scales with zoom; middle-drag grabs the map.

@export var auto_center_on_start: bool = true         # run once on game start
@export var start_zoom_in_fraction: float = 0.35      # 15% closer than current target zoom
@export var start_focus_instant: bool = true          # instant or smooth

var _did_start_focus: bool = false


@export var tile_width_override: float = 0     # 0 = auto-measure; else exact visual width in world units
@export var wrap_tiles_each_side: int = 5       # how many tiles to spawn to left/right (increase if you still see gaps)
@export var extra_cull_margin: float = 4.0       # helps hide tiny culling gaps at long distances
const SEAM_EPS := 0.02                           # tiny overlap to hide FP seams

# --- Optional explicit edge markers (stronger than auto-measure) ---
@export var wrap_edge_west_path: NodePath        # OPTIONAL: Node3D at the far WEST edge of the map
@export var wrap_edge_east_path: NodePath        # OPTIONAL: Node3D at the far EAST  edge of the map

# --- Camera speeds / limits ---
@export var pan_speed: float = 1000.0         # world units/s at zoom distance 1000; scales with zoom
@export var edge_pan_speed: float = 1000.0    # same, for screen-edge panning
@export var zoom_step_ratio: float = 0.85     # each wheel notch multiplies the zoom distance by this (or its inverse)
@export var min_zoom: float = 100.0
@export var max_zoom: float = 2000.0

# --- Smoothing (time to ~90% target) ---
@export var pan_smooth_time: float = 0.15
@export var zoom_smooth_time: float = 0.12

# --- Edge panning ---
@export var enable_edge_pan: bool = true
@export var edge_margin_px: int = 16
@export var edge_corner_scale: float = 0.75

# --- Rotation + auto tilt ---
@export var rotate_speed: float = 70.0
@export var auto_tilt: bool = true
@export var min_pitch_deg: float = -75.0      # pitch at the closest zoom
@export var max_pitch_deg: float = -90.0      # pitch from tilt_zoom_range outward
@export var tilt_zoom_range: float = 0.45     # fraction of the (logarithmic) zoom range over which the camera tilts

# --- Zoom to cursor ---
@export var zoom_to_cursor: bool = true
@export var ground_y: float = 0.0

@onready var cam: Camera3D = $Camera3D

# --- Rect bounds (used for Z clamp; X clamp only when globe is ON) ---
@export var use_bounds: bool = true
@export var bounds_min_x: float = -2816.0
@export var bounds_max_x: float =  2816.0
@export var bounds_min_z: float = -1158.0
@export var bounds_max_z: float =  1158.0

# --- Focus ---
@export var focus_padding_xz: float = 0.0
@export var focus_default_zoom: float = -1.0


# --- FOV ---
@export var fov_close: float = 90.0
@export var fov_far: float = 30.0

# --- Visual tiling container paths ---
@export var tile_flat_x: bool = true
@export var flat_world_path: NodePath
@export var globe_world_path: NodePath

# --- Globe switch ---
@export var globe_enable: bool = true
@export var globe_radius: float = 1200.0
@export var globe_zoom_threshold: float = 0.85
@export var globe_blend_time: float = 0.2

var active_tween: Tween = null
var velocity: Vector3 = Vector3.ZERO
var edge_pan_velocity: Vector2 = Vector2.ZERO

var _pan_target: Vector3 = Vector3.ZERO
var _pan_active := false

var _wrap_axis: Vector3 = Vector3(1, 0, 0)   # world-space east–west axis
var _wrap_anchor: Vector3 = Vector3.ZERO     # midpoint on that axis

var _wrap_ready: bool = false


# Internals
var _base_origin: Vector3


var _flat_world: Node3D
var _globe_world: Node3D
var _globe_on: bool = false
var _globe_blend: float = 0.0

var _tile_width: float = 0.0
var _wrap_anchor_x: float = 0.0

var _target_yaw: float = 0.0
var _dragging: bool = false
var _grab_world: Vector3 = Vector3.ZERO      # map point held under the cursor while middle-dragging

# zoom-to-cursor: keep this map point under this screen position while the zoom eases in
var _zoom_anchor_active: bool = false
var _zoom_anchor_screen: Vector2 = Vector2.ZERO
var _zoom_anchor_world: Vector3 = Vector3.ZERO

var _target_pos: Vector3
var _target_zoom: float
var _curr_zoom: float

# Pool of wrap tiles: offset index (int) -> Node3D (0 is the base)
var _wrap_tiles: Dictionary = {}   # key: int, value: Node3D

func _ready() -> void:
	_target_pos = global_transform.origin
	_curr_zoom = cam.transform.origin.length()
	_target_zoom = clampf(_curr_zoom, min_zoom, max_zoom)

	if flat_world_path != NodePath():
		_flat_world = get_node_or_null(flat_world_path) as Node3D
	if globe_world_path != NodePath():
		_globe_world = get_node_or_null(globe_world_path) as Node3D
	if is_instance_valid(_globe_world):
		_globe_world.visible = false

	# --- Resolve tile width + anchor (markers > auto-measure/override) ---
	var have_markers: bool = _try_use_edge_markers()
	if not have_markers:
		var measured_width: float = 0.0
		var measured_center: float = global_transform.origin.x
		if is_instance_valid(_flat_world):
			var m: Dictionary = _measure_tile_x(_flat_world)
			measured_width  = float(m.get("width", 0.0))
			measured_center = float(m.get("center", _flat_world.global_transform.origin.x))
			_base_origin = _flat_world.global_transform.origin

		# pick width: override > measured
		if tile_width_override > 0.0:
			_tile_width = tile_width_override
			_wrap_anchor_x = measured_center
		else:
			_tile_width = measured_width
			_wrap_anchor_x = measured_center

		# IMPORTANT: set axis/anchor whenever NOT using markers
		if _tile_width > 0.0:
			_wrap_axis = Vector3(1, 0, 0)
			_wrap_anchor = Vector3(_wrap_anchor_x, 0.0, 0.0)

	# --- Build wrap pool and lay it out ---
	if tile_flat_x and is_instance_valid(_flat_world) and _tile_width > 0.0:
		_build_wrap_pool()
		


func _try_use_edge_markers() -> bool:
	if wrap_edge_west_path == NodePath() or wrap_edge_east_path == NodePath():
		return false
	var west_node := get_node_or_null(wrap_edge_west_path) as Node3D
	var east_node := get_node_or_null(wrap_edge_east_path) as Node3D
	if west_node == null or east_node == null:
		return false

	var pw: Vector3 = west_node.global_transform.origin
	var pe: Vector3 = east_node.global_transform.origin
	var delta: Vector3 = pe - pw
	@warning_ignore("shadowed_global_identifier")
	var len: float = delta.length()
	if len <= 0.0001:
		return false

	_wrap_axis = delta / len
	_tile_width = len
	_wrap_anchor = pw + delta * 0.5
	return _tile_width > 0.0


func _build_wrap_pool() -> void:
	# Clean any previous clones
	for n in _wrap_tiles.values():
		if n != _flat_world and is_instance_valid(n):
			(n as Node3D).queue_free()

	_wrap_tiles.clear()
	_wrap_tiles[0] = _flat_world

	var parent := _flat_world.get_parent()
	if parent == null:
		push_error("FlatWorld has no parent; cannot attach clones.")
		return

	for k in range(-wrap_tiles_each_side, wrap_tiles_each_side + 1):
		if k == 0:
			continue
		var clone := _flat_world.duplicate() as Node3D
		_wrap_tiles[k] = clone

		# Defer adding child to avoid "parent is busy" error
		parent.call_deferred("add_child", clone)
		# Defer property sets until after it's in the tree
		clone.set_deferred("name", "%s_%+d" % [_flat_world.name, k])
		clone.set_deferred("top_level", true) # absolute world transforms
		# DO NOT set owner at runtime; it's for editor saving and must be an ancestor.

	# After all clones are queued, defer the layout step
	call_deferred("_post_build_layout")



func _layout_wrap_row() -> void:
	if !is_instance_valid(_flat_world) or _tile_width <= 0.0 or !_flat_world.is_inside_tree():
		return

	var span := _tile_width - SEAM_EPS
	var base_origin := _flat_world.global_transform.origin

	for k_obj in _wrap_tiles.keys():
		var idx := int(k_obj)
		if idx == 0: continue
		var node := _wrap_tiles[idx] as Node3D
		if node == null or !node.is_inside_tree(): continue
		node.global_position = base_origin + Vector3(span * float(idx), 0.0, 0.0)




func _update_wrap_visibility() -> void:
	# hide all wrap tiles when globe is on
	var make_visible: bool = (_globe_blend < 0.5)
	var keys: Array = _wrap_tiles.keys()
	for k_untyped in _wrap_tiles.keys():
		var k: int = int(k_untyped)
		var node := _wrap_tiles[k] as Node3D
		if node != null:
			node.visible = make_visible

	# globe node visibility (on at/after halfway point)
	if is_instance_valid(_globe_world):
		_globe_world.visible = !make_visible  # i.e., (_globe_blend >= 0.5)

func _set_extra_cull_margin_all(margin: float) -> void:
	if margin <= 0.0:
		return
	var keys: Array = _wrap_tiles.keys()
	for k_untyped in keys:
		var k: int = int(k_untyped)
		var node: Node3D = _wrap_tiles[k] as Node3D
		if node == null:
			continue
		_set_extra_cull_margin(node, margin)

func _set_extra_cull_margin(root: Node3D, margin: float) -> void:
	var stack: Array[Node3D] = [root]
	while not stack.is_empty():
		var n: Node3D = stack.pop_back()
		for child in n.get_children():
			if child is Node3D:
				stack.push_back(child as Node3D)
		if n is VisualInstance3D:
			var vi: VisualInstance3D = n as VisualInstance3D
			vi.extra_cull_margin = margin

func _normalize_rig_x() -> void:
	if _tile_width <= 0.0 or _globe_blend >= 0.5:
		return

	var axis: Vector3 = _wrap_axis
	if axis.length() < 0.5:
		axis = Vector3(1, 0, 0)
	else:
		axis = axis.normalized()

	var half: float = 0.5 * _tile_width
	var rig_pos: Vector3 = global_transform.origin
	var dx: float = (rig_pos - _wrap_anchor).dot(axis)

	if dx > half:
		var k: int = int(floor((dx + half) / _tile_width))
		var shift_vec: Vector3 = axis * (_tile_width * float(k))
		global_transform.origin -= shift_vec
		_target_pos            -= shift_vec
	elif dx < -half:
		var k: int = int(floor(((-dx) + half) / _tile_width))
		var shift_vec: Vector3 = axis * (_tile_width * float(k))
		global_transform.origin += shift_vec
		_target_pos            += shift_vec

func _physics_process(delta: float) -> void:
	var move_input: Vector3 = _collect_keyboard_pan()
	var edge_input: Vector3 = Vector3.ZERO
	if enable_edge_pan:
		edge_input = _collect_edge_pan()

	if _globe_on:
		move_input = Vector3.ZERO
		edge_input = Vector3.ZERO

	var zoom_scale: float = _curr_zoom / 1000.0
	if move_input != Vector3.ZERO:
		_zoom_anchor_active = false
		_target_pos += (basis.x * move_input.x + basis.z * move_input.z) * pan_speed * zoom_scale * delta
	if edge_input != Vector3.ZERO:
		_zoom_anchor_active = false
		if edge_input.length() > 1.0:
			edge_input = edge_input.normalized() * edge_corner_scale
		_target_pos += (basis.x * edge_input.x + basis.z * edge_input.z) * edge_pan_speed * zoom_scale * delta

	if use_bounds:
		_target_pos = _apply_bounds(_target_pos)

	# Smooth pos & zoom
	global_transform.origin = _exp_smooth_vec3(global_transform.origin, _target_pos, pan_smooth_time, delta)
	_curr_zoom = _exp_smooth_float(_curr_zoom, _target_zoom, zoom_smooth_time, delta)
	_apply_zoom_and_pitch(_curr_zoom)
	_hold_zoom_anchor()

	# Wrap rig + keep the row lined up to the base every frame
	if not _globe_on:
		_normalize_rig_x()
	if tile_flat_x and _wrap_ready:
		_layout_wrap_row()

	# Globe alignment (same as before)
	var center_hit: Array = _center_ground_hit()
	if bool(center_hit[0]):
		var p: Vector3 = center_hit[1] as Vector3
		var uv: Vector2 = _flat_xz_to_uv(p.x, p.z)
		var lonlat: Vector2 = _uv_to_lonlat(uv)
		_align_globe_to_lonlat(lonlat.x, lonlat.y)

	# Smooth yaw
	var current_yaw: float = rotation_degrees.y
	current_yaw = lerp(current_yaw, _target_yaw, 5.0 * delta)
	rotation_degrees.y = current_yaw

	# Globe blend logic
	var zoom_t: float = 0.0
	if max_zoom > min_zoom:
		zoom_t = clampf((_curr_zoom - min_zoom) / (max_zoom - min_zoom), 0.0, 1.0)
	var want_globe: bool = globe_enable and (zoom_t >= globe_zoom_threshold)
	var target_blend: float = 1.0 if want_globe else 0.0
	_globe_blend = _exp_smooth_float(_globe_blend, target_blend, max(globe_blend_time, 0.0001), delta)
	_apply_globe_crossfade()
	_globe_on = (_globe_blend >= 0.5)
	_update_wrap_visibility()
	
	if _pan_active:
		var d := _pan_target - global_position
		if d.length() <= 0.05:
			global_position = _pan_target
			_pan_active = false
			_cancel_motion_state()
		else:
			var step := d.normalized() * pan_speed * delta
			if step.length() > d.length():
				step = d
			global_position += step
		return  # <-- important: don't let other logic run this frame

func _cancel_motion_state() -> void:
	# Stop any running tween that could yank the rig back
	if active_tween != null and is_instance_valid(active_tween):
		if active_tween.is_running():
			active_tween.kill()
		active_tween = null

	# Clear any motion you use (keep these if you reference them elsewhere)
	velocity = Vector3.ZERO
	edge_pan_velocity = Vector2.ZERO
func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		var mb: InputEventMouseButton = event as InputEventMouseButton
		if mb.pressed and (mb.button_index == MOUSE_BUTTON_WHEEL_UP or mb.button_index == MOUSE_BUTTON_WHEEL_DOWN):
			var ratio: float = zoom_step_ratio if mb.button_index == MOUSE_BUTTON_WHEEL_UP else 1.0 / zoom_step_ratio
			# trackpads send fractional notches
			if mb.factor > 0.0:
				ratio = pow(ratio, mb.factor)
			_target_zoom = clampf(_target_zoom * ratio, min_zoom, max_zoom)
			if zoom_to_cursor and not _globe_on:
				var result: Array = _cursor_ground_hit()
				if bool(result[0]):
					_zoom_anchor_active = true
					_zoom_anchor_screen = mb.position
					_zoom_anchor_world = result[1] as Vector3
		elif mb.button_index == MOUSE_BUTTON_MIDDLE:
			_dragging = mb.pressed and not _globe_on
			if _dragging:
				_zoom_anchor_active = false
				var grab: Array = _screen_ground_hit(mb.position)
				_dragging = bool(grab[0])
				_grab_world = grab[1] as Vector3

	elif event is InputEventMouseMotion and _dragging:
		var mm: InputEventMouseMotion = event as InputEventMouseMotion
		var now: Array = _screen_ground_hit(mm.position)
		if bool(now[0]):
			_shift_rig(_grab_world - (now[1] as Vector3))

	elif event is InputEventKey:
		var kev: InputEventKey = event as InputEventKey
		if kev.pressed:
			if kev.keycode == KEY_F:
				var result2: Array = _cursor_ground_hit()
				if bool(result2[0]):
					focus(result2[1] as Vector3, -1.0, kev.shift_pressed)
			elif kev.keycode == KEY_Q:
				_target_yaw = -30.0
			elif kev.keycode == KEY_E:
				_target_yaw = 30.0
		else:
			if kev.keycode == KEY_Q or kev.keycode == KEY_E:
				_target_yaw = 0.0
# ---- Helpers ---------------------------------------------------------------
func _measure_tile_x(root: Node3D) -> Dictionary:
	var min_x: float = INF
	var max_x: float = -INF

	# Typed stack to avoid Variant warnings
	var stack: Array[Node3D] = [root]
	while not stack.is_empty():
		var n: Node3D = stack.pop_back()

		# Push typed children
		for child in n.get_children():
			if child is Node3D:
				stack.push_back(child as Node3D)

		# Only measure visible VisualInstance3D (MeshInstance3D, Sprite3D, etc.)
		if n is VisualInstance3D:
			var vi := n as VisualInstance3D
			if not vi.visible:
				continue
			var aabb: AABB = vi.get_aabb()
			for i in range(8):
				var wp: Vector3 = (n as Node3D).global_transform * aabb.get_endpoint(i)
				if wp.x < min_x: min_x = wp.x
				if wp.x > max_x: max_x = wp.x

	# If nothing visible was found, fall back to the node’s current X
	if min_x == INF:
		return {"width": 0.0, "center": root.global_transform.origin.x}

	return {
		"width":  max_x - min_x,
		"center": 0.5 * (min_x + max_x)
	}
func _collect_keyboard_pan() -> Vector3:
	var v: Vector3 = Vector3.ZERO
	if Input.is_action_pressed("ui_right"): v.x += 1.0
	if Input.is_action_pressed("ui_left"):  v.x -= 1.0
	if Input.is_action_pressed("ui_up"):    v.z -= 1.0
	if Input.is_action_pressed("ui_down"):  v.z += 1.0
	return v

func _collect_edge_pan() -> Vector3:
	var vp: Viewport = get_viewport()
	if vp == null:
		return Vector3.ZERO
	var size: Vector2 = vp.get_visible_rect().size
	var mpos: Vector2 = vp.get_mouse_position()
	var v: Vector3 = Vector3.ZERO
	if mpos.x <= float(edge_margin_px): v.x -= 1.0
	elif mpos.x >= size.x - float(edge_margin_px): v.x += 1.0
	if mpos.y <= float(edge_margin_px): v.z -= 1.0
	elif mpos.y >= size.y - float(edge_margin_px): v.z += 1.0
	return v

func _apply_zoom_and_pitch(dist: float) -> void:
	# zoom feels even on a log scale; tilt happens only in the closest part of the range
	var t: float = clampf(log(dist / min_zoom) / max(0.001, log(max_zoom / min_zoom)), 0.0, 1.0)
	if auto_tilt:
		var tilt_t: float = smoothstep(0.0, max(tilt_zoom_range, 0.001), t)
		cam.rotation_degrees.x = lerp(min_pitch_deg, max_pitch_deg, tilt_t)
	cam.fov = lerp(fov_close, fov_far, t)
	var tform: Transform3D = cam.transform
	var forward: Vector3 = tform.basis.z.normalized()
	tform.origin = forward * dist
	cam.transform = tform

# Moves the rig (now and its target) by a world offset; x wraps so a map seam in between doesn't matter.
func _shift_rig(offset: Vector3) -> void:
	offset.y = 0.0
	if _tile_width > 0.0:
		offset.x = fposmod(offset.x + 0.5 * _tile_width, _tile_width) - 0.5 * _tile_width
	var pos: Vector3 = global_transform.origin + offset
	if use_bounds:
		pos = _apply_bounds(pos)
	global_transform.origin = pos
	_target_pos = pos
	_pan_active = false

# While the zoom eases toward its target, keep the anchored map point under the cursor.
func _hold_zoom_anchor() -> void:
	if not _zoom_anchor_active:
		return
	if _globe_on:
		_zoom_anchor_active = false
		return
	var hit: Array = _screen_ground_hit(_zoom_anchor_screen)
	if bool(hit[0]):
		_shift_rig(_zoom_anchor_world - (hit[1] as Vector3))
	if absf(_curr_zoom - _target_zoom) <= 0.002 * _target_zoom:
		_zoom_anchor_active = false

# Clamp Z always; clamp X only when globe is on (no flat-mode X clamp).
func _apply_bounds(p: Vector3) -> Vector3:
	var px: float = p.x
	var pz: float = p.z
	if _globe_blend >= 0.5:
		px = clampf(px, bounds_min_x, bounds_max_x)
	pz = clampf(pz, bounds_min_z, bounds_max_z)
	return Vector3(px, p.y, pz)

# --- Measuring the visible tile width/center (world X) ----------------------

func _measure_tile_along_axis(root: Node3D, axis: Vector3) -> Dictionary:
	var n_axis: Vector3 = axis.normalized()
	if n_axis.length() < 0.5:
		n_axis = Vector3(1, 0, 0)

	var min_t: float = INF
	var max_t: float = -INF

	var stack: Array[Node3D] = [root]
	while not stack.is_empty():
		var n: Node3D = stack.pop_back()
		for child in n.get_children():
			if child is Node3D:
				stack.push_back(child as Node3D)
		if n is VisualInstance3D:
			var vi := n as VisualInstance3D
			if not vi.visible:
				continue
			var aabb: AABB = vi.get_aabb()
			for i in range(8):
				var wp: Vector3 = (n as Node3D).global_transform * aabb.get_endpoint(i)
				var t: float = wp.dot(n_axis)
				if t < min_t: min_t = t
				if t > max_t: max_t = t

	if min_t == INF:
		return {"width": 0.0, "anchor": root.global_transform.origin}

	var center_t: float = 0.5 * (min_t + max_t)
	var root_o: Vector3 = root.global_transform.origin
	var perp: Vector3 = root_o - n_axis * root_o.dot(n_axis)
	var anchor_point: Vector3 = perp + n_axis * center_t
	return {"width": (max_t - min_t), "anchor": anchor_point}

# --- Rays / globe helpers ----------------------------------------------------

func _cursor_ground_hit() -> Array:
	var vp: Viewport = get_viewport()
	if vp == null:
		return [false, Vector3.ZERO]
	return _screen_ground_hit(vp.get_mouse_position())

# Ground point under a screen position (the map's base plane; terrain relief is visual only).
func _screen_ground_hit(mpos: Vector2) -> Array:
	var origin: Vector3 = cam.project_ray_origin(mpos)
	var dir: Vector3 = cam.project_ray_normal(mpos)
	if absf(dir.y) < 1e-6:
		return [false, Vector3.ZERO]
	var t: float = (ground_y - origin.y) / dir.y
	if t < 0.0:
		return [false, Vector3.ZERO]
	return [true, origin + dir * t]

func _center_ground_hit() -> Array:
	var vp: Viewport = get_viewport()
	if vp == null:
		return [false, Vector3.ZERO]
	return _screen_ground_hit(vp.get_visible_rect().size * 0.5)

func _flat_xz_to_uv(x: float, z: float) -> Vector2:
	var w: float = max(0.0001, bounds_max_x - bounds_min_x)
	var h: float = max(0.0001, bounds_max_z - bounds_min_z)
	var u: float = (x - bounds_min_x) / w
	var v: float = (z - bounds_min_z) / h
	v = 1.0 - v
	return Vector2(clampf(u, 0.0, 1.0), clampf(v, 0.0, 1.0))

func _uv_to_lonlat(uv: Vector2) -> Vector2:
	return Vector2(uv.x * 360.0 - 180.0, uv.y * 180.0 - 90.0)

func _align_globe_to_lonlat(lon_deg: float, lat_deg: float) -> void:
	if _globe_world == null:
		return
	var yaw_y: float = -deg_to_rad(lon_deg)
	var pitch_x: float =  deg_to_rad(lat_deg)
	var b: Basis = Basis().rotated(Vector3(0, 1, 0), yaw_y).rotated(Vector3(1, 0, 0), pitch_x)
	var t: Transform3D = _globe_world.transform
	t.basis = b
	_globe_world.transform = t

# --- Smoothing ---------------------------------------------------------------

func _exp_smooth_float(curr: float, target: float, time_to_90: float, dt: float) -> float:
	if time_to_90 <= 0.0:
		return target
	var k: float = 2.302585093 / time_to_90
	var a: float = 1.0 - exp(-k * dt)
	return lerp(curr, target, a)

func _exp_smooth_vec3(curr: Vector3, target: Vector3, time_to_90: float, dt: float) -> Vector3:
	if time_to_90 <= 0.0:
		return target
	var k: float = 2.302585093 / time_to_90
	var a: float = 1.0 - exp(-k * dt)
	return curr.lerp(target, a)

func focus(world_pos: Vector3, zoom: float = -1.0, instant: bool = false) -> void:
	var tgt: Vector3 = Vector3(world_pos.x, _target_pos.y, world_pos.z)
	if focus_padding_xz > 0.0:
		var bx0: float = bounds_min_x + focus_padding_xz
		var bx1: float = bounds_max_x - focus_padding_xz
		var bz0: float = bounds_min_z + focus_padding_xz
		var bz1: float = bounds_max_z - focus_padding_xz
		tgt.x = clampf(tgt.x, bx0, bx1)
		tgt.z = clampf(tgt.z, bz0, bz1)
	if use_bounds:
		tgt = _apply_bounds(tgt)
	_target_pos = tgt
	var desired_zoom: float = _target_zoom
	if zoom >= 0.0:
		desired_zoom = clampf(zoom, min_zoom, max_zoom)
	elif focus_default_zoom >= 0.0:
		desired_zoom = clampf(focus_default_zoom, min_zoom, max_zoom)
	_target_zoom = desired_zoom
	if instant:
		global_transform.origin = _target_pos
		_curr_zoom = _target_zoom
		_apply_zoom_and_pitch(_curr_zoom)
		
func _post_build_layout() -> void:
	# Wait until all wrap tiles are inside the tree
	for k in _wrap_tiles.keys():
		var n := _wrap_tiles[int(k)] as Node3D
		if n == null or !n.is_inside_tree():
			call_deferred("_post_build_layout")
			return

	_set_extra_cull_margin_all(extra_cull_margin)
	_layout_wrap_row()
	_update_wrap_visibility()
	_wrap_ready = true
	_wrap_ready = true
	if auto_center_on_start:
		call_deferred("_start_focus_default")  # next frame, after everything is in-tree



func _set_transparency_recursive(root: Node3D, t: float) -> void:
	var stack: Array[Node3D] = [root]
	while not stack.is_empty():
		var n: Node3D = stack.pop_back()
		for c in n.get_children():
			if c is Node3D:
				stack.push_back(c as Node3D)
		if n is GeometryInstance3D:
			(n as GeometryInstance3D).transparency = clampf(t, 0.0, 1.0)



func _apply_globe_crossfade() -> void:
	# _globe_blend goes 0→1 as you zoom past globe_zoom_threshold
	var t := clampf(_globe_blend, 0.0, 1.0)

	# Flat tiles fade OUT
	for k in _wrap_tiles.keys():
		var node := _wrap_tiles[int(k)] as Node3D
		if node: _set_transparency_recursive(node, t)

	# Globe fades IN
	if is_instance_valid(_globe_world):
		_set_transparency_recursive(_globe_world, 1.0 - t)

	# Hide/show at the extremes (performance optimization)
	var flat_visible := t < 0.999
	for k in _wrap_tiles.keys():
		var node := _wrap_tiles[int(k)] as Node3D
		if node: node.visible = flat_visible
	if is_instance_valid(_globe_world):
		_globe_world.visible = (1.0 - t) < 0.999

func pan_to_world_xz(xz: Vector2, smooth: bool = true) -> void:
	var y: float = global_transform.origin.y
	var target: Vector3 = Vector3(xz.x, y, xz.y)

	# If you use bounds, keep them honored
	if use_bounds:
		target = _apply_bounds(target)

	# ❗ Update the rig's authoritative target so it won't snap back
	_target_pos = target

	# Stop any motion/tweens that could pull us back
	_cancel_motion_state()

	if smooth:
		_pan_target = target
		_pan_active = true
	else:
		_pan_active = false
		global_transform.origin = target

func _start_focus_default() -> void:
	if _did_start_focus or !auto_center_on_start:
		return
	_did_start_focus = true

	# Center of your flat world bounds (defaults you already set: -2816..+2816, -1158..+1158)
	var cx: float = 0.5 * (bounds_min_x + bounds_max_x)
	var cz: float = 0.5 * (bounds_min_z + bounds_max_z)

	# Zoom in a little from whatever the rig initialized as its target zoom
	var desired_zoom: float = clampf(_target_zoom - start_zoom_in_fraction * (max_zoom - min_zoom), min_zoom, max_zoom)

	# Use the built-in focus() which updates _target_pos/_target_zoom correctly
	focus(Vector3(cx, _target_pos.y, cz), desired_zoom, start_focus_instant)
