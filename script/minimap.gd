extends TextureRect

@export var fallback_teleport_if_no_receiver: bool = true

# --- World mapping (set these to your flat map bounds) --------------------
@export var world_origin_xz: Vector2 = Vector2(-2816.0, -1158.0) # bottom-left
@export var world_size_xz:  Vector2 = Vector2(5632.0, 2316.0)    # width,height
@export var ground_y: float = 0.0
@export var wrap_horizontally: bool = true

# If rectangle is mirrored/rotated, flip these until it matches:
@export var invert_u: bool = false
@export var invert_v: bool = false
@export var swap_xz:  bool = false

# --- Nodes ----------------------------------------------------------------
@export var camera_path: NodePath
@export var pan_receiver_path: NodePath
@onready var _camera: Camera3D = get_node_or_null(camera_path)
var _pan_receiver: Node = null    # set in _ready

# --- View-rect rendering --------------------------------------------------
@export var rect_color: Color = Color(1, 1, 1, 1)  # outline color
@export var rect_thickness: float = 2.0           # outline thickness px
var _rects: Array[Rect2] = []                     # computed each frame

# --- Interaction ----------------------------------------------------------
@export var center_on_click: bool = true          # center camera on click
@export var drag_to_pan: bool = true              # hold LMB and drag to pan
@export var smooth_pan: bool = true               # pass 'true' to pan_to_world_xz

var _dragging: bool = false
var _drag_prefer_x: float = 0.0                   # keeps wrap continuity

# -------------------------------------------------------------------------

func _ready() -> void:
	if not pan_receiver_path.is_empty():
		_pan_receiver = get_node_or_null(pan_receiver_path)
	elif _camera != null and _camera.get_parent() != null:
		_pan_receiver = _camera.get_parent()
	# Make sure this control receives input and sits on top
	mouse_filter = Control.MOUSE_FILTER_STOP
	z_index = 1000
	print("[Minimap] READY rect=", get_rect(), " global=", get_global_rect())

	# Ensure the parent Panel doesn't swallow events
	var p := get_parent()
	if p is Control:
		(p as Control).mouse_filter = Control.MOUSE_FILTER_PASS
		# or Control.MOUSE_FILTER_IGNORE also works

	# Optional: visibility of hover is a quick sanity check
	mouse_entered.connect(func(): print("[Minimap] mouse_entered"))
	mouse_exited.connect(func(): print("[Minimap] mouse_exited"))


func _process(_delta: float) -> void:
	if _camera == null:
		return
	_update_view_rects()
	queue_redraw()

func _draw() -> void:
	for r in _rects:
		draw_rect(r, rect_color, false, rect_thickness)  # outline only

# -------------------------------------------------------------------------

func _world_xz_to_uv(xz: Vector2) -> Vector2:
	var X: float = xz.x
	var Z: float = xz.y
	if swap_xz:
		var tmp := X
		X = Z
		Z = tmp

	var u: float = (X - world_origin_xz.x) / world_size_xz.x
	var v: float = (Z - world_origin_xz.y) / world_size_xz.y

	if wrap_horizontally:
		u = fposmod(u, 1.0)

	if invert_u:
		u = 1.0 - u
	v = clampf(v, 0.0, 1.0)
	if invert_v:
		v = 1.0 - v

	return Vector2(u, v)

func _uv_to_world_xz(uv_in: Vector2, prefer_near_x: float = INF) -> Vector2:
	var u: float = uv_in.x
	var v: float = uv_in.y
	if invert_u:
		u = 1.0 - u
	if invert_v:
		v = 1.0 - v

	var X: float = world_origin_xz.x + u * world_size_xz.x
	var Z: float = world_origin_xz.y + v * world_size_xz.y

	if wrap_horizontally and is_finite(prefer_near_x):
		var period: float = world_size_xz.x
		var k: float = round((prefer_near_x - X) / period)
		X += k * period

	if swap_xz:
		return Vector2(Z, X)
	return Vector2(X, Z)

func _intersect_screen_to_ground(screen_pt: Vector2) -> Vector2:
	var from: Vector3 = _camera.project_ray_origin(screen_pt)
	var dir:  Vector3 = _camera.project_ray_normal(screen_pt)
	if is_zero_approx(dir.y):
		return Vector2(from.x, from.z)
	var t: float = (ground_y - from.y) / dir.y
	var hit: Vector3 = from + dir * t
	return Vector2(hit.x, hit.z)

# --- build view rect(s) in minimap pixels --------------------------------
func _update_view_rects() -> void:
	_rects.clear()

	var vp_size: Vector2 = Vector2(_camera.get_viewport().get_visible_rect().size)
	var p0: Vector2 = _intersect_screen_to_ground(Vector2(0.0,       0.0))
	var p1: Vector2 = _intersect_screen_to_ground(Vector2(vp_size.x, 0.0))
	var p2: Vector2 = _intersect_screen_to_ground(Vector2(vp_size.x, vp_size.y))
	var p3: Vector2 = _intersect_screen_to_ground(Vector2(0.0,       vp_size.y))

	var uv0: Vector2 = _world_xz_to_uv(p0)
	var uv1: Vector2 = _world_xz_to_uv(p1)
	var uv2: Vector2 = _world_xz_to_uv(p2)
	var uv3: Vector2 = _world_xz_to_uv(p3)

	var us: Array[float] = [uv0.x, uv1.x, uv2.x, uv3.x]
	var vs: Array[float] = [uv0.y, uv1.y, uv2.y, uv3.y]

	var us_plain: Array[float] = (us.duplicate() as Array[float]); us_plain.sort()
	var span_plain: float = us_plain[3] - us_plain[0]

	var us_shifted: Array[float] = []
	for u in us:
		us_shifted.append(u + 1.0 if u < 0.5 else u)
	var us_shifted_sorted: Array[float] = (us_shifted.duplicate() as Array[float]); us_shifted_sorted.sort()
	var span_shift: float = us_shifted_sorted[3] - us_shifted_sorted[0]

	var use_shift: bool = wrap_horizontally and (span_shift < span_plain)

	var umin: float
	var umax: float
	if use_shift:
		umin = min(us_shifted[0], us_shifted[1], us_shifted[2], us_shifted[3])
		umax = max(us_shifted[0], us_shifted[1], us_shifted[2], us_shifted[3])
	else:
		umin = min(us[0], us[1], us[2], us[3])
		umax = max(us[0], us[1], us[2], us[3])

	var vmin: float = clampf(min(vs[0], vs[1], vs[2], vs[3]), 0.0, 1.0)
	var vmax: float = clampf(max(vs[0], vs[1], vs[2], vs[3]), 0.0, 1.0)

	var s: Vector2 = size

	if not use_shift:
		var r := Rect2(Vector2(umin * s.x, vmin * s.y),
					   Vector2(max(1.0, (umax - umin) * s.x), max(1.0, (vmax - vmin) * s.y)))
		_rects.append(r)
	else:
		var rA := Rect2(Vector2(0.0, vmin * s.y),
						Vector2((umax - 1.0) * s.x, max(1.0, (vmax - vmin) * s.y)))
		var rB := Rect2(Vector2(umin * s.x, vmin * s.y),
						Vector2((1.0 - umin) * s.x, max(1.0, (vmax - vmin) * s.y)))
		_rects.append(rA)
		_rects.append(rB)

# --- input: click + drag to pan ------------------------------------------
func _gui_input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		var mb := event as InputEventMouseButton
		if mb.button_index == MOUSE_BUTTON_LEFT:
			if mb.pressed:
				_dragging = true
				_drag_prefer_x = _camera.global_position.x if _camera != null else 0.0
				print("[Minimap] click @ local=", mb.position)
				if center_on_click:
					_pan_to_local_pos(mb.position)
					accept_event()
			else:
				_dragging = false
				print("[Minimap] release")
	elif event is InputEventMouseMotion and _dragging and drag_to_pan:
		var mm := event as InputEventMouseMotion
		print("[Minimap] drag @ local=", mm.position)
		_pan_to_local_pos(mm.position)
		accept_event()

func _pan_to_local_pos(local_pos: Vector2) -> void:
	var uv: Vector2 = Vector2(
		clampf(local_pos.x / max(1.0, size.x), 0.0, 1.0),
		clampf(local_pos.y / max(1.0, size.y), 0.0, 1.0)
	)

	# 1) Desired ground center from the minimap click
	var desired_center: Vector2 = _uv_to_world_xz(uv, _drag_prefer_x)

	# 2) Current ground center under the screen center
	var current_center: Vector2 = _get_camera_ground_center_xz()

	# 3) Delta needed on ground to bring current -> desired
	var delta: Vector2 = desired_center - current_center

	# Wrap-aware horizontal delta (choose the shortest X translation)
	if wrap_horizontally:
		var period: float = world_size_xz.x
		delta.x -= round(delta.x / period) * period

	# 4) Convert ground delta to a rig XZ target (translate the rig by the same delta)
	var rig: Node3D = _get_rig_node()
	if rig:
		var target_rig_xz := Vector2(rig.global_position.x + delta.x,
									 rig.global_position.z + delta.y)
		_drag_prefer_x = target_rig_xz.x
		_request_pan(target_rig_xz)
	else:
		push_warning("No rig node found to pan.")


func _request_pan(xz: Vector2) -> void:
	# Debug: see what we’re trying to do
	# (You can comment these out once confirmed)
	print("[Minimap] pan request -> XZ: ", xz, 
		  " | receiver=", _pan_receiver, 
		  " | smooth=", smooth_pan)

	if _pan_receiver != null and _pan_receiver.has_method("pan_to_world_xz"):
		_pan_receiver.call("pan_to_world_xz", xz, smooth_pan)
		return

	if fallback_teleport_if_no_receiver:
		# Fallback: move the camera rig (parent of camera) or the camera itself.
		if _camera != null:
			var rig := _camera.get_parent()
			if rig is Node3D:
				var y := (rig as Node3D).global_position.y
				(rig as Node3D).global_position = Vector3(xz.x, y, xz.y)
				print("[Minimap] Fallback: moved camera parent to ", (rig as Node3D).global_position)
				return
			# If your camera has no Node3D parent that moves, move the camera.
			_camera.global_position = Vector3(xz.x, _camera.global_position.y, xz.y)
			print("[Minimap] Fallback: moved camera to ", _camera.global_position)
			return

	push_warning("No pan receiver with 'pan_to_world_xz' and fallback disabled; cannot pan.")

func _input(event: InputEvent) -> void:
	# This runs before GUI dispatch; use it to capture clicks over the minimap
	if event is InputEventMouseButton:
		var mb := event as InputEventMouseButton
		if mb.button_index == MOUSE_BUTTON_LEFT:
			var gp: Vector2 = get_viewport().get_mouse_position()
			if get_global_rect().has_point(gp):
				if mb.pressed:
					_dragging = true
					_drag_prefer_x = _camera.global_position.x if _camera != null else 0.0
					print("[Minimap] _input CLICK @ local=", get_local_mouse_position())
					if center_on_click:
						_pan_to_local_pos(get_local_mouse_position())
				else:
					_dragging = false
					print("[Minimap] _input RELEASE")
				accept_event()  # stop propagation so nothing above eats it
	elif event is InputEventMouseMotion and _dragging and drag_to_pan:
		var gp2: Vector2 = get_viewport().get_mouse_position()
		if get_global_rect().has_point(gp2):
			var local := get_local_mouse_position()
			print("[Minimap] _input DRAG @ local=", local)
			_pan_to_local_pos(local)
			accept_event()

func _get_camera_ground_center_xz() -> Vector2:
	var vp: Vector2 = _camera.get_viewport().get_visible_rect().size
	return _intersect_screen_to_ground(vp * 0.5)

func _get_rig_node() -> Node3D:
	if _pan_receiver is Node3D:
		return _pan_receiver as Node3D
	if _camera and _camera.get_parent() is Node3D:
		return _camera.get_parent() as Node3D
	return null
