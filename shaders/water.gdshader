shader_type spatial;
render_mode blend_mix, depth_prepass_alpha, cull_back, shadows_disabled, fog_disabled;

// Optional (for refraction). Set refraction_strength=0 to disable.
uniform sampler2D SCREEN_TEXTURE : hint_screen_texture, filter_linear_mipmap;

// --------------------- Textures ---------------------
uniform sampler2D ripples_a : hint_normal;
uniform sampler2D ripples_b : hint_normal;
uniform sampler2D foam_tex;
uniform sampler2D water_mask;     // optional: white=water, black=land
uniform sampler2D heightmap_tex;  // same heightmap as terrain (linear, Repeat=Clamp)

// --------------------- Map / world mapping ---------------------
// Set these to your terrain’s world XZ rectangle
uniform vec2 map_origin = vec2(-2816.0, -1158.0); // min XZ
uniform vec2 map_size   = vec2(5632.0,  2316.0);  // size XZ

// --------------------- Terrain / water params ---------------------
uniform float height_scale   = 60.0;        // world units from black→white
uniform float sea_level_norm = 0.5019608;   // ≈128/255
uniform float terrain_base_y = 0.0;         // terrain Y offset (if any)
uniform float water_level_y  = 0.0;         // sea level (this plane’s Y)
uniform float deep_world     = 20.0;        // units to reach “deep”

// --------------------- Colors / optics ---------------------
uniform vec3  shallow_color : source_color = vec3(0.10, 0.45, 0.60);
uniform vec3  deep_color    : source_color = vec3(0.02, 0.10, 0.18);
uniform float shallow_alpha = 0.25;
uniform float deep_alpha    = 0.85;

// --------------------- Surface / animation ---------------------
uniform float normal_scale   = 0.65;
uniform float ripple_tiling  = 0.06;
uniform vec2  ripple_speed_a = vec2( 0.06,  0.02);
uniform vec2  ripple_speed_b = vec2(-0.03, -0.05);

// --------------------- Refraction / Fresnel ---------------------
uniform float refraction_strength = 0.012; // set 0 to disable
uniform float fresnel_power       = 5.0;

// --------------------- Foam ---------------------
uniform float foam_band      = 0.22; // 0..1 band near shore
uniform float foam_intensity = 0.9;
uniform float foam_tiling    = 0.35;
uniform vec2  foam_scroll    = vec2(0.035, -0.02);

// --------------------- Gentle vertical wobble ---------------------
uniform float wave_amp  = 0.05;
uniform float wave_freq = 0.6;
uniform vec2  wave_dir  = vec2(1.0, 0.3);

// --------------------- Mask settings ---------------------
uniform bool  use_water_mask = false;
uniform float mask_threshold = 0.5;

// Pass world position to fragment
varying vec3 v_world_pos;

// helper to force tiling even if a texture imported with Clamp
vec2 wrap01(vec2 uv) { return fract(uv); }

void vertex() {
    float t = TIME;
    float phase = (VERTEX.x * wave_dir.x + VERTEX.z * wave_dir.y) * wave_freq;
    VERTEX.y += sin(phase + t) * wave_amp;

    v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    // optional water mask (still uses mesh UV; leave as-is unless your mask is world-space)
    if (use_water_mask) {
        float m = texture(water_mask, UV).r;
        if (m < mask_threshold) { ALPHA = 0.0; discard; }
    }

    // ---------- WORLD-SPACE UV THAT WRAPS EAST/WEST ----------
    vec2 rel = v_world_pos.xz - map_origin;

    float w = max(map_size.x, 0.0001);
    rel.x = rel.x - floor(rel.x / w) * w;   // wrap X (east/west)

    float h = max(map_size.y, 0.0001);
    rel.y = clamp(rel.y, 0.0, h);           // clamp Z (north/south) — use wrap if you also tile N/S

    vec2 wuv = rel / vec2(w, h);            // 0..1 normalized world-space UV
    // --------------------------------------------------------

    // animated normals (dual scrolling) — use world-space wuv to avoid seams at tile borders
    // If you prefer mesh UVs: replace 'wuv' with 'UV' in the two lines below.
    vec2 uv_a = wrap01(wuv * ripple_tiling + ripple_speed_a * TIME);
    vec2 uv_b = wrap01(wuv * ripple_tiling + ripple_speed_b * TIME);
    vec3 n1 = texture(ripples_a, uv_a).rgb * 2.0 - 1.0;
    vec3 n2 = texture(ripples_b, uv_b).rgb * 2.0 - 1.0;
    vec3 w_n = normalize(mix(n1, n2, 0.5));
    NORMAL = normalize(mix(NORMAL, w_n, normal_scale));

    // -------- WORLD-SPACE COAST FADE (seamless) --------
    vec2 hm_uv = wuv; // wrapped X, clamped Z
    float htex = texture(heightmap_tex, hm_uv).r; // 0..1 linear
    float world_h = (htex - sea_level_norm) * height_scale + terrain_base_y;

    float submergence = max(0.0, water_level_y - world_h);                 // units below sea level
    float depth01 = clamp(submergence / max(0.001, deep_world), 0.0, 1.0); // 0=shore,1=deep
    // ----------------------------------------------------

    // depth-tinted color
    vec3 water_col = mix(shallow_color, deep_color, depth01);

    // optional refraction
    vec3 refr_col = water_col;
    if (refraction_strength > 0.0) {
        vec2 refr_uv = clamp(SCREEN_UV + NORMAL.xy * refraction_strength, vec2(0.0), vec2(1.0));
        refr_col = textureLod(SCREEN_TEXTURE, refr_uv, 0.0).rgb;
    }

    // Foam near shore — also driven by world-space wuv to avoid seams
    float shore = 1.0 - depth01; // 1 at shore, 0 deep
    float foam_mask = smoothstep(0.0, max(0.0001, foam_band), shore);
    vec2 fuv = wrap01(wuv * foam_tiling + foam_scroll * TIME); // swap 'wuv'→'UV' if you prefer mesh UVs
    float foam_noise = texture(foam_tex, fuv).r;
    float foam = foam_mask * foam_noise * foam_intensity;

    // Fresnel-ish mix
    float fres = pow(1.0 - max(dot(NORMAL, VIEW), 0.0), fresnel_power);
    vec3 final_col = mix(refr_col, water_col, fres);

    ALBEDO = final_col + vec3(foam);
    ALPHA  = mix(shallow_alpha, deep_alpha, depth01);

    ROUGHNESS = 0.08;
    METALLIC  = 0.0;
    SPECULAR  = 0.5;
}
