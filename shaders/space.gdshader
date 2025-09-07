shader_type sky;

// ---------------- Controls (safe defaults) ----------------
uniform vec3  space_tint        = vec3(0.0); // keep near black

// Stars
uniform float star_brightness   = 1.8;       // HDR boost for star dots
uniform float star_density      = 0.002;     // 0.0005..0.005 (higher = more stars)
uniform float twinkle_speed     = 0.6;
uniform float twinkle_amount    = 0.20;      // 0..1

// Rotation (radians/sec, around +Y)
uniform float rotation_speed    = 0.01;

// Nebula
uniform float nebula_intensity  = 0.10;      // 0 = off
uniform float nebula_scale      = 2.0;
uniform vec3  nebula_color_a    = vec3(0.6, 0.75, 1.0);
uniform vec3  nebula_color_b    = vec3(1.0, 0.55, 0.85);

// Safety clamp against accidental overbright
uniform float max_hdr           = 10.0;

// Randomization
uniform float seed              = 1.234;     // change to reshuffle the sky

// ---------------- Hash & Noise ----------------
float hash13(vec3 p) {
    return fract(sin(dot(p, vec3(127.1, 311.7, 74.7) + seed)) * 43758.5453123);
}

float value_noise(vec3 p) {
    vec3 i = floor(p);
    vec3 f = fract(p);
    float n000 = hash13(i + vec3(0,0,0));
    float n100 = hash13(i + vec3(1,0,0));
    float n010 = hash13(i + vec3(0,1,0));
    float n110 = hash13(i + vec3(1,1,0));
    float n001 = hash13(i + vec3(0,0,1));
    float n101 = hash13(i + vec3(1,0,1));
    float n011 = hash13(i + vec3(0,1,1));
    float n111 = hash13(i + vec3(1,1,1));
    vec3 u = f * f * (3.0 - 2.0 * f);
    float nx00 = mix(n000, n100, u.x);
    float nx10 = mix(n010, n110, u.x);
    float nx01 = mix(n001, n101, u.x);
    float nx11 = mix(n011, n111, u.x);
    float nxy0 = mix(nx00, nx10, u.y);
    float nxy1 = mix(nx01, nx11, u.y);
    return mix(nxy0, nxy1, u.z);
}

float fbm(vec3 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 5; i++) {
        v += a * value_noise(p);
        p = p * 2.03 + 13.57;
        a *= 0.5;
    }
    return v;
}

// ---------------- Helpers ----------------
mat3 rot_y(float a) {
    float c = cos(a), s = sin(a);
    return mat3(vec3(c,0.0,-s), vec3(0.0,1.0,0.0), vec3(s,0.0,c));
}

// Sparse multi-scale star field with twinkle and stable distribution.
// Uses a single density knob; twinkle applies only where a star exists.
float stars(vec3 dir) {
    dir = normalize(dir);
    dir = rot_y(TIME * rotation_speed) * dir;

    // Three layers for variety
    vec3 c1 = floor(dir * 256.0 + seed);
    vec3 c2 = floor(dir * 512.0 + seed * 1.7);
    vec3 c3 = floor(dir * 1024.0 + seed * 3.1);

    float t = clamp(star_density, 0.0, 0.02); // keep sane
    float s1 = float(hash13(c1) > 1.0 - t);
    float s2 = float(hash13(c2) > 1.0 - t) * 0.6;
    float s3 = float(hash13(c3) > 1.0 - t) * 0.4;

    float s = s1 + s2 + s3;

    // Twinkle only where stars are present
    float tw_seed = hash13(c1 + c2 + c3);
    float tw = 1.0 + twinkle_amount * sin(TIME * twinkle_speed + tw_seed * 6.28318);

    return s * tw;
}

vec3 nebula(vec3 dir) {
    dir = normalize(dir);
    float v = fbm(dir * nebula_scale + vec3(0.0, 2.0, 0.0));
    v = pow(smoothstep(0.55, 1.0, v), 2.0);
    vec3 col = mix(nebula_color_a, nebula_color_b, fbm(dir * (nebula_scale * 1.7)));
    return col * v * nebula_intensity;
}

// ---------------- Main ----------------
void sky() {
    vec3 dir = normalize(EYEDIR);

    float s = stars(dir);
    vec3  n = nebula(dir);

    vec3 base = space_tint + n;

    // Slight coloration from nebula into starlight
    vec3 star_tint = mix(vec3(1.0), normalize(n + 1e-4), 0.12);

    vec3 col = base + s * star_brightness * star_tint;
    COLOR = clamp(col, 0.0, max_hdr); // safety clamp
}
