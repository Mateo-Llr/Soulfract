// Ragdoll.cs - Ragdoll physique du joueur à la mort, basé sur le squelette existant.
//
//  Particularité importante du squelette de ce jeu : ce n'est PAS une vraie chaîne
// hiérarchique unique. Plusieurs parties (le corps, la tête, le haut de chaque bras/jambe)
// ont un "parent" vide dans les données ("skeleton" du species.json) : elles sont chacune
// ancrées indépendamment sur la position du joueur pour l'animation, et non reliées entre
// elles. Seuls les avant-bras/mollets ont un vrai parent (le haut du bras/de la jambe).
//
// Pour un ragdoll crédible, il faut donc reconstruire un vrai squelette physique :
// - "body" devient la racine (le tronc).
// - Les parties sans parent (tête, hauts de bras, hauts de jambes) sont rattachées
//   synthétiquement au tronc (comme des épaules/hanches/cou).
// - Les parties qui ont déjà un vrai parent (avant-bras, mollets) restent attachées à lui
//   (coude, genou).
// Chaque liaison est à la fois une contrainte de distance (l'os garde sa longueur, donc les
// membres restent physiquement attachés au corps au lieu de s'effondrer au même endroit) ET
// une contrainte angulaire (l'articulation ne peut se plier que dans une plage réaliste
// autour de son angle de repos, comme un vrai coude/genou/épaule).
using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Soulfract
{
    public static class PlayerRagdoll
    {
        private class Node
        {
            public string Name = "";
            public Vector2 Pos;
            public Vector2 PrevPos;
            public Texture2D Texture;
            public float TexWidth;
            public float TexHeight;
            public float Rotation;
            public bool IsVirtual; // particule invisible, sert uniquement de repère d'orientation

            // Masse relative : plus c'est lourd, moins ça bouge quand une contrainte est résolue.
            public float Weight = 1.5f;

            // Liaison ("os") vers le parent physique. Peut différer du parent d'animation
            // d'origine (voir note en tête de fichier).
            public Node? PhysicsParent;
            public float RestLength;

            // Contrainte angulaire = l'articulation elle-même.
            public bool HasAngleLimit;
            public float RestRelativeAngle; // angle de repos (pose au moment de la mort), en degrés
            public float MaxBendDegrees;    // débattement autorisé autour de cet angle de repos

            // Décalage entre l'orientation physique de "l'os" (direction parent -> nœud) et la
            // rotation réelle à appliquer à la texture. Calculé une fois à Start() à partir de la
            // pose cinématique d'origine (qui, elle, est forcément correcte car identique au rendu
            // normal). Évite de supposer un décalage fixe (ex : +90°) qui ne correspond pas
            // forcément à l'orientation de chaque texture/partie.
            public float RotationOffset;
        }

        // Doit correspondre à EntityRenderer.BASE_ENTITY_SCALE pour que la pose initiale
        // du ragdoll s'aligne exactement sur le rendu normal du joueur au moment de sa mort.
        private const float SPECIES_SCALE = 2.8f;
        private const float GRAVITY = 1400f;
        private const int CONSTRAINT_ITERATIONS = 8;
        private const float AIR_DAMPING = 0.985f;
        private const float RAD2DEG = 180f / MathF.PI;

        private static readonly List<Node> _nodes = new();
        private static bool _active = false;
        private static float _floorY = 0f;
        private static string _rootName = "body";

        public static bool IsActive => _active;

        /// <summary>
        /// Démarre le ragdoll à partir de la pose actuelle du joueur (position, animation en cours),
        /// pour que la transition mort/ragdoll soit fluide plutôt qu'un "pop" visuel.
        /// </summary>
        public static void Start(Vector2 playerVisualPos, float facing, string animState, int animFrame, float animProg, string species = "human")
        {
            _nodes.Clear();
            _active = false;

            if (!SpeciesData.Skeletons.TryGetValue(species, out var skeleton) || skeleton.Count == 0)
                return;

            // ---------- 1) Pose de départ : forward-kinematics identique au rendu normal ----------
            // (calcul récursif, indépendant de l'ordre des parties dans la liste, comme EntityRenderer)
            float speciesScale = SPECIES_SCALE;
            if (SpeciesData.Species.TryGetValue(species.ToLowerInvariant(), out var speciesInfo))
                speciesScale *= speciesInfo.Scale;

            var byName = new Dictionary<string, AnimalBodyPart>();
            foreach (var part in skeleton) byName[part.Name] = part;

            var kinPos = new Dictionary<string, Vector2>();
            var kinRot = new Dictionary<string, float>();
            var kinComputed = new HashSet<string>();

            void ComputeKinematics(string name)
            {
                if (kinComputed.Contains(name) || !byName.TryGetValue(name, out var part)) return;

                Vector2 animOffset = Vector2.Zero;
                float animRot = 0f;
                if (part.Animations != null && part.Animations.TryGetValue(animState, out var frames) && frames.Count > 0)
                {
                    int safeFrame = ((animFrame % frames.Count) + frames.Count) % frames.Count;
                    int ni = (safeFrame + 1) % frames.Count;
                    animOffset = Vector2.Lerp(
                        new Vector2(frames[safeFrame].X, frames[safeFrame].Y),
                        new Vector2(frames[ni].X, frames[ni].Y),
                        animProg) * speciesScale;
                    animRot = Raymath.Lerp(frames[safeFrame].Z, frames[ni].Z, animProg);
                }

                Vector2 fb = new(part.BasePos.X * speciesScale, part.BasePos.Y * speciesScale);
                Vector2 fa = new(animOffset.X * facing, animOffset.Y);
                Vector2 finalOffset = fb + fa;
                if (facing < 0)
                    finalOffset = new Vector2(-finalOffset.X, finalOffset.Y);

                if (!string.IsNullOrEmpty(part.ParentName) && byName.ContainsKey(part.ParentName))
                {
                    ComputeKinematics(part.ParentName);
                    float parentRot = kinRot[part.ParentName];
                    Vector2 parentPos = kinPos[part.ParentName];
                    kinRot[name] = parentRot + (part.BaseRot + animRot) * facing;
                    kinPos[name] = parentPos + RotateVec(finalOffset, parentRot);
                }
                else
                {
                    kinPos[name] = playerVisualPos + finalOffset;
                    kinRot[name] = (part.BaseRot + animRot) * facing;
                }
                kinComputed.Add(name);
            }

            foreach (var part in skeleton) ComputeKinematics(part.Name);

            // ---------- 2) Détermination de la racine physique du ragdoll ----------
            _rootName = byName.ContainsKey("body") ? "body" : "";
            if (_rootName == "")
            {
                foreach (var part in skeleton)
                {
                    if (string.IsNullOrEmpty(part.ParentName)) { _rootName = part.Name; break; }
                }
                if (_rootName == "") _rootName = skeleton[0].Name;
            }

            // ---------- 3) Création des particules (une par partie du squelette) ----------
            var nodeByName = new Dictionary<string, Node>();
            foreach (var part in skeleton)
            {
                var node = new Node
                {
                    Name = part.Name,
                    Pos = kinPos[part.Name],
                    PrevPos = kinPos[part.Name],
                    Texture = part.Texture,
                    TexWidth = part.Texture.Id != 0 ? part.Texture.Width : 0,
                    TexHeight = part.Texture.Id != 0 ? part.Texture.Height : 0,
                    Rotation = kinRot[part.Name]
                };
                (node.Weight, node.MaxBendDegrees) = GetJointProfile(part.Name, _rootName);
                _nodes.Add(node);
                nodeByName[part.Name] = node;
            }

            var rootNode = nodeByName[_rootName];
            rootNode.Weight = 4.5f; // le tronc est la partie la plus "lourde" : elle bouge le moins

            // Particule virtuelle au-dessus de la racine : sert uniquement de repère d'orientation
            // dynamique pour le tronc (comme si on suivait la colonne vertébrale). Elle tombe et
            // tournoie sous la gravité comme les autres, ce qui donne au tronc une bascule naturelle.
            var rootRef = new Node
            {
                Name = "__rootRef",
                IsVirtual = true,
                Weight = 1.5f,
            };
            Vector2 upDir = RotateVec(new Vector2(0, -40f), rootNode.Rotation);
            rootRef.Pos = rootNode.Pos + upDir;
            rootRef.PrevPos = rootRef.Pos;
            _nodes.Add(rootRef);

            rootNode.PhysicsParent = rootRef;
            rootNode.HasAngleLimit = false; // le tronc oriente librement (voir rootRef ci-dessus)

            // ---------- 4) Reconstruction des liaisons physiques (les vraies "articulations") ----------
            foreach (var part in skeleton)
            {
                if (part.Name == _rootName) continue;
                var node = nodeByName[part.Name];

                Node? physicsParent;
                if (!string.IsNullOrEmpty(part.ParentName) && nodeByName.TryGetValue(part.ParentName, out var trueParent))
                {
                    // Vrai parent d'animation existant (ex : avant-bras -> haut du bras) : coude/genou.
                    physicsParent = trueParent;
                }
                else
                {
                    // Pas de parent d'animation (ex : tête, haut de bras/jambe) : on la rattache
                    // synthétiquement au tronc, comme une épaule/hanche/cou.
                    physicsParent = rootNode;
                }

                node.PhysicsParent = physicsParent;
                node.HasAngleLimit = true;
            }

            // ---------- 5) Longueurs de repos + angles de repos (pose au moment de la mort) ----------
            foreach (var node in _nodes)
            {
                if (node.PhysicsParent == null) continue;
                node.RestLength = Vector2.Distance(node.Pos, node.PhysicsParent.Pos);
                if (node.RestLength < 1f) node.RestLength = 1f; // évite les os de longueur nulle

                if (node.HasAngleLimit)
                {
                    Vector2 refDir = GetSegmentDir(node.PhysicsParent);
                    Vector2 curDir = node.Pos - node.PhysicsParent.Pos;
                    node.RestRelativeAngle = WrapAngle(AngleOf(curDir) - AngleOf(refDir));
                }

                // Le décalage texture <-> direction physique est déduit de la pose de départ :
                // à cet instant, node.Rotation (= kinRot, la rotation "vraie" du rendu normal) et
                // la direction géométrique parent -> nœud sont toutes les deux connues et cohérentes.
                // On fige leur écart pour pouvoir reconstruire la bonne rotation à chaque frame de
                // simulation, quelle que soit la convention d'orientation de la texture de la partie.
                Vector2 boneDir = node.Pos - node.PhysicsParent.Pos;
                node.RotationOffset = boneDir.LengthSquared() > 0.0001f
                    ? WrapAngle(node.Rotation - AngleOf(boneDir))
                    : node.Rotation;
            }

            // Sol approximatif = niveau des pieds au moment de la mort
            _floorY = playerVisualPos.Y + Program.FeetOffsetY;

            // Petite impulsion aléatoire pour que la chute ne soit jamais deux fois identique
            var rand = new Random();
            foreach (var node in _nodes)
            {
                Vector2 kick = new(
                    (float)(rand.NextDouble() - 0.5) * 60f,
                    (float)(rand.NextDouble()) * -40f);
                node.PrevPos -= kick * 0.016f;
            }

            _active = true;
        }

        public static void Update(float dt)
        {
            if (!_active || _nodes.Count == 0) return;
            dt = Math.Min(dt, 1f / 30f); // évite l'explosion de la simulation en cas de lag

            // --- Intégration de Verlet + gravité ---
            foreach (var node in _nodes)
            {
                Vector2 velocity = (node.Pos - node.PrevPos) * AIR_DAMPING;
                Vector2 newPos = node.Pos + velocity + new Vector2(0, GRAVITY) * dt * dt;
                node.PrevPos = node.Pos;
                node.Pos = newPos;
            }

            // --- Résolution itérative : distance (les os gardent leur longueur), puis angle
            //     (les articulations restent dans une plage réaliste), puis collision au sol ---
            for (int iter = 0; iter < CONSTRAINT_ITERATIONS; iter++)
            {
                foreach (var node in _nodes)
                {
                    if (node.PhysicsParent == null) continue;
                    var parent = node.PhysicsParent;

                    Vector2 delta = node.Pos - parent.Pos;
                    float dist = delta.Length();
                    if (dist < 0.0001f) continue;

                    float diff = (dist - node.RestLength) / dist;
                    Vector2 correction = delta * diff;

                    float totalWeight = node.Weight + parent.Weight;
                    float parentMove = node.Weight / totalWeight;
                    float childMove = parent.Weight / totalWeight;

                    parent.Pos += correction * parentMove;
                    node.Pos -= correction * childMove;
                }

                foreach (var node in _nodes)
                {
                    if (!node.HasAngleLimit || node.PhysicsParent == null) continue;
                    ApplyAngleConstraint(node);
                }

                foreach (var node in _nodes)
                {
                    if (node.Pos.Y > _floorY)
                    {
                        node.Pos.Y = _floorY;
                        // Stoppe la composante verticale de la vélocité Verlet et applique
                        // une friction horizontale au contact du sol.
                        node.PrevPos.Y = node.Pos.Y;
                        node.PrevPos.X += (node.Pos.X - node.PrevPos.X) * 0.15f;
                    }
                }
            }

            // --- Rotation visuelle de chaque partie, à partir de sa propre orientation physique ---
            foreach (var node in _nodes)
            {
                if (node.IsVirtual || node.PhysicsParent == null) continue;
                Vector2 dir = node.Pos - node.PhysicsParent.Pos;
                if (dir.LengthSquared() > 0.5f)
                    node.Rotation = AngleOf(dir) + node.RotationOffset;
            }
        }

        /// <summary>
        /// Contraint l'angle du membre par rapport à l'orientation ACTUELLE de son parent
        /// (et non par rapport au monde), exactement comme une vraie articulation : un coude
        /// se plie relativement au bras, pas relativement à l'écran.
        /// </summary>
        private static void ApplyAngleConstraint(Node node)
        {
            var parent = node.PhysicsParent!;
            Vector2 refDir = GetSegmentDir(parent);
            Vector2 curDir = node.Pos - parent.Pos;
            if (curDir.LengthSquared() < 0.0001f) return;

            float relative = WrapAngle(AngleOf(curDir) - AngleOf(refDir));

            // Écart par rapport à l'angle de repos, lui-même ramené dans [-180, 180] AVANT le clamp.
            // C'est essentiel : si on clampait "relative" directement entre
            // (RestRelativeAngle - MaxBend) et (RestRelativeAngle + MaxBend), ces bornes peuvent
            // dépasser 180° / -180° dès que RestRelativeAngle est proche de ±180 (bras/jambe un peu
            // tordu au moment de la mort, ce qui est fréquent). "relative" (issu de WrapAngle) ne
            // peut lui jamais dépasser 180°, donc le clamp se retrouvait à comparer des angles hors
            // de leur référentiel commun : une articulation à peine hors limite pouvait recevoir une
            // correction de 200-300° d'un coup, ce qui "catapultait" le membre (et tout ce qui est
            // accroché derrière lui) — c'était la cause du ragdoll qui s'envole aléatoirement.
            float delta = WrapAngle(relative - node.RestRelativeAngle);

            if (delta < -node.MaxBendDegrees || delta > node.MaxBendDegrees)
            {
                float clampedDelta = Math.Clamp(delta, -node.MaxBendDegrees, node.MaxBendDegrees);
                float correctionAngle = clampedDelta - delta;
                node.Pos = parent.Pos + RotateVec(curDir, correctionAngle);
            }
        }

        /// <summary>Direction de "l'os" que représente ce nœud (de son parent physique vers lui).</summary>
        private static Vector2 GetSegmentDir(Node n)
        {
            if (n.PhysicsParent == null) return new Vector2(0, -1);
            Vector2 d = n.Pos - n.PhysicsParent.Pos;
            return d.LengthSquared() > 0.0001f ? d : new Vector2(0, -1);
        }

        /// <summary>
        /// Poids relatif et débattement angulaire (en degrés) par type d'articulation.
        /// Réglé pour le squelette humain ("body" = tronc), avec un repli raisonnable pour
        /// les autres espèces qui utilisent un nom de partie différent.
        /// </summary>
        private static (float weight, float maxBend) GetJointProfile(string partName, string rootName)
        {
            if (partName == "head") return (1.5f, 50f);
            if (partName.Contains("bottom")) return (1f, 75f);   // avant-bras / mollet : coude / genou
            if (partName.Contains("top")) return (2f, 100f);     // haut de bras / de jambe : épaule / hanche
            if (partName == rootName) return (4.5f, 0f);         // tronc (racine)
            return (1.5f, 80f);                                  // repli par défaut
        }

        /// <summary>
        /// Position approximative du corps (pour que la caméra suive le ragdoll qui tombe).
        /// </summary>
        public static Vector2 GetBodyPosition()
        {
            if (!_active || _nodes.Count == 0) return Vector2.Zero;
            foreach (var node in _nodes)
                if (node.Name == _rootName) return node.Pos;
            return _nodes[0].Pos;
        }

        public static void Draw(Color skinTint)
        {
            if (!_active) return;

            foreach (var node in _nodes)
            {
                if (node.IsVirtual || node.Texture.Id == 0) continue;

                bool isSkinPart = node.Name == "head" || node.Name.Contains("arm");
                Color tint = isSkinPart ? skinTint : Color.White;

                Rectangle src = new(0, 0, node.TexWidth, node.TexHeight);
                Rectangle dest = new(node.Pos.X, node.Pos.Y, node.TexWidth * SPECIES_SCALE, node.TexHeight * SPECIES_SCALE);
                Vector2 origin = new(dest.Width / 2f, dest.Height / 2f);
                Raylib.DrawTexturePro(node.Texture, src, dest, origin, node.Rotation, tint);
            }
        }

        public static void Reset()
        {
            _active = false;
            _nodes.Clear();
        }

        private static float AngleOf(Vector2 v) => MathF.Atan2(v.Y, v.X) * RAD2DEG;

        private static float WrapAngle(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            if (deg < -180f) deg += 360f;
            return deg;
        }

        private static Vector2 RotateVec(Vector2 v, float angle)
        {
            float r = angle * MathF.PI / 180f;
            return new Vector2(
                v.X * MathF.Cos(r) - v.Y * MathF.Sin(r),
                v.X * MathF.Sin(r) + v.Y * MathF.Cos(r));
        }
    }
}
