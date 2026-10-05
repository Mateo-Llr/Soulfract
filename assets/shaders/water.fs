#version 330

// ═══════════════════════════════════════════════════════════════════════
//  SHADER EAU — caustiques procédurales par bruit (remplace les textures
//  d'eau statiques). Principe (cf. https://youtu.be/pcd4DdqO_BM) :
//
//   1) Un premier bruit ("warp"), qui bouge très lentement, distord les
//      coordonnées d'échantillonnage -> donne l'impression que le motif
//      "flotte" au lieu d'être figé.
//   2) Deux couches de bruit de caustiques, qui défilent chacune dans une
//      direction différente. Là où elles se recoupent (différence proche
//      de 0), on obtient les filaments lumineux caractéristiques des
//      caustiques. On seuille (smoothstep) ce résultat pour décider,
//      pixel par pixel, s'il est "bleu profond" ou "blanc lumineux".
//
//  fragPosition est fourni par le vertex shader par défaut de raylib : il
//  contient la position MONDE (en pixels) du fragment, pas les UV 0-1 de
//  la tuile. C'est ce qui permet au motif d'être continu entre les tuiles
//  d'eau adjacentes plutôt que de se répéter identiquement sur chacune.
// ═══════════════════════════════════════════════════════════════════════

in vec3 fragPosition;
in vec2 fragTexCoord;
in vec4 fragColor;

out vec4 finalColor;

uniform float uTime;          // horloge du shader (secondes, boucle en C#)
uniform vec3  uColorDeep;     // bleu profond (base de l'eau)
uniform vec3  uColorShallow;  // bleu clair (eau peu profonde)
uniform vec3  uColorCaustic;  // blanc/cyan des filaments de lumière
uniform float uCausticStrength; // 0..1, intensité globale des caustiques

// ── Bruit de valeur 2D (bon marché, suffisant pour cet effet) ──────────
float hash(vec2 p)
{
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float valueNoise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y;
}

void main()
{
    vec2 worldPos = fragPosition.xy;

    // ── 1) Bruit de mouvement lent : distord les UV des caustiques ─────
    vec2 warpUv = worldPos * 0.010 + vec2(uTime * 0.015, -uTime * 0.011);
    float warpX = valueNoise(warpUv) - 0.5;
    float warpY = valueNoise(warpUv + vec2(31.7, 5.2)) - 0.5;
    vec2 warp = vec2(warpX, warpY) * 14.0; // amplitude du flottement, en pixels

    // ── 2) Deux couches de bruit qui défilent en sens opposés ──────────
    vec2 uv1 = (worldPos + warp) * 0.032 + vec2(uTime * 0.055, uTime * 0.027);
    vec2 uv2 = (worldPos + warp) * 0.047 - vec2(uTime * 0.038, uTime * 0.061);

    float n1 = valueNoise(uv1);
    float n2 = valueNoise(uv2);

    // Filaments = endroits où les deux couches se recoupent (différence ~0)
    float caustic = 1.0 - abs(n1 - n2) * 2.0;
    caustic = clamp(caustic, 0.0, 1.0);
    caustic = smoothstep(0.55, 0.92, caustic) * uCausticStrength;

    // Un léger dégradé de "profondeur" à basse fréquence, pour que l'eau
    // ne soit pas d'un bleu parfaitement uniforme entre les caustiques.
    float depthShade = valueNoise(worldPos * 0.006 + uTime * 0.004);
    vec3 baseColor = mix(uColorDeep, uColorShallow, depthShade * 0.5 + 0.25);

    vec3 color = mix(baseColor, uColorCaustic, caustic);

    // fragColor.a permet à l'appelant (draw call C#) de faire varier la
    // transparence par tuile (ex : profondeur/océan vs mare peu profonde)
    // sans devoir changer d'uniform à chaque tuile.
    finalColor = vec4(color, fragColor.a);
}
