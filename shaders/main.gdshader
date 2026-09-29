shader_type spatial;
render_mode cull_back, depth_draw_opaque;

// ===== Height / base UVs =====
// R = height (sea level = sea_level_norm), G = coast field, B = river field; see tools/bake_map_fields.py
uniform sampler2D heightmap : filter_linear_mipmap, repeat_enable;
uniform float     sea_level_norm : hint_range(0.0, 1.0, 0.001) = 0.5019608;
uniform float     height_scale   = 80.0;
uniform float     terrain_base_y = 0.0;
uniform vec2      uv_tiling      = vec2(1.0, 1.0);
uniform vec2      uv_offset      = vec2(0.0, 0.0);

// ===== Land base =====
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec2      albedo_tiling = vec2(4.0, 4.0);
// map/normalmap.png: R = -dh/dx, G = -dh/dz (image-down / DirectX convention), decoded to a world normal below
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float     terrain_normal_strength = 1.0;

// ===== Detail (micro) =====
uniform sampler2D detail_tex      : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D detail_norm_tex : hint_normal,  filter_linear_mipmap, repeat_enable;
uniform vec2  detail_tiling          = vec2(24.0, 24.0);
uniform float detail_color_strength  = 0.25;
uniform float detail_normal_strength = 0.6;
uniform float detail_fwidth_min = 0.002;
uniform float detail_fwidth_max = 0.02;
uniform float albedo_lod_scale  = 0.9;
uniform float normal_lod_scale  = 0.9;

// ===== Shoreline + foam =====
uniform float coast_softness_texels = 0.35; // minimum coastline blend, in heightmap texels
uniform float shoreline_width_m = 8.0;
uniform float foam_width_texels = 1.5;   // foam band width from the coastline
uniform vec3  foam_color        = vec3(0.95, 0.98, 1.0);
uniform float foam_strength     = 0.7;
uniform float foam_speed        = 0.15;
uniform vec2  foam_dir          = vec2(1.0, 0.3);
uniform float foam_noise_scale  = 8.0;
uniform float foam_threshold    = 0.45;
uniform float foam_aa_mult      = 1.0;   // derivative AA

// ===== Sea look =====
// Sea color by depth: left = deepest (height 0), right = sea level
uniform sampler2D water_gradient : source_color, filter_linear, repeat_disable;
uniform vec3 lake_color : source_color = vec3(0.13, 0.36, 0.46);
uniform vec3  shallow_tint     = vec3(0.08, 0.20, 0.25);
uniform float shallow_strength = 0.35;
uniform float sea_roughness    = 0.05;
uniform float land_roughness   = 0.9;

// ===== Animated sea normals =====
uniform sampler2D water_norm_tex : hint_normal, filter_linear_mipmap, repeat_enable;
uniform vec2  water_norm_tiling = vec2(8.0, 8.0);
uniform vec2  water_scroll_1    = vec2( 0.03,  0.00);
uniform vec2  water_scroll_2    = vec2(-0.02,  0.01);
uniform float water_norm_strength = 0.7;
uniform float sea_normal_strength = 0.35; // lighting relief from the animated normals

// ===== Vertex waves =====
uniform float wave_amp_m   = 0.35;
uniform float wave_len_m   = 6.0;
uniform float wave_speed   = 1.4;
uniform vec2  wave_dir_1   = vec2( 1.0,  0.2);
uniform vec2  wave_dir_2   = vec2(-0.6,  1.0);
uniform float wave_shore_falloff_m = 10.0;

// ===== Province selection (driven by src/Map/ProvinceHighlight.cs) =====
uniform sampler2D province_map : filter_nearest, repeat_enable;
uniform vec3  selected_province_color = vec3(-1.0); // map color of the selected province; negative = none
uniform vec3  selection_tint_color      = vec3(1.0, 0.85, 0.35);
uniform float selection_tint_strength   : hint_range(0.0, 1.0) = 0.22;
// Province and country borders and the selection outline are drawn by shaders/province_border.gdshader.

bool is_selected_province(vec2 p) {
    vec3 diff = abs(texture(province_map, p).rgb - selected_province_color);
    return all(lessThan(diff, vec3(0.5 / 255.0)));
}

// ===== Map modes (driven by src/Map/MapModes.cs) =====
// province id under each pixel (low byte in R, high byte in G), and a colour per province id laid over the land
uniform sampler2D province_ids : filter_nearest, repeat_enable;
uniform sampler2D province_palette : source_color, filter_nearest, repeat_disable;
uniform bool map_mode_enabled = false;

vec4 province_palette_at(vec2 p) {
    vec2 rg = floor(texture(province_ids, p).rg * 255.0 + 0.5);
    int id = int(rg.r + rg.g * 256.0);
    return texelFetch(province_palette, ivec2(id % 256, id / 256), 0);
}

// ===== Rivers (mask & materials) =====
// IMPORTANT: import rivers.png with Filter=Nearest and Mipmaps=Off.
uniform sampler2D river_mask_tex : source_color, filter_nearest, repeat_disable;
uniform vec2  river_mask_tiling  = vec2(1.0, 1.0);
uniform vec2  river_mask_offset  = vec2(0.0, 0.0);
uniform float river_softness     = 1.75; // edge feather (raise for smoother)

// Materials
uniform sampler2D river_bottom_diffuse  : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D river_surface_diffuse : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D river_surface_normal  : hint_normal,  filter_linear_mipmap, repeat_enable;

uniform vec2  river_bottom_tiling  = vec2(6.0, 6.0);   // base tiling
uniform vec2  river_surface_tiling = vec2(12.0, 12.0); // base tiling
uniform float river_roughness      = 0.07;

uniform float river_flow_speed       = 0.18;
uniform vec2  river_flow_dir_global  = vec2(1.0, 0.0);
uniform float river_flow_from_height = 1.0;     // 0..1 blend with height gradient
uniform float river_flow_eps_uv      = 0.0015;

uniform float river_depth_power = 1.35;        // banks vs center
uniform float river_geo_depth_m = 0.5;         // 0 = off trench carve

// Global river opacity
uniform float river_opacity = 1.0;             // 0..1

// Palette classification tolerance (0..1). Raise if PNG gets filtered.
uniform float river_color_tol = 0.08;          // ≈ ~20/255 in linear-ish

// Width class multipliers (scale your base tilings)
uniform float river_surf_mult_narrowest  = 1.5;
uniform float river_surf_mult_narrow     = 1.25;
uniform float river_surf_mult_wide       = 1.00;
uniform float river_surf_mult_widest     = 0.70;

uniform float river_bottom_mult_narrowest = 1.3;
uniform float river_bottom_mult_narrow    = 1.15;
uniform float river_bottom_mult_wide      = 1.00;
uniform float river_bottom_mult_widest    = 0.80;

// Node flow multipliers (optional)
uniform float river_flow_mult_source  = 0.85;  // green
uniform float river_flow_mult_injoin  = 1.15;  // red
uniform float river_flow_mult_outflow = 0.95;  // yellow

// Widens (+) or narrows (-) every river, in heightmap texels; per-river width is baked by class
uniform float river_width_bias_texels = 0.0;

// Distance range baked into the coast/river fields; must match RANGE in tools/bake_map_fields.py
const float FIELD_RANGE_TEXELS = 8.0;
// Range of the river field (0.5 = river edge); must match RIVER_RANGE in tools/bake_map_fields.py
const float RIVER_RANGE_TEXELS = 4.0;

float river_edge_threshold() {
    return 0.5 - river_width_bias_texels / (2.0 * RIVER_RANGE_TEXELS);
}

// ---- Palette constants (0..1) ----
const vec3 COL_SRC = vec3(0.0, 1.0, 0.0);                 // green (source)
const vec3 COL_IN  = vec3(1.0, 0.0, 0.0);                 // red (flow-in)
const vec3 COL_OUT = vec3(1.0, 0.9882353, 0.0);           // yellow (flow-out)
const vec3 B0      = vec3(0.0, 0.8823529, 1.0);           // 0,225,255 narrowest
const vec3 B1      = vec3(0.0, 0.7843137, 1.0);           // 0,200,255 narrow
const vec3 B2      = vec3(0.0, 0.3921569, 1.0);           // 0,100,255 wide
const vec3 B3      = vec3(0.0, 0.0,       0.7843137);     // 0,0,200   widest

// ---------- HELPERS ----------
float color_match(vec3 c, vec3 t, float tol, float aa) {
    float d = max(max(abs(c.r - t.r), abs(c.g - t.g)), abs(c.b - t.b));
    return 1.0 - smoothstep(tol - aa, tol + aa, d); // 1 when close to target
}

// ---------------- VERTEX ----------------
void vertex() {
    vec2 h_uv = UV * uv_tiling + uv_offset;
    vec3 hm   = texture(heightmap, h_uv).rgb;
    float h   = hm.r;

    float base_y = (h - sea_level_norm) * height_scale + terrain_base_y;
    float d_m    = (h - sea_level_norm) * height_scale;

    // Object-space waves (literal 2π)
    vec2 wxz = VERTEX.xz;
    float k  = 6.2831853 / max(wave_len_m, 0.01);
    float ph = wave_speed * k;

    vec2 d1 = normalize(wave_dir_1);
    vec2 d2 = normalize(wave_dir_2);
    float w1 = sin(dot(d1, wxz) * k + TIME * ph);
    float w2 = sin(dot(d2, wxz) * k + TIME * (ph * 0.83));

    float shore_fade = smoothstep(-wave_shore_falloff_m, 0.0, d_m);
    float wave_y     = wave_amp_m * 0.5 * (w1 + w2) * shore_fade;

    // water surface sits flat at sea level; depth only drives color
    float is_water = step(d_m, 0.0);
    VERTEX.y = mix(base_y, terrain_base_y + wave_y, is_water);

    // Optional trench carve along rivers
    if (river_geo_depth_m > 0.0) {
        float thr = river_edge_threshold();
        float is_river_px = smoothstep(thr, thr + 0.5 / (2.0 * RIVER_RANGE_TEXELS), hm.b);
        float is_land_v = step(0.0, d_m);
        VERTEX.y -= river_geo_depth_m * is_river_px * is_land_v;
    }
}

// ---------------- FRAGMENT ----------------
void fragment() {
    vec2  uv = UV;
    vec3  hm = texture(heightmap, uv).rgb;
    float h  = hm.r;
    float d_m = (h - sea_level_norm) * height_scale;
    float w = max(shoreline_width_m, 0.0001);

    // Land/sea from the baked coast field: its 0.5 contour is the coastline at any zoom
    float coast_aa = max(fwidth(hm.g) * 0.75, coast_softness_texels / (2.0 * FIELD_RANGE_TEXELS));
    float land_mix = smoothstep(0.5 - coast_aa, 0.5 + coast_aa, hm.g); // 0=sea, 1=land
    float sea_mix  = 1.0 - land_mix;

    // Land base (with micro detail color)
    vec2 uv_base = UV * albedo_tiling;
    // (no textureGrad in Compat) → use texture()
    vec3 base_col = texture(albedo_tex, uv_base).rgb;

    vec2  uv_det  = UV * detail_tiling;
    vec3  det_col = texture(detail_tex, uv_det).rgb;
    float du      = length(fwidth(uv_det));
    float dw      = smoothstep(detail_fwidth_min, detail_fwidth_max, du);
    vec3  det_mul = mix(vec3(1.0), det_col * 2.0, detail_color_strength * dw);
    vec3  land_col = base_col * det_mul;

    // ===== Rivers (baked distance field, one fetch) =====
    float rraw = hm.b;                                   // > 0.5 inside a river, falls off over RIVER_RANGE_TEXELS
    float river_thr = river_edge_threshold();
    float r_aa = max(fwidth(rraw) * river_softness, 1e-4);
    float river_cov = smoothstep(river_thr - r_aa, river_thr + r_aa, rraw) * land_mix;

    if (river_cov > 0.0) {
        // classify width & nodes at the center sample
        vec2 ruv_c = (UV * river_mask_tiling) + river_mask_offset;
        vec3 c = texture(river_mask_tex, ruv_c).rgb;

        float aa_col2 = max(0.25 * river_softness * length(fwidth(ruv_c)), 1e-5);

        float m_src = color_match(c, COL_SRC, river_color_tol, aa_col2);
        float m_in  = color_match(c, COL_IN,  river_color_tol, aa_col2);
        float m_out = color_match(c, COL_OUT, river_color_tol, aa_col2);

        float m_b0  = color_match(c, B0, river_color_tol, aa_col2);
        float m_b1  = color_match(c, B1, river_color_tol, aa_col2);
        float m_b2  = color_match(c, B2, river_color_tol, aa_col2);
        float m_b3  = color_match(c, B3, river_color_tol, aa_col2);

        // texels beside the painted line carry no class; they keep the base tiling
        float wsum = m_b0 + m_b1 + m_b2 + m_b3;
        float surf_mult = wsum > 0.001 ? (
            m_b0 * river_surf_mult_narrowest +
            m_b1 * river_surf_mult_narrow    +
            m_b2 * river_surf_mult_wide      +
            m_b3 * river_surf_mult_widest
        ) / wsum : 1.0;

        float bottom_mult = wsum > 0.001 ? (
            m_b0 * river_bottom_mult_narrowest +
            m_b1 * river_bottom_mult_narrow    +
            m_b2 * river_bottom_mult_wide      +
            m_b3 * river_bottom_mult_widest
        ) / wsum : 1.0;

        vec2 t_surf   = river_surface_tiling * surf_mult;
        vec2 t_bottom = river_bottom_tiling  * bottom_mult;

        // flow direction (height gradient → downhill) blended with global + node nudges
        float flow_mult = 1.0
            + m_src * (river_flow_mult_source  - 1.0)
            + m_in  * (river_flow_mult_injoin  - 1.0)
            + m_out * (river_flow_mult_outflow - 1.0);

        vec2 flow_dir = normalize(river_flow_dir_global);
        if (river_flow_from_height > 0.0) {
            float hx1 = texture(heightmap, UV + vec2(river_flow_eps_uv, 0.0)).r;
            float hx0 = texture(heightmap, UV - vec2(river_flow_eps_uv, 0.0)).r;
            float hy1 = texture(heightmap, UV + vec2(0.0, river_flow_eps_uv)).r;
            float hy0 = texture(heightmap, UV - vec2(0.0, river_flow_eps_uv)).r;
            vec2 grad   = vec2(hx1 - hx0, hy1 - hy0);
            vec2 flow_h = normalize(-grad + 1e-6);
            flow_dir    = normalize(mix(flow_dir, flow_h, clamp(river_flow_from_height, 0.0, 1.0)));
        }

        // bottom
        vec3 r_bottom = texture(river_bottom_diffuse, UV * t_bottom).rgb;

        // animated surface
        vec2 rsuv = UV * t_surf + flow_dir * (TIME * river_flow_speed * flow_mult);
        vec3 r_surf_col = texture(river_surface_diffuse, rsuv).rgb;

        vec3 rsn = texture(river_surface_normal,  rsuv).rgb * 2.0 - 1.0;
        float r_fake = clamp(0.5 + 0.5 * rsn.z, 0.0, 1.0);
        r_surf_col  *= mix(0.9, 1.1, r_fake);

        // banks vs center
        float center    = clamp((rraw - river_thr) * (2.0 * RIVER_RANGE_TEXELS) / 0.6, 0.0, 1.0); // full depth 0.6 texel in
        float depth_mix = pow(center, river_depth_power);
        vec3  river_col = mix(r_bottom, r_surf_col, depth_mix);

        // apply with global opacity
        float cov_final = clamp(river_cov * river_opacity, 0.0, 1.0);
        land_col = mix(land_col, river_col, cov_final);
        river_cov = cov_final; // keep for roughness
    }

    // ===== Sea base & shoreline =====
    // lakes sit above sea level and get their own color; seas are shaded by depth
    vec3 sea_col = h > sea_level_norm + 0.5 / 255.0
        ? lake_color
        : texture(water_gradient, vec2(clamp(h / sea_level_norm, 0.0, 1.0), 0.5)).rgb;

    // shallow tint (underwater only)
    float shallow = (1.0 - step(0.0, d_m)) * (1.0 - smoothstep(-w, 0.0, d_m));
    vec3  sea_tinted = mix(sea_col, sea_col + shallow_tint, shallow_strength * shallow);

    // animated water normals (screen-space hack)
    vec2 wuv1 = UV * water_norm_tiling + water_scroll_1 * TIME;
    vec2 wuv2 = UV * water_norm_tiling * 0.85 + water_scroll_2 * TIME;
    vec3 wn1 = texture(water_norm_tex, wuv1).rgb * 2.0 - 1.0;
    vec3 wn2 = texture(water_norm_tex, wuv2).rgb * 2.0 - 1.0;
    vec3 wnorm_ts = normalize(wn1 + wn2);
    float fake_light  = clamp(0.5 + 0.5 * wnorm_ts.z, 0.0, 1.0);
    float water_shade = mix(0.9, 1.1, fake_light);

    // mask animated shading to water
    float sea_only   = 1.0 - step(0.0, d_m);
    float water_mask = sea_mix * sea_only;
    float water_shade_mod = mix(1.0, water_shade, water_norm_strength * water_mask);
    vec3  sea_animated = sea_tinted * water_shade_mod;

    // foam: a band along the coastline (distance from the baked coast field), broken up by noise
    float coast_dist = (hm.g - 0.5) * 2.0 * FIELD_RANGE_TEXELS;   // texels; < 0 at sea
    float fw = max(foam_width_texels, 0.0001);
    float faa = max(fwidth(coast_dist) * foam_aa_mult, 1e-4);
    float foam_band = 1.0 - smoothstep(fw - faa, fw + faa, -coast_dist);
    vec2  foam_uv   = uv * foam_noise_scale + foam_dir * TIME * foam_speed;
    float foam_noise = texture(detail_tex, foam_uv).r;
    float foam_gate  = smoothstep(foam_threshold, 1.0, foam_noise);
    float foam_mask  = foam_band * foam_gate * (1.0 - land_mix);

    // compose color
    vec3 shore_mix = mix(sea_animated, land_col, land_mix);
    ALBEDO = mix(shore_mix, foam_color, foam_strength * foam_mask);

    // map mode colours: blend the 4 nearest map texels' colours so province edges stay smooth up close
    if (map_mode_enabled) {
        vec2 size = vec2(textureSize(province_ids, 0));
        vec2 st = uv * size - 0.5;
        vec2 f = fract(st);
        vec2 c = (floor(st) + 0.5) / size;
        vec2 t = 1.0 / size;
        vec4 col = mix(mix(province_palette_at(c), province_palette_at(c + vec2(t.x, 0.0)), f.x),
                       mix(province_palette_at(c + vec2(0.0, t.y)), province_palette_at(c + t), f.x), f.y);
        float a = col.a * land_mix * (1.0 - 0.6 * river_cov);
        ALBEDO = mix(ALBEDO, col.rgb / max(col.a, 1e-3), a);
    }

    // selected province: soft tint with a smooth edge (bilinear blend of the 4 nearest map texels);
    // the crisp outline comes from the border ribbons
    if (selected_province_color.r >= 0.0) {
        vec2 size = vec2(textureSize(province_map, 0));
        vec2 st = uv * size - 0.5;
        vec2 f = fract(st);
        vec2 c = (floor(st) + 0.5) / size;
        vec2 t = 1.0 / size;
        float sel = mix(mix(float(is_selected_province(c)), float(is_selected_province(c + vec2(t.x, 0.0))), f.x),
                        mix(float(is_selected_province(c + vec2(0.0, t.y))), float(is_selected_province(c + t)), f.x), f.y);
        float aa = max(fwidth(sel), 1e-3);
        sel = smoothstep(0.5 - aa, 0.5 + aa, sel);
        ALBEDO = mix(ALBEDO, selection_tint_color, sel * selection_tint_strength);
    }

    // final roughness
    float rgh = mix(sea_roughness, land_roughness, land_mix);
          rgh = mix(rgh, 0.6,             foam_mask);  // foam rougher
          rgh = mix(rgh, river_roughness, river_cov);  // rivers smoother on land
    ROUGHNESS = clamp(rgh, 0.0, 1.0);

    // normals: terrain relief on land, animated ripples at sea, calm rivers
    vec2 slope_land = (texture(normal_tex, uv).rg * 2.0 - 1.0) * terrain_normal_strength;
    slope_land += (texture(detail_norm_tex, uv_det).rg * 2.0 - 1.0) * detail_normal_strength * (1.0 - dw);
    vec2 slope_sea = (wn1.xy + wn2.xy) * sea_normal_strength;
    vec2 slope = mix(slope_sea, slope_land, land_mix) * (1.0 - 0.8 * river_cov);
    vec3 n_obj = normalize(vec3(slope.x, 1.0, slope.y));
    NORMAL = normalize((VIEW_MATRIX * (MODEL_MATRIX * vec4(n_obj, 0.0))).xyz);
}
