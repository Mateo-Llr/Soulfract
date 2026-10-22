#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

out vec4 finalColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;

uniform float uProgress;     // 0 = intact, 1 = entièrement dissous
uniform float uTime;         // pour faire "couler" le motif vers le haut
uniform vec2  uCameraTarget;
uniform vec2  uCameraOffset;
uniform float uCameraZoom;
uniform float uScreenHeight;

// --- Hash & value noise ---
float hash(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float valueNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// 3 octaves : le motif a des "grappes" de différentes tailles (comme de vraies particules)
float fbm(vec2 p) {
    return valueNoise(p) * 0.55
         + valueNoise(p * 2.17) * 0.30
         + valueNoise(p * 4.61) * 0.15;
}

// gl_FragCoord a son origine en bas-gauche (convention OpenGL) : on la
// remet en coordonnées écran (origine haut-gauche) comme le fait raylib.
vec2 screenToWorld(vec2 screenPos) {
    return (screenPos - uCameraOffset) / uCameraZoom + uCameraTarget;
}

void main() {
    vec4 texel = texture(texture0, fragTexCoord) * fragColor * colDiffuse;
    if (texel.a < 0.01) discard;

    // Position MONDE du pixel : le motif de dissolution est ainsi solidaire
    // du monde et ne "glisse" pas quand la caméra bouge ou zoome.
    vec2 screenPos = vec2(gl_FragCoord.x, uScreenHeight - gl_FragCoord.y);
    vec2 worldPos = screenToWorld(screenPos);

    // Le motif "flue" vers le haut au fil du temps : visuellement, les
    // particules qui se détachent semblent s'envoler vers le ciel.
    vec2 noisePos = worldPos * 0.08;
    noisePos.y += uTime * 0.9;

    float n = fbm(noisePos);

    // Progression du seuil : * 1.05 pour garantir qu'à progress = 1 tout
    // soit effectivement dissous (fbm reste dans [0,1]).
    float threshold = uProgress * 1.05;
    if (n < threshold) discard;

    // Halo de braise sur les pixels survivants juste au bord de la dissolution :
    // accentue la sensation de "particules qui s'évaporent".
    float edgeDist = n - threshold;
    if (edgeDist < 0.06) {
        float glow = 1.0 - edgeDist / 0.06;
        texel.rgb = mix(texel.rgb, vec3(1.0, 0.6, 0.2), glow * 0.7);
    }

    finalColor = texel;
}