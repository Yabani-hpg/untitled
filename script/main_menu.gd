extends Control

@export var first_scene: PackedScene        # Game Setup screen
@export var load_scene: PackedScene         # Load Game screen (fallback if no overlay)
@export var options_scene: PackedScene      # Options screen
@export var pedia_scene: PackedScene        # Encyclopedia screen

@onready var _fade: ColorRect = $Fade
@onready var _btn_new: Button = $MarginContainer/Menu/NewGame
@onready var _btn_load: Button = $MarginContainer/Menu/LoadGame
@onready var _btn_options: Button = $MarginContainer/Menu/Options
@onready var _btn_pedia: Button = $MarginContainer/Menu/Encyclopedia
@onready var _btn_quit: Button = $MarginContainer/Menu/Quit
@onready var _confirm_quit: ConfirmationDialog = get_node_or_null("ConfirmQuit")

func _ready() -> void:
	# Ensure we have a Confirm dialog
	_ensure_confirm_quit()

	# Start covered, then fade in
	_fade.visible = true
	_fade.modulate = Color(0, 0, 0, 1)
	_fade_in()

	await get_tree().process_frame
	_btn_new.grab_focus()

	# Connect buttons
	_btn_new.pressed.connect(_on_new_game)
	_btn_load.pressed.connect(_on_load_game)
	_btn_options.pressed.connect(_on_options)
	_btn_pedia.pressed.connect(_on_pedia)
	_btn_quit.pressed.connect(_on_quit)

	# Tiny hover scale
	var buttons: Array[Button] = [_btn_new, _btn_load, _btn_options, _btn_pedia, _btn_quit]
	for b: Button in buttons:
		b.mouse_entered.connect(_on_btn_hover_enter.bind(b))
		b.mouse_exited.connect(_on_btn_hover_exit.bind(b))

# --- Hover animations ---

func _on_btn_hover_enter(b: Control) -> void:
	b.scale = Vector2.ONE
	b.create_tween().tween_property(b, "scale", Vector2(1.04, 1.04), 0.08).set_trans(Tween.TRANS_SINE)

func _on_btn_hover_exit(b: Control) -> void:
	b.create_tween().tween_property(b, "scale", Vector2.ONE, 0.08).set_trans(Tween.TRANS_SINE)

# --- Button actions ---

func _on_new_game() -> void:
	_transition_to(first_scene)

func _on_load_game() -> void:
	var overlay := get_node_or_null("Overlay/LoadOverlay")
	if overlay:
		overlay.set("game_scene", first_scene)
		overlay.call("open")
	elif load_scene:
		_transition_to(load_scene)
	else:
		push_error("No LoadOverlay at Overlay/LoadOverlay and no load_scene assigned.")

func _on_options() -> void:
	_transition_to(options_scene)

func _on_pedia() -> void:
	_transition_to(pedia_scene)

func _on_quit() -> void:
	_confirm_quit.popup_centered()

# --- Scene transition helpers (with fade) ---

func _fade_in() -> void:
	_fade.visible = true
	_fade.modulate.a = 1.0
	_fade.create_tween().tween_property(_fade, "modulate:a", 0.0, 0.35)

func _fade_out_and_then(callback: Callable) -> void:
	_fade.visible = true
	var t := _fade.create_tween()
	t.tween_property(_fade, "modulate:a", 1.0, 0.25)
	t.finished.connect(callback)

func _transition_to(scene: PackedScene) -> void:
	if scene == null:
		push_warning("No scene assigned for this button.")
		return
	_fade_out_and_then(func ():
		get_tree().change_scene_to_packed(scene)
	)

# --- Quit confirmation setup ---

func _ensure_confirm_quit() -> void:
	if _confirm_quit == null:
		_confirm_quit = ConfirmationDialog.new()
		_confirm_quit.name = "ConfirmQuit"
		add_child(_confirm_quit)

	_confirm_quit.title = "Quit"
	_confirm_quit.dialog_text = "Are you sure you want to quit?"
	_confirm_quit.ok_button_text = "Yes"

	var cancel_btn: Button = _confirm_quit.get_cancel_button()
	if cancel_btn:
		cancel_btn.text = "No"
		cancel_btn.visible = true

	if not _confirm_quit.confirmed.is_connected(_on_quit_confirmed):
		_confirm_quit.confirmed.connect(_on_quit_confirmed)

func _on_quit_confirmed() -> void:
	_fade_out_and_then(func ():
		get_tree().quit()
	)
