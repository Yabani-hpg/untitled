extends Node3D
@export var speed_deg_per_sec := 3.0
func _process(delta):
	rotate_y(deg_to_rad(speed_deg_per_sec) * delta)
