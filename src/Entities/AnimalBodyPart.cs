using Raylib_cs;
using System.Globalization;
using System.Numerics;

namespace Soulfract
{
    public class AnimalBodyPart
    {
        public string Name;
        public Vector2 BasePos;
        public float BaseRot;
        public string ParentName;
        public Texture2D Texture;
        public Dictionary<string, List<Vector3>> Animations = new();
        public Vector2 GlobalPos;
        public float GlobalRot;
        public float Scale;
        public float PositionScale = 1.0f;

        public AnimalBodyPart(string name, float x, float y, float rot, string parent, float scale = 1.0f)
        {
            Name = name;
            // Inverser Y : dans vos données Y positif = HAUT, Raylib Y positif = BAS
            BasePos = new Vector2(x, -y);
            BaseRot = rot;
            ParentName = parent;
            Scale = scale;
            PositionScale = scale;
        }

        public void AddAnimation(string animName, string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            
            var frames = data.Split('#');  // Séparation par #
            var list = new List<Vector3>();
            
            // Commencer à i=1 car le premier élément est le nom de la pièce
            for (int i = 1; i < frames.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(frames[i])) continue;
                var val = frames[i].Split('_');
                if (val.Length >= 3)
                {
                    // Utiliser InvariantCulture pour supporter les points décimaux
                    // quel que soit les paramètres régionaux de l'utilisateur
                    float x = float.Parse(val[0], CultureInfo.InvariantCulture);
                    float y = -float.Parse(val[1], CultureInfo.InvariantCulture);  // Inversion Y
                    float z = float.Parse(val[2], CultureInfo.InvariantCulture);
                    list.Add(new Vector3(x, y, z));
                }
                else if (val.Length == 1 && !string.IsNullOrEmpty(val[0]))
                {
                    // Format alternatif : une seule valeur (ex: "0" ou "0#")
                    // Dans ce cas, on considère que X=Y=Z= cette valeur
                    float v = float.Parse(val[0], CultureInfo.InvariantCulture);
                    list.Add(new Vector3(v, -v, v));
                }
            }
            
            //  IMPORTANT : S'assurer qu'il y a au moins 2 frames pour l'interpolation
            if (list.Count == 1)
            {
                // Dupliquer la seule frame pour éviter les erreurs d'interpolation
                list.Add(list[0]);
            }
            
            Animations[animName] = list;
        }
    }
}