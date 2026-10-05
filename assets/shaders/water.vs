#version 330

// ═══════════════════════════════════════════════════════════════════════
//  VERTEX SHADER COMPAGNON DE water.fs
//
//  Le vertex shader PAR DÉFAUT de raylib ne fournit PAS fragPosition (il ne
//  sort que fragTexCoord et fragColor) : c'est ce qui causait le rendu tout
//  blanc, le fragment shader déclarant un varying "fragPosition" jamais
//  rempli, ce qui fait échouer le lien du programme -> raylib retombe sur
//  son shader par défaut (texture blanche x teinte blanche = blanc uni).
//
//  Ce vertex shader est une copie du vertex shader par défaut de raylib
//  (GLSL 330), avec l'ajout du calcul de fragPosition en espace monde.
// ═══════════════════════════════════════════════════════════════════════

in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec4 vertexColor;

out vec2 fragTexCoord;
out vec4 fragColor;
out vec3 fragPosition;

uniform mat4 mvp;
uniform mat4 matModel;

void main()
{
    fragTexCoord = vertexTexCoord;
    fragColor = vertexColor;
    fragPosition = vec3(matModel * vec4(vertexPosition, 1.0));

    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
