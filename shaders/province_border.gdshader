shader_type spatial;
// Province and country borders drawn as ribbons over the terrain (built by src/Map/ProvinceBorders.cs).
// Each ribbon vertex carries: VERTEX = point on the border (y = 0), NORMAL = miter direction,
// UV = (side -1/+1, miter length), UV2 = (province A, province B).
render_mode unshaded, blend_mix, depth_test_disabled, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

// same terrain parameters as shaders/main.gdshader, so borders sit on the relief
uniform sampler2D heightmap : filter_linear_mipmap, repeat_enable;
uniform float sea_level_norm = 0.5019608;
uniform float height_scale = 35.0;
uniform float terrain_base_y = 0.0;
uniform vec2  map_size = vec2(5632.0, 2316.0);
uniform float lift = 0.3;

// owner index per province id (RG8 = low/high byte, 256 ids per row); differing owners = country border
uniform sampler2D owner_map : filter_nearest, repeat_disable;
uniform float selected_province = -1.0;

uniform vec4  province_color : source_color = vec4(0.06, 0.05, 0.04, 0.5);
uniform vec4  country_color  : source_color = vec4(0.16, 0.03, 0.03, 0.9);
uniform vec4  selected_color : source_color = vec4(1.0, 0.86, 0.38, 1.0);
uniform float province_width_px = 1.0;
uniform float country_width_px  = 2.2;
uniform float selected_width_px = 2.8;

// fade out by camera distance (world units), provinces first, countries later
uniform vec2 province_fade = vec2(700.0, 1100.0);
uniform vec2 country_fade  = vec2(1500.0, 2200.0);

varying float v_offset_px;   // signed distance from the centre line, in screen pixels
varying float v_width_px;
varying vec4  v_color;

float owner_of(float id) {
    int i = int(id + 0.5);
    vec2 rg = texelFetch(owner_map, ivec2(i % 256, i / 256), 0).rg;
    return floor(rg.r * 255.0 + 0.5) + 256.0 * floor(rg.g * 255.0 + 0.5);
}

void vertex() {
    vec2 uv = VERTEX.xz / map_size + 0.5;
    float h = textureLod(heightmap, uv, 0.0).r;
    vec3 center = vec3(VERTEX.x, max((h - sea_level_norm) * height_scale, 0.0) + terrain_base_y + lift, VERTEX.z);

    vec3 world = (MODEL_MATRIX * vec4(center, 1.0)).xyz;
    float dist = length(CAMERA_POSITION_WORLD - world);
    float world_per_px = 2.0 * dist / (PROJECTION_MATRIX[1][1] * VIEWPORT_SIZE.y);

    bool selected = selected_province >= 0.0
        && (abs(UV2.x - selected_province) < 0.5 || abs(UV2.y - selected_province) < 0.5);
    bool country = owner_of(UV2.x) != owner_of(UV2.y);

    if (selected) {
        v_width_px = selected_width_px;
        v_color = selected_color;
    } else if (country) {
        v_width_px = country_width_px;
        v_color = country_color;
        v_color.a *= 1.0 - smoothstep(country_fade.x, country_fade.y, dist);
    } else {
        v_width_px = province_width_px;
        v_color = province_color;
        v_color.a *= 1.0 - smoothstep(province_fade.x, province_fade.y, dist);
    }

    // one extra pixel of ribbon on each side for anti-aliasing
    float half_px = 0.5 * v_width_px + 1.0;
    v_offset_px = UV.x * half_px;
    VERTEX = center + NORMAL * (UV.x * UV.y * half_px * world_per_px);
}

void fragment() {
    float coverage = clamp(0.5 * v_width_px + 0.5 - abs(v_offset_px), 0.0, 1.0);
    ALBEDO = v_color.rgb;
    ALPHA = v_color.a * coverage;
}
