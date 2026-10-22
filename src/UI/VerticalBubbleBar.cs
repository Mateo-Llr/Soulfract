// VerticalBubbleBar.cs - Bulle de statut (faim / soif) qui se remplit du bas vers le haut
// Réutilise le même principe que la health bar (une texture avec une zone "vide"
// couleur (27,27,27) recolorée par un shader), mais en une seule texture (pas de
// nine-slice) et avec un remplissage vertical au lieu d'horizontal.
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public class VerticalBubbleBar
    {
        public Texture2D Texture { get; private set; }
        private Shader _shader;
        private bool _shaderLoaded = false;
        private int _locFillPercent;
        private int _locFillColor;

        public bool IsValid => Texture.Id != 0;
        
        /// <summary>Ratio largeur/hauteur natif de la texture (pour dessiner sans l'étirer).</summary>
        public float AspectRatio => Texture.Height > 0 ? (float)Texture.Width / Texture.Height : 1f;
        
        /// <summary>Calcule un rectangle de destination qui respecte le ratio natif de la
        /// texture à partir d'une hauteur cible (évite toute déformation/étirement).</summary>
        public Rectangle GetFitRect(float x, float y, float targetHeight)
        {
            float w = targetHeight * AspectRatio;
            return new Rectangle(x, y, w, targetHeight);
        }

        public VerticalBubbleBar(string texturePath, string shaderPath)
        {
            if (File.Exists(texturePath))
            {
                Texture = Raylib.LoadTexture(texturePath);
                Raylib.SetTextureFilter(Texture, TextureFilter.Point);
            }
            else
            {
                Texture = new Texture2D();
            }

            if (File.Exists(shaderPath))
            {
                _shader = Raylib.LoadShader(null, shaderPath);
                _shaderLoaded = _shader.Id != 0;
                if (_shaderLoaded)
                {
                    _locFillPercent = Raylib.GetShaderLocation(_shader, "fillPercent");
                    _locFillColor = Raylib.GetShaderLocation(_shader, "fillColor");
                }
            }
        }

        /// <summary>
        /// Dessine la bulle. percent : 0 = vide, 1 = plein. fillColor : couleur du
        /// liquide (orange/brun pour la faim, bleu pour la soif par exemple).
        /// </summary>
        public void Draw(Rectangle destRect, float percent, Color fillColor, Color tint)
        {
            if (!IsValid) return;

            if (!_shaderLoaded)
            {
                // Fallback si le shader n'a pas pu être chargé : simple rectangle
                // de couleur superposé, dessiné du bas vers le haut.
                Raylib.DrawTexturePro(Texture,
                    new Rectangle(0, 0, Texture.Width, Texture.Height),
                    destRect, Vector2.Zero, 0, tint);

                float fillH = destRect.Height * Math.Clamp(percent, 0f, 1f);
                Raylib.DrawRectangle(
                    (int)destRect.X, (int)(destRect.Y + destRect.Height - fillH),
                    (int)destRect.Width, (int)fillH, fillColor);
                return;
            }

            float[] col = { fillColor.R / 255f, fillColor.G / 255f, fillColor.B / 255f };
            Raylib.SetShaderValue(_shader, _locFillColor, col, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(_shader, _locFillPercent, Math.Clamp(percent, 0f, 1f), ShaderUniformDataType.Float);

            Raylib.BeginShaderMode(_shader);
            Raylib.DrawTexturePro(Texture,
                new Rectangle(0, 0, Texture.Width, Texture.Height),
                destRect, Vector2.Zero, 0, tint);
            Raylib.EndShaderMode();
        }

        public void Unload()
        {
            if (Texture.Id != 0) Raylib.UnloadTexture(Texture);
            if (_shaderLoaded) Raylib.UnloadShader(_shader);
        }
        
        /// <summary>
        /// Dessine la texture telle quelle, sans shader de remplissage ni déformation
        /// (utilisé pour la grosse bulle décorative qui ne représente aucune jauge).
        /// </summary>
        public void DrawStatic(Rectangle destRect, Color tint)
        {
            if (!IsValid) return;
            Raylib.DrawTexturePro(Texture,
                new Rectangle(0, 0, Texture.Width, Texture.Height),
                destRect, Vector2.Zero, 0, tint);
        }
    }
}
