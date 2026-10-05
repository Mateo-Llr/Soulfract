// NineSlice.cs - Version corrigée avec fallback partiel
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public class NineSliceTexture
    {
        public Texture2D TopLeft { get; set; }
        public Texture2D TopMid { get; set; }
        public Texture2D TopRight { get; set; }
        public Texture2D MidLeft { get; set; }
        public Texture2D MidMid { get; set; }
        public Texture2D MidRight { get; set; }
        public Texture2D BottomLeft { get; set; }
        public Texture2D BottomMid { get; set; }
        public Texture2D BottomRight { get; set; }
        
        // Conservées pour compatibilité (référence : coin haut-gauche)
        public int SliceWidth { get; private set; }
        public int SliceHeight { get; private set; }
        
        public float Scale { get; set; } = 4f;
        
        //  MODIFIÉ : Considère valide si au moins la texture centrale existe
        public bool IsValid => MidMid.Id != 0;
        
        // Hauteur/largeur naturelles (Scale = 1) de chaque ligne/colonne, calculées
        // à partir de la texture RÉELLE de chaque morceau (et non plus une seule
        // dimension partagée) pour éviter que les bords soient rognés quand les
        // textures top/mid/bottom n'ont pas toutes la même hauteur.
        private int TopRowHeightPx => TopMid.Id != 0 ? TopMid.Height : (TopLeft.Id != 0 ? TopLeft.Height : (TopRight.Id != 0 ? TopRight.Height : 0));
        private int BottomRowHeightPx => BottomMid.Id != 0 ? BottomMid.Height : (BottomLeft.Id != 0 ? BottomLeft.Height : (BottomRight.Id != 0 ? BottomRight.Height : 0));
        private int LeftColWidthPx => MidLeft.Id != 0 ? MidLeft.Width : (TopLeft.Id != 0 ? TopLeft.Width : (BottomLeft.Id != 0 ? BottomLeft.Width : 0));
        private int RightColWidthPx => MidRight.Id != 0 ? MidRight.Width : (TopRight.Id != 0 ? TopRight.Width : (BottomRight.Id != 0 ? BottomRight.Width : 0));
        
        public NineSliceTexture(string basePath, float scale = 4f)
        {
            Scale = scale;
            TopLeft = LoadSlice($"{basePath}_top1.png");
            TopMid = LoadSlice($"{basePath}_top2.png");
            TopRight = LoadSlice($"{basePath}_top3.png");
            MidLeft = LoadSlice($"{basePath}_mid1.png");
            MidMid = LoadSlice($"{basePath}_mid2.png");
            MidRight = LoadSlice($"{basePath}_mid3.png");
            BottomLeft = LoadSlice($"{basePath}_bottom1.png");
            BottomMid = LoadSlice($"{basePath}_bottom2.png");
            BottomRight = LoadSlice($"{basePath}_bottom3.png");
            
            if (MidMid.Id != 0)
            {
                SliceWidth = MidMid.Width;
                SliceHeight = MidMid.Height;
            }
            else if (TopLeft.Id != 0)
            {
                SliceWidth = TopLeft.Width;
                SliceHeight = TopLeft.Height;
            }
        }
        
        private static Texture2D LoadSlice(string path)
        {
            if (File.Exists(path))
            {
                var tex = Raylib.LoadTexture(path);
                Raylib.SetTextureFilter(tex, TextureFilter.Point);
                return tex;
            }
            return new Texture2D();
        }
        
        public void Draw(Rectangle destRect, Color tint)
        {
            DrawInternal(destRect, tint, mirrorHorizontally: false);
        }

        public void DrawMirrored(Rectangle destRect, Color tint)
        {
            DrawInternal(destRect, tint, mirrorHorizontally: true);
        }

        public void DrawSplit(Rectangle destRect, Color tint)
        {
            if (!IsValid) return;

            float halfWidth = destRect.Width / 2f;
            Rectangle leftRect = new Rectangle(destRect.X, destRect.Y, halfWidth, destRect.Height);
            Rectangle rightRect = new Rectangle(destRect.X + halfWidth, destRect.Y, halfWidth, destRect.Height);

            Draw(leftRect, tint);
            DrawMirrored(rightRect, tint);
        }

        private void DrawInternal(Rectangle destRect, Color tint, bool mirrorHorizontally)
        {
            if (!IsValid) return;
            
            //  Chaque ligne/colonne utilise SA PROPRE dimension (et non une taille
            // unique partagée), pour éviter que des morceaux plus grands que MidMid
            // soient rognés à l'affichage.
            float topH = TopRowHeightPx * Scale;
            float bottomH = BottomRowHeightPx * Scale;
            float leftW = LeftColWidthPx * Scale;
            float rightW = RightColWidthPx * Scale;
            
            float destW = destRect.Width;
            float destH = destRect.Height;
            
            // Garde-fou : si les bords (haut+bas ou gauche+droite) dépassent la
            // taille du destRect, on les rétrécit proportionnellement pour éviter
            // tout chevauchement/débordement (ex: Scale trop grande pour barH).
            if (topH + bottomH > destH && (topH + bottomH) > 0)
            {
                float ratio = destH / (topH + bottomH);
                topH *= ratio;
                bottomH *= ratio;
            }
            if (leftW + rightW > destW && (leftW + rightW) > 0)
            {
                float ratio = destW / (leftW + rightW);
                leftW *= ratio;
                rightW *= ratio;
            }
            
            float centerWidth = destW - leftW - rightW;
            float centerHeight = destH - topH - bottomH;
            
            if (centerWidth < 0) centerWidth = 0;
            if (centerHeight < 0) centerHeight = 0;

            float leftX = mirrorHorizontally ? destRect.X + destW - leftW : destRect.X;
            float centerX = mirrorHorizontally ? destRect.X + rightW : destRect.X + leftW;
            float rightX = mirrorHorizontally ? destRect.X : destRect.X + destW - rightW;
            
            // === Ligne du haut ===
            if (TopLeft.Id != 0)
                Raylib.DrawTexturePro(TopLeft,
                    mirrorHorizontally ? new Rectangle(TopLeft.Width, 0, -TopLeft.Width, TopLeft.Height) : new Rectangle(0, 0, TopLeft.Width, TopLeft.Height),
                    new Rectangle(leftX, destRect.Y, leftW, topH),
                    Vector2.Zero, 0, tint);
            
            if (TopMid.Id != 0)
                Raylib.DrawTexturePro(TopMid,
                    mirrorHorizontally ? new Rectangle(TopMid.Width, 0, -TopMid.Width, TopMid.Height) : new Rectangle(0, 0, TopMid.Width, TopMid.Height),
                    new Rectangle(centerX, destRect.Y, centerWidth, topH),
                    Vector2.Zero, 0, tint);
            
            if (TopRight.Id != 0)
                Raylib.DrawTexturePro(TopRight,
                    mirrorHorizontally ? new Rectangle(TopRight.Width, 0, -TopRight.Width, TopRight.Height) : new Rectangle(0, 0, TopRight.Width, TopRight.Height),
                    new Rectangle(rightX, destRect.Y, rightW, topH),
                    Vector2.Zero, 0, tint);
            
            // === Ligne du milieu ===
            if (MidLeft.Id != 0)
                Raylib.DrawTexturePro(MidLeft,
                    mirrorHorizontally ? new Rectangle(MidLeft.Width, 0, -MidLeft.Width, MidLeft.Height) : new Rectangle(0, 0, MidLeft.Width, MidLeft.Height),
                    new Rectangle(leftX, destRect.Y + topH, leftW, centerHeight),
                    Vector2.Zero, 0, tint);
            
            if (MidMid.Id != 0)
                Raylib.DrawTexturePro(MidMid,
                    mirrorHorizontally ? new Rectangle(MidMid.Width, 0, -MidMid.Width, MidMid.Height) : new Rectangle(0, 0, MidMid.Width, MidMid.Height),
                    new Rectangle(centerX, destRect.Y + topH, centerWidth, centerHeight),
                    Vector2.Zero, 0, tint);
            
            if (MidRight.Id != 0)
                Raylib.DrawTexturePro(MidRight,
                    mirrorHorizontally ? new Rectangle(MidRight.Width, 0, -MidRight.Width, MidRight.Height) : new Rectangle(0, 0, MidRight.Width, MidRight.Height),
                    new Rectangle(rightX, destRect.Y + topH, rightW, centerHeight),
                    Vector2.Zero, 0, tint);
            
            // === Ligne du bas ===
            if (BottomLeft.Id != 0)
                Raylib.DrawTexturePro(BottomLeft,
                    mirrorHorizontally ? new Rectangle(BottomLeft.Width, 0, -BottomLeft.Width, BottomLeft.Height) : new Rectangle(0, 0, BottomLeft.Width, BottomLeft.Height),
                    new Rectangle(leftX, destRect.Y + destH - bottomH, leftW, bottomH),
                    Vector2.Zero, 0, tint);
            
            if (BottomMid.Id != 0)
                Raylib.DrawTexturePro(BottomMid,
                    mirrorHorizontally ? new Rectangle(BottomMid.Width, 0, -BottomMid.Width, BottomMid.Height) : new Rectangle(0, 0, BottomMid.Width, BottomMid.Height),
                    new Rectangle(centerX, destRect.Y + destH - bottomH, centerWidth, bottomH),
                    Vector2.Zero, 0, tint);
            
            if (BottomRight.Id != 0)
                Raylib.DrawTexturePro(BottomRight,
                    mirrorHorizontally ? new Rectangle(BottomRight.Width, 0, -BottomRight.Width, BottomRight.Height) : new Rectangle(0, 0, BottomRight.Width, BottomRight.Height),
                    new Rectangle(rightX, destRect.Y + destH - bottomH, rightW, bottomH),
                    Vector2.Zero, 0, tint);
        }
        
        /// <summary>
        /// Hauteur totale "naturelle" du nine slice pour une Scale donnée
        /// (haut + milieu + bas), utile pour dimensionner le conteneur qui
        /// accueille la barre sans écraser ni superposer les bords.
        /// </summary>
        public float GetNaturalHeight(float scale) => (TopRowHeightPx + MidRowHeightPx + BottomRowHeightPx) * scale;
        
        /// <summary>
        /// Largeur totale "naturelle" du nine slice pour une Scale donnée
        /// (gauche + milieu + droite).
        /// </summary>
        public float GetNaturalWidth(float scale) => (LeftColWidthPx + MidColWidthPx + RightColWidthPx) * scale;
        
        private int MidRowHeightPx => MidMid.Id != 0 ? MidMid.Height : 0;
        private int MidColWidthPx => MidMid.Id != 0 ? MidMid.Width : 0;
        
        public void Unload()
        {
            if (TopLeft.Id != 0) Raylib.UnloadTexture(TopLeft);
            if (TopMid.Id != 0) Raylib.UnloadTexture(TopMid);
            if (TopRight.Id != 0) Raylib.UnloadTexture(TopRight);
            if (MidLeft.Id != 0) Raylib.UnloadTexture(MidLeft);
            if (MidMid.Id != 0) Raylib.UnloadTexture(MidMid);
            if (MidRight.Id != 0) Raylib.UnloadTexture(MidRight);
            if (BottomLeft.Id != 0) Raylib.UnloadTexture(BottomLeft);
            if (BottomMid.Id != 0) Raylib.UnloadTexture(BottomMid);
            if (BottomRight.Id != 0) Raylib.UnloadTexture(BottomRight);
        }
    }
}