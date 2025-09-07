shader_type spatial;

// --- Color textures ---
uniform sampler2D base_tex    : source_color;
uniform sampler2D overlay_tex : source_color;
uniform sampler2D mask_tex; // white = overlay, black = base

// --- Normal maps (tangent space) ---
uniform sampler2D base_norm;
uniform sampler2D overlay_norm;
uniform float base_norm_strength    = 1.0; // 0..2 typical
uniform float overlay_norm_strength = 1.0; // 0..2 typical

// --- UV controls ---
uniform vec2 base_tiling    = vec2(1.0);
uniform vec2 overlay_tiling = vec2(1.0);
uniform vec2 overlay_scroll = vec2(0.1, 0.0); // UV units per second

// Mask follows overlay by default; can be decoupled if desired.
uniform bool  mask_uses_overlay_uv = true;
uniform vec2  mask_tiling_override = vec2(1.0);
uniform vec2  mask_scroll_override = vec2(0.0);

// --- Overlay mix ---
uniform float overlay_strength = 1.0; // 0..1 global gain on mask

// --- PBR params (optional) ---
uniform float roughness = 0.6;
uniform float metallic  = 0.0;

uniform sampler2D night_lights_tex : source_color, repeat_enable; // optional
uniform vec3  emission_color : source_color = vec3(0.44, 0.78, 0.94); // ~#6FC8F0
uniform float emission_energy : hint_range(0.0, 10.0) = 4.0;
uniform float rim_strength : hint_range(0.0, 6.0) = 0.0; // set >0 to enable
uniform float rim_power    : hint_range(0.5, 8.0) = 2.2;
uniform float city_boost   : hint_range(0.0, 10.0) = 3.0;
// ------------------ Helpers ------------------

float clamp01(float x) { return clamp(x, 0.0, 1.0); }

// Minimal-difference across wrap: bring derivative into [-0.5, 0.5] range.
vec2 wrap_aware_deriv(vec2 d) {
    return d - round(d);
}

// Half-texel padding keeps taps away from 0/1 edges.
vec2 pad_uv(vec2 uv, sampler2D tex) {
    vec2 texels = vec2(textureSize(tex, 0));
    vec2 pad = 0.5 / texels;
    return clamp(uv, pad, 1.0 - pad);
}

// Repeat-sample with explicit wrap-aware derivatives (seam-safe).
vec4 sample_repeat_grad(sampler2D tex, vec2 uv_wrapped) {
    // uv_wrapped may cross 0/1; keep a tiled copy for derivatives
    vec2 dpx = dFdx(uv_wrapped);
    vec2 dpy = dFdy(uv_wrapped);
    dpx = wrap_aware_deriv(dpx);
    dpy = wrap_aware_deriv(dpy);

    // Use repeated coords for sampling, padded off the edges
    vec2 uv = pad_uv(fract(uv_wrapped), tex);
    return textureGrad(tex, uv, dpx, dpy);
}

// Sample & decode tangent-space normal, with strength scaling.
vec3 normal_from_map_repeat(sampler2D tex, vec2 uv_wrapped, float strength) {
    vec3 n = sample_repeat_grad(tex, uv_wrapped).rgb * 2.0 - 1.0;
    n.xy *= strength;
    n.z = sqrt(max(1.0 - dot(n.xy, n.xy), 0.0));
    return normalize(n);
}

void fragment() {
    // Unwrapped (continuous) UVs — do NOT fract here
    vec2 uv_base_wrapped = UV * base_tiling;
    vec2 uv_over_wrapped = UV * overlay_tiling + overlay_scroll * TIME;

    // Mask UVs (follow overlay by default)
    vec2 uv_mask_wrapped = uv_over_wrapped;
    if (!mask_uses_overlay_uv) {
        uv_mask_wrapped = UV * mask_tiling_override + mask_scroll_override * TIME;
    }

    // --- Albedo with seam-safe sampling ---
    vec3 base_alb    = sample_repeat_grad(base_tex,    uv_base_wrapped).rgb;
    vec3 overlay_alb = sample_repeat_grad(overlay_tex, uv_over_wrapped).rgb;

    float m = sample_repeat_grad(mask_tex, uv_mask_wrapped).r; // 0..1
    float t = clamp01(m * overlay_strength);

    ALBEDO = mix(base_alb, overlay_alb, t);

    // --- Normal blending (tangent space) ---
    vec3 n_base_tan = normal_from_map_repeat(base_norm,    uv_base_wrapped, base_norm_strength);
    vec3 n_over_tan = normal_from_map_repeat(overlay_norm, uv_over_wrapped, overlay_norm_strength);
    vec3 n_tan = normalize(mix(n_base_tan, n_over_tan, t));

    // Tangent-to-world (ensure mesh has tangents; enable "Generate Tangents" if needed)
    mat3 TBN = mat3(normalize(TANGENT), normalize(BINORMAL), normalize(NORMAL));
    vec3 n_world = normalize(TBN * n_tan);
	
	    vec3 albedo = texture(base_tex, UV).rgb;
    ALBEDO = albedo;

    // Base emission color
    vec3 e = emission_color * emission_energy;

    // Optional: city lights texture (white-on-black)
    vec3 cities = texture(night_lights_tex, UV).rgb * city_boost;
    e += cities;

    // Optional: soft rim glow for atmosphere
    float rim = pow(1.0 - dot(normalize(NORMAL), normalize(VIEW)), rim_power);
    e += emission_color * rim * rim_strength;

    EMISSION = e; // <- this makes it glow (with Glow enabled in the Environment)
    NORMAL = n_world;

    ROUGHNESS = roughness;
    METALLIC  = metallic;
}
