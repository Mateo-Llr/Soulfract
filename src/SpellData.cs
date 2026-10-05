// SpellData.cs
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public enum SpellElement
    {
        Fire,
        Water,
        Earth,
        Air,
        Ice,
        Lightning,
        Poison,
        Light,
        Dark,
        Metal
    }

    public enum SpellType
    {
        Projectile,   // ligne droite
        Homing,       // poursuit la cible
        Area,         // explosion au point visé
        Rain,         // tombe du ciel sur une zone
        Lightning,    // frappe à l'emplacement visé
        Shield,       // protection autour du joueur
        Summon,       // invoque une entité
        Burst         // large explosion instantanée
    }

    public enum PowerLevel
    {
        Weak,      // 1 projectile, petits dégâts
        Medium,    // 2‑3 projectiles ou zone moyenne
        Strong,    // 5‑6 projectiles ou grosse zone
        Massive    // nombreux projectiles ou énorme zone
    }

    public struct SpellData
    {
        public SpellElement Element;
        public SpellType Type;
        public PowerLevel Power;

        public string GetDisplayName()
        {
            string elem = Element.ToString();
            string type = Type switch
            {
                SpellType.Projectile => "Boule",
                SpellType.Homing => "Projectile guidé",
                SpellType.Area => "Explosion",
                SpellType.Rain => "Pluie",
                SpellType.Lightning => "Éclair",
                SpellType.Shield => "Bouclier",
                SpellType.Summon => "Invocation",
                SpellType.Burst => "Déflagration",
                _ => "Sort"
            };
            return $"{elem} {type}";
        }

        public Color GetElementColor()
        {
            return Element switch
            {
                SpellElement.Fire => new Color(255, 120, 20, 255),
                SpellElement.Water => new Color(60, 120, 255, 255),
                SpellElement.Earth => new Color(140, 100, 60, 255),
                SpellElement.Air => new Color(200, 230, 255, 255),
                SpellElement.Ice => new Color(180, 240, 255, 255),
                SpellElement.Lightning => new Color(255, 220, 50, 255),
                SpellElement.Poison => new Color(100, 200, 80, 255),
                SpellElement.Light => new Color(255, 255, 200, 255),
                SpellElement.Dark => new Color(100, 80, 180, 255),
                SpellElement.Metal => new Color(180, 180, 200, 255),
                _ => Color.White
            };
        }

        public Color GetParticleColor()
        {
            // Légère variation pour les particules
            var baseColor = GetElementColor();
            return new Color(
                (byte)Math.Clamp(baseColor.R + 30, 0, 255),
                (byte)Math.Clamp(baseColor.G + 30, 0, 255),
                (byte)Math.Clamp(baseColor.B + 30, 0, 255),
                (byte)200);
        }
    }
}