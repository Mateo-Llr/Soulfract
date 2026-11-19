// Shield.cs - Système de bouclier : parade directionnelle au clic droit
#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    /// <summary>
    /// Pose du bras de bouclier, lue par le rendu (EntityRenderer.DrawEntity).
    /// Tous les angles sont exprimés dans le repère "local au facing" des squelettes :
    /// 0 = bras qui pend, -90 = horizontal devant soi, -180 = vers le haut.
    /// </summary>
    public readonly struct ShieldPose
    {
        public readonly bool RightArm;     // true = bouclier en main principale (rarm*), false = main secondaire (larm*)
        public readonly float Raise;       // 0..1 : le bras est-il levé ? (sert à fondre depuis l'anim normale)
        public readonly float Elevation;   // degrés, + = on pare vers le HAUT de l'écran, - = vers le bas
        public readonly float HeadAngle;   // inclinaison de tête (+ = regarde vers le bas, comme headAngle)
        public readonly float Recoil;      // 0..1 : petit recul juste après un coup paré

        public ShieldPose(bool rightArm, float raise, float elevation, float headAngle, float recoil)
        {
            RightArm = rightArm;
            Raise = raise;
            Elevation = elevation;
            HeadAngle = headAngle;
            Recoil = recoil;
        }

        /// <summary>Raise adouci (smoothstep) pour que le bras ne démarre/finisse pas brusquement.</summary>
        public float EasedRaise => Raise * Raise * (3f - 2f * Raise);

        // Le coude garde un angle constant tandis que l'avant-bras suit l'élévation visée.
        public float TopArmRotation => -35f - Elevation;

        public float ForearmRotation => -55f;
    }

    public static partial class Program
    {
        // ─── RÉGLAGES ────────────────────────────────────────────────────────────
        private const float SHIELD_RAISE_TIME = 0.12f;            // temps pour lever / baisser le bouclier
        private const float SHIELD_ACTIVE_THRESHOLD = 0.6f;       // levée minimale pour que la parade compte
        private const float SHIELD_STAMINA_PER_DAMAGE = 1.5f;     // endurance perdue par point de dégât paré
        private const float SHIELD_GUARD_BREAK_LOCK = 1.2f;       // garde brisée : bouclier inutilisable ce temps
        private const float SHIELD_ATTACKER_HINT_WINDOW = 0.3f;   // validité (s) de l'indice "qui vient de m'attaquer"
        private const float SHIELD_ATTACKER_SEARCH_RADIUS = 220f; // rayon de recherche de l'attaquant (repli)

        /// <summary>Multiplicateur de vitesse de déplacement à appliquer pendant la garde (optionnel).</summary>
        public const float SHIELD_MOVE_SPEED_MULTIPLIER = 0.6f;

        // ─── ÉTAT ────────────────────────────────────────────────────────────────
        private static bool _shieldInput;                 // clic droit maintenu ET autorisé cette frame
        private static bool _shieldRightArm;
        private static float _shieldRaise;                // 0..1
        private static float _shieldRecoil;               // 0..1
        private static float _shieldBrokenTimer;
        private static float _shieldElevation;            // degrés, + = haut
        private static float _shieldHeadAngle;
        private static Vector2 _shieldAimDir = new(1f, 0f);
        private static bool _shieldBlockedThisFrame;

        // Indice posé par NotifyPlayerAttackedByEntity : position du dernier agresseur.
        private static Vector2 _shieldAttackerHintPos;
        private static string? _shieldAttackerHintName;
        private static double _shieldAttackerHintTime = -999.0;

        /// <summary>
        /// Vrai tant que le joueur tient son bouclier (clic droit enfoncé). À utiliser dans la boucle
        /// principale pour NE PAS déclencher les autres actions du clic droit (poser, utiliser, manger...)
        /// ni l'attaque au clic gauche pendant la garde.
        /// </summary>
        public static bool IsShieldBlocking => _shieldInput;

        /// <summary>Vrai quand le bouclier est suffisamment levé pour réellement parer.</summary>
        public static bool IsShieldGuardActive => _shieldInput && _shieldRaise >= SHIELD_ACTIVE_THRESHOLD;

        // ─── DÉTECTION DE L'ITEM ─────────────────────────────────────────────────
        public static bool IsShieldItem(Item? item)
        {
            if (item == null) return false;
            if (!GameData.TryGetItemByName(item.Name, out var data)) return false;
            string key = data.Key ?? "";
            string name = data.Name ?? "";
            return key.Contains("shield", StringComparison.OrdinalIgnoreCase)
                || name.Contains("shield", StringComparison.OrdinalIgnoreCase)
                || name.Contains("bouclier", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetEquippedShield(Equipment eq, out bool rightArm)
        {
            // Main secondaire en priorité (cas normal), sinon main principale.
            if (IsShieldItem(eq.OffHand)) { rightArm = false; return true; }
            if (IsShieldItem(eq.MainHand)) { rightArm = true; return true; }
            rightArm = false;
            return false;
        }

        // ─── MISE À JOUR (à appeler UNE fois par frame, voir les notes d'intégration) ───
        /// <param name="dt">Delta time.</param>
        /// <param name="canAct">
        /// Faux quand le joueur ne doit pas pouvoir agir (menu/inventaire ouvert, curseur sur l'UI,
        /// dialogue, conduite d'un véhicule, objet porté...).
        /// </param>
        public static void UpdateShield(float dt, bool canAct, ref float facing)
        {
            _shieldBlockedThisFrame = false;
            bool hasShield = TryGetEquippedShield(equipment, out bool rightArm);
            _shieldBrokenTimer = MathF.Max(0f, _shieldBrokenTimer - dt);
            _shieldRecoil = MathF.Max(0f, _shieldRecoil - dt * 6f);

            bool wantsBlock = hasShield && canAct && !IsPlayerRagdolled && _shieldBrokenTimer <= 0f
                && Raylib.IsMouseButtonDown(MouseButton.Right);
            _shieldInput = wantsBlock;

            if (hasShield)
            {
                _shieldRightArm = rightArm;

                // Direction visée : de la poitrine du joueur vers le curseur (même origine que le lancer d'objets).
                Vector2 playerPos = GetPlayerPosition();
                Vector2 mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), GetCurrentCamera());
                Vector2 toMouse = mouseWorld - GetPlayerAimOrigin(playerPos);

                if (wantsBlock && toMouse.LengthSquared() > 1f)
                {
                    _shieldAimDir = Vector2.Normalize(toMouse);

                    // Le personnage se tourne vers le curseur pendant la garde (comme avec une arme en main).
                    if (MathF.Abs(toMouse.X) > 0.001f)
                    {
                        facing = MathF.Sign(toMouse.X);
                        _playerFacing = facing;
                    }

                    float targetAngle = MathF.Atan2(_shieldAimDir.Y, _shieldAimDir.X) * 180f / MathF.PI;
                    float facingAngle = facing > 0f ? 0f : 180f;
                    float angleDiff = targetAngle - facingAngle;
                    if (angleDiff > 180f) angleDiff -= 360f;
                    if (angleDiff < -180f) angleDiff += 360f;
                    _shieldHeadAngle = Math.Clamp(angleDiff, -45f, 45f);
                    _shieldElevation = -_shieldHeadAngle * facing;
                }

            }

            float step = dt / SHIELD_RAISE_TIME;
            _shieldRaise = (wantsBlock && hasShield)
                ? MathF.Min(1f, _shieldRaise + step)
                : MathF.Max(0f, _shieldRaise - step);
            if (!hasShield) _shieldRaise = 0f;
        }

        /// <summary>Pose à appliquer au rendu pour CET Equipment (seul le joueur local en a une).</summary>
        public static bool TryGetShieldPose(Equipment? eq, out ShieldPose pose)
        {
            pose = default;
            if (eq == null || !ReferenceEquals(eq, equipment) || _shieldRaise <= 0.001f)
                return false;

            pose = new ShieldPose(_shieldRightArm, _shieldRaise, _shieldElevation, _shieldHeadAngle, _shieldRecoil);
            return true;
        }

        // ─── PARADE ──────────────────────────────────────────────────────────────
        /// <summary>
        /// Appelée par NotifyPlayerAttackedByEntity : mémorise la position du dernier agresseur pour que
        /// la parade connaisse la direction du coup même si Entity.cs ne la transmet pas explicitement.
        /// </summary>
        private static void RememberShieldAttacker(Vector2 attackerPos, string? attackerName)
        {
            _shieldAttackerHintPos = attackerPos;
            _shieldAttackerHintName = attackerName;
            _shieldAttackerHintTime = Raylib.GetTime();
        }

        private static Vector2? ResolveShieldAttackerPos(Vector2? explicitPos, string? sourceName)
        {
            if (explicitPos.HasValue) return explicitPos;

            if (!string.IsNullOrEmpty(sourceName))
            {
                Vector2 playerPos = GetPlayerPosition();
                float bestDistSq = SHIELD_ATTACKER_SEARCH_RADIUS * SHIELD_ATTACKER_SEARCH_RADIUS;
                Vector2? found = null;
                var entities = GetEntities();
                for (int i = 0; i < entities.Count; i++)
                {
                    var e = entities[i];
                    if (e == null || !e.IsAlive || e.IsPlayer) continue;
                    if (!string.Equals(e.GetDisplayName(), sourceName, StringComparison.Ordinal)) continue;
                    float d2 = Vector2.DistanceSquared(e.WorldPos, playerPos);
                    if (d2 < bestDistSq) { bestDistSq = d2; found = e.WorldPos; }
                }
                if (found.HasValue) return found;
            }

            bool matchingHint = string.IsNullOrEmpty(sourceName)
                || string.Equals(sourceName, _shieldAttackerHintName, StringComparison.Ordinal);
            return matchingHint && Raylib.GetTime() - _shieldAttackerHintTime <= SHIELD_ATTACKER_HINT_WINDOW
                ? _shieldAttackerHintPos
                : null;
        }

        /// <summary>
        /// Bloque un coup si sa provenance traverse le même cône que l'attaque de faux.
        /// </summary>
        private static int ApplyShieldBlock(
            int amount,
            Vector2? sourcePos,
            string? sourceName,
            bool consumeStamina = true)
        {
            if (amount <= 0 || !IsShieldGuardActive) return amount;

            Vector2? attacker = ResolveShieldAttackerPos(sourcePos, sourceName);
            if (!attacker.HasValue) return amount;

            Vector2 zoneOrigin = GetPlayerAttackOrigin(GetPlayerPosition());
            Vector2 toAttacker = attacker.Value - zoneOrigin;
            float distanceSquared = toAttacker.LengthSquared();
            if (distanceSquared < 1f) return amount;

            float distance = MathF.Sqrt(distanceSquared);
            float checkDistance = MathF.Min(distance, SCYTHE_ATTACK_RADIUS - 0.01f);
            Vector2 zoneIntersection = zoneOrigin + toAttacker * (checkDistance / distance);
            if (!IsPointInAttackCone(
                    zoneIntersection, zoneOrigin, _shieldAimDir, SCYTHE_ATTACK_RADIUS, ATTACK_ANGLE))
                return amount;

            int blocked = amount;

            if (!consumeStamina)
            {
                _shieldRecoil = 1f;
                _shieldBlockedThisFrame = true;
                return amount - blocked;
            }

            // Une parade coûte de l'endurance ; à court, la garde est brisée et le coup passe en entier.
            float cost = blocked * SHIELD_STAMINA_PER_DAMAGE;
            if (playerStamina < cost)
            {
                playerStamina = 0f;
                _shieldBrokenTimer = SHIELD_GUARD_BREAK_LOCK;
                _shieldInput = false;
                _shieldRecoil = 1f;
                return amount;
            }

            playerStamina -= cost;
            _shieldRecoil = 1f;
            _shieldBlockedThisFrame = true;
            return amount - blocked;
        }
    }
}
