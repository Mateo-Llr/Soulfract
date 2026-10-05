#version 330

// Shader de remplissage vertical pour les bulles de faim / soif.
// Repère les pixels de la couleur "vide" (27,27,27) sur la texture bubble_bar
// et les recolore de bas en haut en fonction de fillPercent (0 = vide, 1 = plein).
// Contrairement à la health bar (nine-slice, plusieurs quads -> coordonnées
// écran), la bulle est une texture unique dessinée en un seul quad : on peut
// donc travailler directement en UV local (fragTexCoord).

in vec2 fragTexCoord;
in vec4 fragColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;

uniform float fillPercent; // 0..1 : 0 = vide, 1 = plein
uniform vec3 fillColor;    // couleur du liquide (orange/brun pour la faim, bleu pour la soif)

out vec4 finalColor;

void main()
{
    vec4 texelColor = texture(texture0, fragTexCoord) * fragColor * colDiffuse;

    vec3 target = vec3(27.0 / 255.0, 27.0 / 255.0, 27.0 / 255.0);
    float diff = distance(texelColor.rgb, target);

    const float threshold = 0.035;

    if (diff < threshold)
    {
        // En raylib, fragTexCoord.y = 0 en haut de la texture et 1 en bas.
        // On veut que le remplissage parte du BAS (vide en haut, plein en bas
        // qui remonte), donc on inverse pour obtenir une coordonnée "hauteur
        // depuis le bas" : 0 = bas, 1 = haut.
        float heightFromBottom = 1.0 - fragTexCoord.y;

        if (heightFromBottom <= fillPercent)
        {
            // Zone "remplie"
            finalColor = vec4(fillColor, texelColor.a);
        }
        else
        {
            // Zone "vide" -> couleur d'origine
            finalColor = texelColor;
        }
    }
    else
    {
        // Pixel de bordure / décor de la bulle -> inchangé
        finalColor = texelColor;
    }
}
