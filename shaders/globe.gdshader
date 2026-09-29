shader_type spatial;
// Globe view (camera zoomed far out). Uses the flat map's textures and sea colouring, placed with the
// map's real latitude range; scenes/camera_rig.gd rotates the sphere so the camera's map point is on top.
// Unshaded with a soft light from the viewer, so there is never a night side.
render_mode unshaded, cull_back;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;   // map/landmap.png
uniform sampler2D heightmap : filter_linear_mipmap, repeat_enable;                   // R height, G coast field
uniform sampler2D water_gradient : source_color, filter_linear, repeat_disable;
uniform vec3  lake_color : source_color = vec3(0.13, 0.36, 0.46);
uniform float sea_level_norm = 0.5019608;
uniform float map_top_lat = 86.6;      // latitude of the texture's top and bottom rows
uniform float map_bottom_lat = -61.6;

uniform vec3  polar_ice : source_color = vec3(0.82, 0.87, 0.92);
uniform float antarctic_lat = -66.0;   // south of the map: open ocean, then Antarctic ice
uniform float brightness = 1.15;
uniform float shading = 0.35;          // 0 = flat colour, 1 = strong light from the upper left
uniform vec3  atmosphere_color : source_color = vec3(0.45, 0.68, 1.0);
uniform float atmosphere_strength = 0.55;

varying vec3 v_dir;

void vertex() {
    v_dir = VERTEX;
}

void fragment() {
    vec3 n = normalize(v_dir);
    float lat = degrees(asin(clamp(n.y, -1.0, 1.0)));
    float lon = degrees(atan(n.x, n.z));
    vec2 uv = vec2(lon / 360.0 + 0.5, (map_top_lat - lat) / (map_top_lat - map_bottom_lat));
    // atan jumps at the date line; sample the base level there so mipmapping doesn't draw a seam
    bool seam = fwidth(uv.x) > 0.25;

    vec3 deep = texture(water_gradient, vec2(0.05, 0.5)).rgb;
    vec3 col;
    if (uv.y < 0.0) {
        col = polar_ice;
    } else if (uv.y > 1.0) {
        col = mix(deep, polar_ice, smoothstep(antarctic_lat + 0.5, antarctic_lat - 0.5, lat));
    } else {
        vec3 hm = seam ? textureLod(heightmap, uv, 0.0).rgb : texture(heightmap, uv).rgb;
        vec3 land = seam ? textureLod(albedo_tex, uv, 0.0).rgb : texture(albedo_tex, uv).rgb;
        vec3 sea = hm.r > sea_level_norm + 0.5 / 255.0
            ? lake_color
            : texture(water_gradient, vec2(clamp(hm.r / sea_level_norm, 0.0, 1.0), 0.5)).rgb;
        float aa = max(fwidth(hm.g), 0.01);
        col = mix(sea, land, smoothstep(0.5 - aa, 0.5 + aa, hm.g));
    }

    // soft key light from the viewer's upper left, and an atmosphere rim toward the limb
    float key = clamp(dot(NORMAL, normalize(vec3(-0.35, 0.45, 0.82))), 0.0, 1.0);
    col *= brightness * mix(1.0, 0.55 + 0.6 * key, shading);
    float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.5);
    ALBEDO = mix(col, atmosphere_color, rim * atmosphere_strength);
}
