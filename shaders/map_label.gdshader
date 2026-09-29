shader_type spatial;
// Province and country names lying on the map (built by src/Map/MapLabels.cs). Glyphs come from a
// multichannel signed distance field font atlas, so letters stay sharp at any zoom.
// UV = atlas uv; UV2 = (letter size in world units, kind: 0 province, 1 lake, 2 country).
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform sampler2D atlas : filter_linear, repeat_disable;
uniform vec2  atlas_size = vec2(1024.0);
uniform float msdf_pixel_range = 16.0;

// same terrain parameters as shaders/main.gdshader, so labels sit on the relief
uniform sampler2D heightmap : filter_linear_mipmap, repeat_enable;
uniform float sea_level_norm = 0.5019608;
uniform float height_scale = 35.0;
uniform float terrain_base_y = 0.0;
uniform vec2  map_size = vec2(5632.0, 2316.0);
uniform float lift = 0.6;

// light lettering with a dark shadow reads best on the photographic terrain
uniform vec4 province_color : source_color = vec4(0.93, 0.89, 0.79, 0.95);
uniform vec4 lake_color     : source_color = vec4(0.72, 0.86, 0.96, 0.95);
uniform vec4 country_color  : source_color = vec4(0.98, 0.90, 0.70, 0.9);
uniform vec4 halo_color     : source_color = vec4(0.03, 0.02, 0.01, 0.55);
uniform float halo_width = 0.1;           // in distance-field units (0.5 = the full pixel range)

// province names show once their letters are this many screen pixels tall, and fade out when zoomed away;
// country names fade in when zoomed out, and out again if their letters get huge on screen
uniform vec2 readable_px     = vec2(7.0, 11.0);
uniform vec2 province_too_big = vec2(60.0, 90.0);
uniform vec2 province_fade   = vec2(650.0, 950.0);
uniform vec2 country_fade_in = vec2(700.0, 1000.0);
uniform vec2 country_too_big = vec2(110.0, 170.0);

varying vec4 v_color;
varying float v_fade;   // visibility at this zoom

float median3(vec3 v) {
    return max(min(v.r, v.g), min(max(v.r, v.g), v.b));
}

void vertex() {
    vec2 uv_map = VERTEX.xz / map_size + 0.5;
    float h = textureLod(heightmap, uv_map, 0.0).r;
    VERTEX.y = max((h - sea_level_norm) * height_scale, 0.0) + terrain_base_y + lift;

    vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float dist = length(CAMERA_POSITION_WORLD - world);
    // abs: the Compatibility renderer flips Y, which makes this projection term negative
    float world_per_px = 2.0 * dist / (abs(PROJECTION_MATRIX[1][1]) * VIEWPORT_SIZE.y);
    float letter_px = UV2.x / world_per_px;

    float kind = UV2.y;
    if (kind > 1.5) {
        v_color = country_color;
        v_fade = smoothstep(country_fade_in.x, country_fade_in.y, dist)
               * (1.0 - smoothstep(country_too_big.x, country_too_big.y, letter_px))
               * smoothstep(readable_px.x, readable_px.y, letter_px);
    } else {
        v_color = kind > 0.5 ? lake_color : province_color;
        v_fade = smoothstep(readable_px.x, readable_px.y, letter_px)
               * (1.0 - smoothstep(province_too_big.x, province_too_big.y, letter_px))
               * (1.0 - smoothstep(province_fade.x, province_fade.y, dist));
    }
}

void fragment() {
    float sd = median3(texture(atlas, UV).rgb) - 0.5;
    // distance-field units -> screen pixels at this magnification
    vec2 unit_range = vec2(msdf_pixel_range) / atlas_size;
    vec2 screen_tex = vec2(1.0) / max(fwidth(UV), vec2(1e-6));
    float px = max(0.5 * dot(unit_range, screen_tex), 1.0);
    float fill = clamp(sd * px + 0.5, 0.0, 1.0);
    float halo = clamp((sd + halo_width) * px + 0.5, 0.0, 1.0);

    ALBEDO = mix(halo_color.rgb, v_color.rgb, fill);
    ALPHA = (v_color.a * fill + halo_color.a * halo * (1.0 - fill)) * v_fade;
}
