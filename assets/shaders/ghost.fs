#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
out vec4 finalColor;

uniform sampler2D texture0;
uniform float uTime;
uniform float uEyeGlowMode;
uniform float uEyeGlowOpacity;

// Transformation de la Camera2D de raylib (screen = (world - target) * zoom +
// offset), transmise pour pouvoir inverser gl_FragCoord vers une position
// MONDE. Sans ça, le motif du voile est ancré à l'écran : il glisse/dérive
// dès que la caméra bouge ou zoome, au lieu de rester collé aux entités.
uniform vec2 uCameraTarget;
uniform vec2 uCameraOffset;
uniform float uCameraZoom;
uniform float uScreenHeight;

// Échelle du motif en unités MONDE (pixels monde). Une entité fait
// grossièrement 40-50px de haut ; on reprend une densité de bruit proche de
// l'ancienne (qui faisait uv * ~5 sur une texture 0..1) en divisant les
// coordonnées monde par une constante du même ordre de grandeur.
const float NOISE_PIXEL_SCALE = 42.0;

float hash3(vec3 p)
{
    p = fract(p * 0.1031);
    p += dot(p, p.yxz + 33.33);
    return fract((p.x + p.y) * p.z);
}

float valueNoise3(vec3 p)
{
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);

    float x00 = mix(hash3(i + vec3(0.0, 0.0, 0.0)), hash3(i + vec3(1.0, 0.0, 0.0)), f.x);
    float x10 = mix(hash3(i + vec3(0.0, 1.0, 0.0)), hash3(i + vec3(1.0, 1.0, 0.0)), f.x);
    float x01 = mix(hash3(i + vec3(0.0, 0.0, 1.0)), hash3(i + vec3(1.0, 0.0, 1.0)), f.x);
    float x11 = mix(hash3(i + vec3(0.0, 1.0, 1.0)), hash3(i + vec3(1.0, 1.0, 1.0)), f.x);
    return mix(mix(x00, x10, f.y), mix(x01, x11, f.y), f.z);
}

float ghostShape(vec2 base)
{
    float warpX = valueNoise3(vec3(base * 0.55 + vec2(12.0, 4.0), uTime * 0.19 + 3.0));
    float warpY = valueNoise3(vec3(base * 0.55 + vec2(2.0, 16.0), uTime * 0.19 + 11.0));
    vec2 warped = base + (vec2(warpX, warpY) - 0.5) * 2.2;
    float broad = valueNoise3(vec3(warped, uTime * 0.31));
    float detail = valueNoise3(vec3(warped * 2.1, uTime * 0.53 + 19.0));
    return broad * 0.76 + detail * 0.24;
}

void main()
{
    vec4 texel = texture(texture0, fragTexCoord);

    if (uEyeGlowMode > 0.5)
    {
        vec2 texelSize = 1.0 / vec2(textureSize(texture0, 0));
        float glow = 0.0;
        for (int x = -2; x <= 2; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                float distanceSquared = float(x * x + y * y);
                float sampleAlpha = texture(texture0, fragTexCoord + vec2(x, y) * texelSize).a;
                glow = max(glow, sampleAlpha * exp(-distanceSquared * 0.35));
            }
        }

        float alpha = max(texel.a, glow * 0.72) * fragColor.a * uEyeGlowOpacity;
        vec3 eyeColor = mix(vec3(0.12, 0.62, 1.0), vec3(0.0, 0.98, 1.0), texel.a);
        finalColor = vec4(eyeColor, alpha);
        return;
    }

    if (texel.a < 0.02)
        discard;

    // IMPORTANT : on calcule le motif à partir d'une position MONDE, pas
    // écran. Une entité est composée de plusieurs textures superposées
    // (corps, tête, équipement...), chacune dessinée avec son propre appel
    // de rendu pendant que ce shader est actif : fragTexCoord (UV 0..1 propre
    // à CHAQUE texture) faisait repartir chaque calque de son propre motif,
    // indépendant des autres (effet de patchwork). gl_FragCoord seul réglait
    // ce problème mais ancrait le motif à l'ÉCRAN : il "glissait" dès que la
    // caméra bougeait ou zoomait, puisqu'un même pixel écran ne correspond
    // plus au même point du monde d'une frame à l'autre.
    // On inverse donc la transformation de la Camera2D (screen = (world -
    // target) * zoom + offset) pour retrouver la position MONDE du pixel.
    // gl_FragCoord est en repère OpenGL (origine en bas à gauche, Y vers le
    // haut) alors que la caméra raylib travaille en repère écran (origine en
    // haut à gauche, Y vers le bas) : on doit donc flipper Y avec
    // uScreenHeight avant d'inverser la transformation.
    vec2 screenPos = vec2(gl_FragCoord.x, uScreenHeight - gl_FragCoord.y);
    vec2 worldPos = (screenPos - uCameraOffset) / uCameraZoom + uCameraTarget;
    float noise = ghostShape(worldPos / NOISE_PIXEL_SCALE);
    float veil = smoothstep(0.30, 0.72, noise);
    float alpha = texel.a * fragColor.a * mix(0.38, 0.82, veil);

    vec3 source = texel.rgb * fragColor.rgb;
    vec3 spectral = mix(source, vec3(0.34, 0.76, 0.96), 0.58);
    spectral += vec3(0.06, 0.14, 0.19) * veil;
    finalColor = vec4(spectral, alpha);
}