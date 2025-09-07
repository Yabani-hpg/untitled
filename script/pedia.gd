extends Control

# Where article JSON files live (one file per entry)
const PEDIA_DIR := "res://common/pedia/"   # e.g., res://pedia/entries/rome.json

# Scene to return to when pressing Back
@export var back_to_menu: PackedScene
@export_file("*.tscn") var back_to_menu_path: String = "res://scenes/main_menu.tscn"  # fallback


# ---- Node refs ----
@onready var _back: Button            = $MarginContainer/VBoxContainer/TopBar/Back
@onready var _search: LineEdit        = $MarginContainer/VBoxContainer/TopBar/Search
@onready var _list: ItemList          = $MarginContainer/VBoxContainer/Body/ArticleList
@onready var _title: Label            = $MarginContainer/VBoxContainer/Body/ArticleScroll/Article/Title
@onready var _subtitle: Label         = $MarginContainer/VBoxContainer/Body/ArticleScroll/Article/Subtitle
@onready var _content: RichTextLabel  = $MarginContainer/VBoxContainer/Body/ArticleScroll/Article/HBoxContainer/Content
@onready var _infogrid: GridContainer = $MarginContainer/VBoxContainer/Body/ArticleScroll/Article/HBoxContainer/InfoBox/InfoGrid
@onready var _seealso: RichTextLabel  = $MarginContainer/VBoxContainer/Body/ArticleScroll/Article/HBoxContainer/SeeAlso

# ---- Data ----
var _entries: Array[Dictionary] = []          # all loaded entries
var _filtered_indices: PackedInt32Array = []  # list index -> _entries index

func _ready() -> void:
	_content.bbcode_enabled = true
	_seealso.bbcode_enabled = true
	_wire()
	_load_entries()
	_fill_list()

func _wire() -> void:
	_back.pressed.connect(_on_back)
	_list.item_selected.connect(_on_select)
	_list.item_activated.connect(func(i: int): _on_select(i)) # Enter/double-click
	_search.text_changed.connect(_on_search)

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		_on_back()
		get_viewport().set_input_as_handled()

# ---- Back ----
func _on_back() -> void:
	# Try the PackedScene first (if set on this instance)
	if back_to_menu != null:
		var err_packed := get_tree().change_scene_to_packed(back_to_menu)
		if err_packed != OK:
			push_error("PediaPage: failed to change to packed scene: %d" % err_packed)
		return

	# Fallback to path
	if back_to_menu_path != "":
		var err_file := get_tree().change_scene_to_file(back_to_menu_path)
		if err_file != OK:
			push_error("PediaPage: failed to change to file %s (err %d)" % [back_to_menu_path, err_file])
		return

	push_error("PediaPage: Back target not set. Assign 'back_to_menu' or 'back_to_menu_path'.")

# ---- Load entries from disk ----
func _load_entries() -> void:
	_entries.clear()
	var d: DirAccess = DirAccess.open(PEDIA_DIR)
	if d == null:
		push_warning("Pedia dir not found: " + PEDIA_DIR)
		return

	d.list_dir_begin()
	while true:
		var f: String = d.get_next()
		if f == "":
			break
		if d.current_is_dir():
			continue
		if not f.to_lower().ends_with(".json"):
			continue

		var path: String = PEDIA_DIR + "/" + f
		var txt: String = FileAccess.get_file_as_string(path).strip_edges()
		if txt.is_empty():
			continue

		var json := JSON.new()
		var err: int = json.parse(txt)
		if err == OK and json.data is Dictionary:
			var e: Dictionary = json.data
			if not e.has("id"):
				e["id"] = f.get_basename()
			if not e.has("title"):
				e["title"] = e["id"]
			_entries.append(e)
		# bad JSON silently ignored
	d.list_dir_end()

	# Sort A→Z by title
	_entries.sort_custom(_sort_by_title)

func _sort_by_title(a: Dictionary, b: Dictionary) -> bool:
	var at: String = str(a.get("title", ""))
	var bt: String = str(b.get("title", ""))
	return at.naturalnocasecmp_to(bt) < 0

# ---- List fill & filtering ----
func _fill_list() -> void:
	_list.clear()
	_filtered_indices.clear()
	for i in range(_entries.size()):
		var e := _entries[i]
		_list.add_item(str(e.get("title", "Untitled")))
		_filtered_indices.append(i)
	if _entries.size() > 0:
		_list.select(0)
		_show_entry_index(_filtered_indices[0])
	else:
		_clear_article()

func _on_search(q: String) -> void:
	q = q.strip_edges().to_lower()
	_list.clear()
	_filtered_indices.clear()
	for i in range(_entries.size()):
		var e := _entries[i]
		var title: String = str(e.get("title",""))
		var subtitle: String = str(e.get("subtitle",""))
		if q.is_empty() or title.to_lower().find(q) != -1 or subtitle.to_lower().find(q) != -1:
			_list.add_item(title)
			_filtered_indices.append(i)
	if _list.item_count > 0:
		_list.select(0)
		_show_entry_index(_filtered_indices[0])
	else:
		_clear_article()

# ---- Render article ----
func _on_select(list_index: int) -> void:
	if list_index < 0 or list_index >= _filtered_indices.size():
		return
	var idx: int = _filtered_indices[list_index]
	_show_entry_index(idx)

func _show_entry_index(i: int) -> void:
	if i < 0 or i >= _entries.size():
		return
	var e := _entries[i]

	_title.text = str(e.get("title", "Untitled"))
	_subtitle.text = str(e.get("subtitle", ""))
	_subtitle.visible = not _subtitle.text.is_empty()

	_content.clear()
	_content.append_bbcode(str(e.get("body_bbcode", "")))

	_fill_infobox(e.get("infobox", {}))

	var see: Array = e.get("see_also", [])
	if see.size() > 0:
		var bb := "[b]See also[/b]\n"
		for s in see:
			bb += "• " + str(s) + "\n"
		_seealso.visible = true
		_seealso.clear()
		_seealso.append_bbcode(bb)
	else:
		_seealso.visible = false

func _fill_infobox(box: Variant) -> void:
	for c in _infogrid.get_children():
		c.queue_free()
	if typeof(box) != TYPE_DICTIONARY:
		return
	var d: Dictionary = box
	for k in d.keys():
		var key_lbl := Label.new()
		key_lbl.text = str(k)
		key_lbl.add_theme_color_override("font_color", Color(0.85, 0.78, 0.62))
		key_lbl.add_theme_font_size_override("font_size", 16)

		var val_lbl := RichTextLabel.new()
		val_lbl.bbcode_enabled = true
		val_lbl.fit_content = true
		val_lbl.scroll_active = false
		val_lbl.append_bbcode(str(d[k]))

		_infogrid.add_child(key_lbl)
		_infogrid.add_child(val_lbl)

func _clear_article() -> void:
	_title.text = "(No selection)"
	_subtitle.text = ""
	_content.clear()
	for c in _infogrid.get_children():
		c.queue_free()
	_seealso.visible = false
