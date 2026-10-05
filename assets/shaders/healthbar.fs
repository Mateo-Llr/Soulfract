#version 330

// Shader de remplissage pour la health bar nine-slice.
// Repère les pixels de la couleur "vide" (27,27,27) sur la texture et les
// recolore en rouge de gauche à droite en fonction de healthPercent.
// Le calcul se fait en espace ECRAN (gl_FragCoord) et non en UV local,
// car le nine-slice est composé de 9 quads distincts : on a donc besoin
// d'une coordonnée horizontale continue sur toute la largeur de la barre.

in vec2 fragTexCoord;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;

// Position/largeur de la barre en pixels ECRAN (framebuffer), pas en pixels
// "fenêtre logique" -> à multiplier côté C# par le ratio render/screen.
uniform float barX;
uniform float barWidth;
uniform float healthPercent; // 0..1 : valeur RÉELLE actuelle (rouge / vert...)

// Bord de la zone blanche "sur le point de disparaître" (dégât encaissé mais pas
// encore répercuté visuellement). Toujours >= healthPercent. Quand elle est égale
// à healthPercent, il n'y a pas de zone blanche visible.
uniform float previousPercent; // 0..1

// Couleur de remplissage (permet de réutiliser ce shader pour la barre
// d'endurance en vert, sans dupliquer le shader/les assets). Elle doit
// être définie côté C# à chaque frame (SetShaderValue), sinon elle vaut
// (0,0,0) par défaut.
uniform vec3 fillColor;

out vec4 finalColor;

void main()
{
    vec4 texelColor = texture(texture0, fragTexCoord) * fragColor * colDiffuse;

    // Couleur cible : (27,27,27)
    vec3 target = vec3(27.0 / 255.0, 27.0 / 255.0, 27.0 / 255.0);
    float diff = distance(texelColor.rgb, target);

    // Tolérance pour absorber la compression / l'antialiasing éventuel
    const float threshold = 0.035;

    if (diff < threshold)
    {
        float u = (gl_FragCoord.x - barX) / max(barWidth, 1.0);

        if (u <= healthPercent)
        {
            // Zone "remplie" -> couleur fournie par le C# (rouge pour la vie,
            // vert pour l'endurance, etc.)
            finalColor = vec4(fillColor, texelColor.a);
        }
        else if (u <= previousPercent)
        {
            // Zone "sur le point de disparaître" (dégât tout juste subi) -> blanc,
            // le temps que le C# la fasse rétrécir progressivement.
            vec3 white = vec3(1.0, 1.0, 1.0);
            finalColor = vec4(white, texelColor.a);
        }
        else
        {
            // Zone "vide" -> on garde la couleur d'origine (27,27,27)
            finalColor = texelColor;
        }
    }
    else
    {
        // Pixel de bordure / décor -> inchangé
        finalColor = texelColor;
    }
}
