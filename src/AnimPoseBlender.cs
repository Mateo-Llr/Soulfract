// AnimPoseBlender.cs
#nullable enable
using System;
using System.Numerics;

namespace Soulfract
{
    /// <summary>
    /// Lisse les changements brusques de pose d'un squelette : changement d'animation
    /// (walk -> idle, idle -> attack...), mais aussi passage en pose de portage, de visée
    /// ou de geste d'attaque procédural. Sans cela, chaque pièce "saute" d'un coup vers sa
    /// nouvelle position (les membres semblent se téléporter).
    ///
    /// PRINCIPE (offset blending / "inertialization") :
    ///   - à chaque frame, le renderer calcule la pose CIBLE de chaque pièce (offset X/Y + rotation),
    ///     exactement comme avant (interpolation de keyframes de l'animation courante) ;
    ///   - quand le "mode de pose" change (nom d'animation ou flags), on mémorise pour chaque
    ///     pièce l'écart  delta = (pose affichée à la frame précédente) - (nouvelle pose cible) ;
    ///   - pose affichée = cible + delta * poids, avec un poids qui décroît de 1 à 0 (smoothstep).
    ///
    /// Avantages par rapport à un simple fondu depuis une pose figée :
    ///   - la nouvelle animation continue de jouer à pleine vitesse pendant la transition
    ///     (pas de jambes qui "gèlent" puis rattrapent) ;
    ///   - continuité parfaite à t=0, y compris si la transition précédente n'était pas finie
    ///     (le point de départ est la pose réellement affichée, pas un état théorique) ;
    ///   - seules les pièces dont la pose a réellement changé sont corrigées (delta ~ 0 pour les autres).
    ///
    /// Le temps est mesuré en secondes réelles (horloge passée à Begin) : appeler Begin/Apply
    /// plusieurs fois dans la même frame pour la même entité ne fait donc pas avancer la
    /// transition plus vite.
    /// </summary>
    internal sealed class AnimPoseBlender
    {
        /// <summary>Au-delà de ce délai sans rendu (entité hors écran...), l'historique est périmé : pas de transition.</summary>
        public const double MaxGapSeconds = 0.25;

        //  Garde-fou contre une IA qui alterne deux animations à chaque frame : exiger la continuité
        // à CHAQUE changement figerait la pose (elle ne pourrait plus jamais bouger). On autorise donc
        // au plus MaxCapturesPerBurst captures dans une fenêtre de BurstWindowSeconds (assez pour
        // regrouper un vrai cas à deux changements quasi simultanés : nouvelle anim + nouveau flag),
        // puis on accepte temporairement la discontinuité, comme avant l'introduction du lissage.
        public const double BurstWindowSeconds = 0.05;
        public const int MaxCapturesPerBurst = 2;

        private Vector3[] _shown = Array.Empty<Vector3>();  // pose affichée à la dernière frame (X,Y = offset ; Z = rotation)
        private Vector3[] _delta = Array.Empty<Vector3>();  // écart résiduel à résorber, par pièce
        private string? _speciesKey;
        private string? _sigAnim;
        private int _sigFlags;
        private bool _hasHistory;
        private bool _hasShownPose;
        private bool _captureDelta;   // vrai uniquement pour la frame où le mode de pose vient de changer
        private float _captureScale = 1f; // échelle appliquée à l'écart capturé (voir Begin : prolongation d'une transition)
        private float _t = 1f;        // progression de la transition : 0 (début) -> 1 (terminée)
        private float _duration = 0.15f;
        private double _lastTime;
        private double _lastCaptureTime = double.NegativeInfinity;
        private int _burst;
        private float _weight;        // poids courant de l'écart résiduel : 1 -> 0

        /// <summary>Vrai tant qu'une transition est en cours (utile pour le debug).</summary>
        public bool IsBlending => _weight > 0f;

        /// <summary>Durée de transition selon l'animation de destination.</summary>
        public static float GetDuration(string anim) => anim switch
        {
            "attack" => 0.08f,   // ne doit pas retarder visuellement le geste d'attaque
            "sleep" or "sit" => 0.25f,
            _ => 0.15f
        };

        /// <summary>
        /// À appeler une fois par rendu d'entité, AVANT de calculer les poses des pièces.
        /// <paramref name="flags"/> : bits décrivant les poses procédurales actives (portage, visée,
        /// geste d'attaque...) ; tout changement de ces bits déclenche une transition comme un
        /// changement d'animation.
        /// </summary>
        public void Begin(string speciesKey, int partCount, string anim, int flags, double now, float duration)
        {
            bool stale = !_hasHistory
                || _shown.Length != partCount
                || !string.Equals(_speciesKey, speciesKey, StringComparison.Ordinal)
                || now < _lastTime
                || now - _lastTime > MaxGapSeconds;

            if (stale)
            {
                if (_shown.Length != partCount)
                {
                    _shown = new Vector3[partCount];
                    _delta = new Vector3[partCount];
                }
                _speciesKey = speciesKey;
                _sigAnim = anim;
                _sigFlags = flags;
                _hasHistory = true;
                _hasShownPose = false;
                _captureDelta = false;
                _t = 1f;
                _weight = 0f;
                _lastTime = now;
                _lastCaptureTime = double.NegativeInfinity;
                _burst = 0;
                return;
            }

            float elapsed = (float)(now - _lastTime);
            _lastTime = now;

            if (_t < 1f)
                _t = MathF.Min(1f, _t + elapsed / _duration);
            float w = SmoothWeight(_t);

            bool modeChanged = _sigFlags != flags || !string.Equals(_sigAnim, anim, StringComparison.Ordinal);
            _captureDelta = false;
            _captureScale = 1f;

            if (modeChanged)
            {
                _sigAnim = anim;
                _sigFlags = flags;

                if (now - _lastCaptureTime > BurstWindowSeconds)
                    _burst = 0;

                if (_burst < MaxCapturesPerBurst)
                {
                    _burst++;
                    _lastCaptureTime = now;
                    _captureDelta = true;

                    if (w > 0.5f)
                    {
                        //  Changement de mode pendant une transition encore jeune (ex : nouvelle anim
                        // puis nouveau flag à une frame d'écart) : on PROLONGE la transition en cours
                        // au lieu de la relancer. L'écart capturé est divisé par le poids courant pour
                        // que la pose affichée reste exactement continue.
                        _captureScale = 1f / w;
                    }
                    else
                    {
                        _t = 0f;
                        _duration = MathF.Max(0.01f, duration);
                        w = 1f;
                    }
                }
                // sinon : rafale de changements trop rapprochés -> on ne capture pas (voir MaxCapturesPerBurst)
            }

            _weight = w;
        }

        // smoothstep inversé : 1 au début, 0 à la fin, pente nulle aux deux extrémités.
        private static float SmoothWeight(float t) => t >= 1f ? 0f : 1f - t * t * (3f - 2f * t);

        /// <summary>
        /// Corrige la pose cible d'une pièce (offset + rotation, dans l'espace local indépendant
        /// de l'orientation, avant application du facing) et mémorise la pose finale pour la frame suivante.
        /// </summary>
        public void Apply(int index, ref Vector2 offset, ref float rotation)
        {
            if ((uint)index >= (uint)_shown.Length) return;

            if (_captureDelta)
                _delta[index] = (_shown[index] - new Vector3(offset.X, offset.Y, rotation)) * _captureScale;

            if (_weight > 0f)
            {
                Vector3 d = _delta[index] * _weight;
                offset = new Vector2(offset.X + d.X, offset.Y + d.Y);
                rotation += d.Z;
            }

            _shown[index] = new Vector3(offset.X, offset.Y, rotation);
            _hasShownPose = true;
        }

        public bool TryGetShown(int index, double now, out Vector2 offset, out float rotation)
        {
            if (_hasShownPose
                && (uint)index < (uint)_shown.Length
                && now >= _lastTime
                && now - _lastTime <= MaxGapSeconds)
            {
                Vector3 shown = _shown[index];
                offset = new Vector2(shown.X, shown.Y);
                rotation = shown.Z;
                return true;
            }

            offset = Vector2.Zero;
            rotation = 0f;
            return false;
        }
    }
}
