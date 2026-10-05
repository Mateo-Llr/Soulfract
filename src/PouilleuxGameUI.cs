#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class CartesGameUI
    {
        private enum CardMode
        {
            Selection,
            Pouilleux,
            Solitaire,
            Chess,
            Poker
        }

        private sealed class Participant
        {
            public required Entity Npc;
            public int NetworkConnectionId = -1;
            public readonly List<int> Cards = new();
            public bool IsSpectator;
        }

        private sealed class CardParticle
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public float Size;
            public float Rotation;
            public float RotationSpeed;
            public float Life;
            public float MaxLife;
            public Color Color;
        }

        private sealed class DealVisualCard
        {
            public required Participant Owner;
            public int Rank;
            public Vector2 Position;
            public Vector2 StartPosition;
            public Vector2 TargetPosition;
        }

        private enum TablePhase
        {
            FormingCircle,
            Dealing,
            WaitingForPlayer,
            WaitingForRemotePlayer,
            PlayerTakingCard,
            WaitingForNpc,
            NpcThinking,
            NpcTakingCard,
            NpcShuffling,
            MovingCard,
            ReceivingCard,
            ExplodingPair,
            ReflowingAfterPair,
            Finished
        }

        private enum PokerPhase
        {
            Selecting,
            Result
        }

        private static readonly Participant _player = new() { Npc = null! };
        private static readonly List<Participant> _participants = new();
        private static readonly List<Entity> _availableNpcs = new();
        private static readonly List<CardParticle> _cardParticles = new();
        private static readonly List<DealVisualCard> _dealVisualCards = new();
        private static Entity? _spokenNpc;
        private static TablePhase _phase;
        private static int _currentTurn;
        private static float _phaseTimer;
        private static float _turnArrowAngle;
        private static float _turnArrowTargetAngle;
        private static float _resultFade;
        private static float _moveDuration;
        private static int _movingCard;
        private static Participant? _moveSource;
        private static Participant? _moveTarget;
        private static Vector2 _moveStart;
        private static Vector2 _moveEnd;
        private static Participant? _reflowParticipant;
        private static int _reflowRemovedIndex;
        private static int _reflowOldCount;
        private static Participant? _receiveParticipant;
        private static int _receiveOldCount;
        private static int _receiveInsertIndex;
        private static float _receiveDuration;
        private static int _explodingRank = -1;
        private static Vector2 _explosionCenter;
        private static Participant? _pairReflowParticipant;
        private static int _pairReflowFirstIndex;
        private static int _pairReflowSecondIndex;
        private static int _pairReflowOldCount;
        private const float PAIR_REFLOW_DURATION = 0.35f;
        private static Participant? _thinkingNpc;
        private static int _thinkingCard = -1;
        private static float _thinkingChangeTimer;
        private static float _thinkingDuration;
        private static Vector2 _thinkingHandStart;
        private static Vector2 _thinkingHandEnd;
        private static float _thinkingMoveTimer;
        private static float _thinkingMoveDuration;
        private static float _dealTimer;
        private const float DEAL_DURATION = 3.05f;
        private const float DEAL_INTERVAL = 0.075f;
        private const float DEAL_FLIGHT_DURATION = 0.42f;
        private static readonly List<int> _dealingDeck = new();
        private static int _dealtCardCount;
        private static float _dealReflowTimer;
        private const float DEAL_REFLOW_DURATION = 0.28f;
        private static bool _resolvingInitialPairs;
        private static float _playerTakeTimer;
        private static Participant? _pendingSource;
        private static Participant? _pendingTarget;
        private static int _pendingCardIndex;
        private static bool _playerIsSpectator;
        private static bool _gameStarted;
        private static bool _draggingPlayerCard;
        private static int _draggedPlayerCardIndex = -1;
        private static Vector2 _draggedPlayerCardPosition;
        private static Participant? _shufflingNpc;
        private static int _shuffleFirstIndex;
        private static int _shuffleSecondIndex;
        private static float _shuffleTimer;
        private static float _shuffleDuration;
        private static bool _shuffleTriggered;
        private static string _status = "";
        private static string _result = "";
        private static string? _networkSessionId;
        private static int _networkOwnerConnectionId = -1;
        private static Rectangle _closeRect;
        private static Rectangle _playRect;
        private static Rectangle _pouilleuxModeRect;
        private static Rectangle _solitaireModeRect;
        private static Rectangle _chessModeRect;
        private static Rectangle _pokerModeRect;
        private static Rectangle _pouilleuxModeHelpRect;
        private static Rectangle _solitaireModeHelpRect;
        private static Rectangle _chessModeHelpRect;
        private static Rectangle _pokerModeHelpRect;
        private static Rectangle _pokerDrawRect;
        private static Rectangle _pokerFoldRect;
        private static Rectangle _pokerRestartRect;
        private static Rectangle _solitaireStockRect;
        private static Rectangle _solitaireWasteRect;
        private static Rectangle _solitaireRestartRect;
        private static Rectangle _helpRect;
        private static Rectangle _rulesCloseRect;
        private static bool _showRules;
        private static bool _rulesForSolitaire;
        private static bool _rulesForChess;
        private static bool _rulesForPoker;
        private static bool _isOpen;
        private static CardMode _cardMode;
        private static readonly List<List<int>> _solitaireTableau = new();
        private static readonly List<int> _solitaireStock = new();
        private static readonly List<int> _solitaireWaste = new();
        private static readonly int[] _solitaireFoundations = new int[4];
        private static readonly int[] _solitaireFoundationSuits = new int[4];
        private static int _solitaireSelectedColumn = -1;
        private static int _solitaireSelectedCard = -1;
        private static bool _solitaireDragging;
        private static bool _solitaireWon;
        private static string _solitaireStatus = "Retourne toutes les cartes pour gagner.";
        private static readonly List<int> _pokerPlayerHand = new();
        private static readonly List<int> _pokerOpponentHand = new();
        private static readonly List<int> _pokerDeck = new();
        private static readonly bool[] _pokerSelected = new bool[5];
        private static PokerPhase _pokerPhase;
        private static int _pokerPlayerChips;
        private static int _pokerOpponentChips;
        private static int _pokerPot;
        private static string _pokerStatus = "Choisis jusqu'a trois cartes a echanger.";
        private static string _pokerResult = "";
        private static bool _cardTexturesLoaded;
        private static Texture2D _cardBase;
        private static Texture2D _cardBack;
        private static Texture2D _cardJoker;
        private static Texture2D _chessBlackPawn;
        private static readonly Dictionary<string, Texture2D> _pokerTokens = new();
        private static Texture2D _turnArrowTexture;
        private static readonly Dictionary<string, Texture2D> _cardSymbols = new();
        private static readonly Dictionary<string, Texture2D> _cardSmallSymbols = new();
        private static readonly Dictionary<string, Texture2D> _cardAces = new();
        private static readonly Dictionary<string, Texture2D> _cardFaces = new();
        private static readonly Dictionary<string, Texture2D> _handTextures = new();

        private static readonly string[] CardSuits = { "clubs", "diamonds", "hearts", "spades" };
        private const float CARD_TEXTURE_SCALE = 3f;
        private const float NPC_CARD_TEXTURE_SCALE = 2f;
        private const float ACTIVE_NPC_CARD_TEXTURE_SCALE = 2.7f;
        private const int JOKER_CARD_RANK = 99;
        private const int KING_OF_HEARTS_CARD_RANK = 38;

        public static bool IsOpen => _isOpen;
        public static bool IsNetworkSession => _isOpen && !string.IsNullOrEmpty(_networkSessionId);

        public static bool ShouldBlockPlayerMovement()
        {
            return _isOpen && _phase != TablePhase.WaitingForPlayer;
        }

        public static int GetPlayingMemberCount()
        {
            return 1 + _participants.Count;
        }

        private static void EnsureCardTexturesLoaded()
        {
            if (_cardTexturesLoaded) return;
            _cardTexturesLoaded = true;

            _cardBase = LoadCardTexture("assets/gui/card_base.png");
            _cardBack = LoadCardTexture("assets/gui/card_back.png");
            _cardJoker = LoadCardTexture("assets/gui/card_joker.png");
            _chessBlackPawn = LoadCardTexture("assets/gui/chess_b_pawn.png");
            foreach (string tokenColor in new[] { "black", "blue", "green", "red", "white", "yellow" })
                _pokerTokens[tokenColor] = LoadCardTexture($"assets/gui/poker_token_{tokenColor}.png");
            _turnArrowTexture = LoadCardTexture("assets/gui/arrow.png");
            foreach (string side in new[] { "palm", "back" })
            {
                foreach (string action in new[] { "point", "grab", "pick" })
                    _handTextures[$"{side}_{action}"] = LoadCardTexture($"assets/gui/hand_{side}_{action}.png");
            }
            foreach (string suit in CardSuits)
            {
                _cardSymbols[suit] = LoadCardTexture($"assets/gui/card_{suit}.png");
                _cardSmallSymbols[suit] = LoadCardTexture($"assets/gui/card_{suit}_small.png");
                _cardAces[suit] = LoadCardTexture($"assets/gui/card_ace_{suit}.png");
                foreach (string face in new[] { "jack", "queen", "king" })
                    _cardFaces[$"{face}_{suit}"] = LoadCardTexture($"assets/gui/card_{face}_{suit}.png");
            }
        }

        private static Texture2D LoadCardTexture(string path)
        {
            return File.Exists(path) ? Raylib.LoadTexture(path) : new Texture2D();
        }

        public static void Open(Entity spokenNpc, Entity otherNpc) => Open((Entity?)spokenNpc, new[] { otherNpc });

        public static void OpenStandaloneCards()
        {
            Open(null, Array.Empty<Entity>());
        }

        private static void OpenChessMode()
        {
            _cardMode = CardMode.Chess;
            ChessGameUI.Open();
        }

        public static void OpenPouilleuxDirect(Entity spokenNpc)
        {
            Open(spokenNpc, Array.Empty<Entity>());
            _cardMode = CardMode.Pouilleux;
        }

        public static void Open(Entity? spokenNpc, IEnumerable<Entity> otherNpcs)
        {
            EnsureCardTexturesLoaded();
            Close();
            _networkSessionId = null;
            _networkOwnerConnectionId = -1;
            _cardMode = CardMode.Selection;
            _cardParticles.Clear();
            _spokenNpc = spokenNpc;
            _participants.Clear();
            _availableNpcs.Clear();
            _player.Cards.Clear();
            _playerIsSpectator = false;
            _gameStarted = false;
            if (spokenNpc != null)
            {
                spokenNpc.ReleaseDialogueFreeze();
                spokenNpc.AiState = NpcAiState.WalkToTarget;
                spokenNpc.AiTarget = Program.GetPlayerPosition();
                spokenNpc.TryClaimAiPriority(Entity.NpcPriority.Play);
                _participants.Add(new Participant { Npc = spokenNpc });
            }
            foreach (Entity npc in otherNpcs.Distinct())
            {
                if (npc == spokenNpc) continue;
                npc.ReleaseDialogueFreeze();
                npc.ResumeMovementAfterDialogue();
                npc.AiState = NpcAiState.Idle;
                npc.AiTarget = npc.WorldPos;
                _availableNpcs.Add(npc);
            }
            Vector2 center = Program.GetPlayerPosition();
            if (spokenNpc != null)
                spokenNpc.WorldPos = center + new Vector2(48f, 0f);
            for (int i = 0; i < _participants.Count; i++)
            {
                float angle = MathF.PI + i * MathF.PI / Math.Max(1, _participants.Count - 1);
                _participants[i].Npc.WorldPos = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 72f;
            }

            var deck = new List<int>();
            for (int rank = 0; rank < 16; rank++)
            {
                deck.Add(rank);
                deck.Add(rank);
            }
            deck.Add(99);
            Shuffle(deck);
            _dealingDeck.Clear();
            _dealingDeck.AddRange(deck);
            _dealtCardCount = 0;
            _dealReflowTimer = DEAL_REFLOW_DURATION;
            _resolvingInitialPairs = false;
            _dealVisualCards.Clear();
            _player.Cards.Clear();
            foreach (Participant participant in _participants)
            {
                participant.Cards.Clear();
                participant.IsSpectator = false;
            }

            _currentTurn = 0;
            _turnArrowAngle = -MathF.PI / 2f;
            _turnArrowTargetAngle = _turnArrowAngle;
            _phase = TablePhase.WaitingForPlayer;
            _phaseTimer = 0f;
            _dealTimer = 0f;
            _result = "";
            _resultFade = 0f;
            _status = spokenNpc == null
                ? "Choisis un jeu, ou invite un PNJ pour jouer au Pouilleux."
                : "Choisis quand lancer la partie.";
            _isOpen = true;
            foreach (Participant participant in _participants)
            {
                participant.Npc.ReleaseDialogueFreeze();
                participant.Npc.ResumeMovementAfterDialogue();
                participant.Npc.TryClaimAiPriority(Entity.NpcPriority.Play);
                participant.Npc.AiState = NpcAiState.Follow;
                participant.Npc.AiTarget = center;
            }
            UIManager.PushUI(Close, () => IsOpen);
        }

        public static void OpenNetwork(Entity remoteParticipant, string sessionId, int otherConnectionId, int ownerConnectionId)
        {
            Open(remoteParticipant, Array.Empty<Entity>());
            _cardMode = CardMode.Pouilleux;
            _networkSessionId = sessionId;
            _networkOwnerConnectionId = ownerConnectionId;
            _participants[0].NetworkConnectionId = otherConnectionId;
        }

        public static void StartNetworkGame()
        {
            if (!_isOpen || string.IsNullOrEmpty(_networkSessionId) || _participants.Count == 0)
                return;
            BeginCircleFormation();
        }

        public static void HandleNetworkStartRequest(int connectionId, PouilleuxStartRequestMsg request)
        {
            if (!IsNetworkSession || !string.Equals(request.SessionId, _networkSessionId, StringComparison.Ordinal)) return;
            if (connectionId != _networkOwnerConnectionId) return;
            BeginCircleFormation();
        }

        public static PouilleuxStateMsg BuildNetworkState()
        {
            var state = new PouilleuxStateMsg
            {
                SessionId = _networkSessionId ?? "",
                CurrentOwnerConnectionId = _currentTurn == 0
                    ? 0
                    : _participants[Math.Clamp(_currentTurn - 1, 0, _participants.Count - 1)].NetworkConnectionId,
                Phase = (int)_phase,
                Status = _status,
                Result = _result,
                DealingDeck = _dealingDeck.ToList(),
                TransferSourceConnectionId = GetNetworkConnectionId(_moveSource),
                TransferTargetConnectionId = GetNetworkConnectionId(_moveTarget),
                TransferCardIndex = _reflowRemovedIndex,
                TransferCard = _movingCard,
                TransferTargetIndex = _receiveInsertIndex,
                AnimationRank = _explodingRank,
                //  Nécessaire pour rejouer l'animation de mélange chez le client (voir
                // ApplyNetworkState) : contrairement à NpcThinking/NpcTakingCard, cette
                // animation ne dépend pas de la main de l'hôte, donc on peut la répliquer
                // fidèlement au lieu de la masquer.
                ShuffleParticipantConnectionId = GetNetworkConnectionId(_shufflingNpc),
                ShuffleFirstIndex = _shuffleFirstIndex,
                ShuffleSecondIndex = _shuffleSecondIndex,
                ShuffleDuration = _shuffleDuration
            };
            state.Participants.Add(new PouilleuxParticipantStateMsg
            {
                ConnectionId = 0,
                Name = Program.LocalPlayerName,
                Cards = _player.Cards.ToList(),
                IsSpectator = _playerIsSpectator
            });
            state.Participants.AddRange(_participants.Select(participant => new PouilleuxParticipantStateMsg
            {
                ConnectionId = participant.NetworkConnectionId,
                Name = participant.Npc.DisplayName ?? "Joueur",
                Cards = participant.Cards.ToList(),
                IsSpectator = participant.IsSpectator
            }));
            return state;
        }

        public static void ApplyNetworkState(PouilleuxStateMsg state)
        {
            if (!IsNetworkSession || !string.Equals(state.SessionId, _networkSessionId, StringComparison.Ordinal)) return;
            int localConnectionId = NetworkManager.LocalConnectionId;
            PouilleuxParticipantStateMsg? localState = state.Participants.FirstOrDefault(item => item.ConnectionId == localConnectionId);
            if (localState != null)
            {
                _player.Cards.Clear();
                _player.Cards.AddRange(localState.Cards);
                _playerIsSpectator = localState.IsSpectator;
            }
            foreach (Participant participant in _participants)
            {
                PouilleuxParticipantStateMsg? participantState = state.Participants.FirstOrDefault(item => item.ConnectionId == participant.NetworkConnectionId);
                if (participantState == null) continue;
                participant.Npc.FirstName = participantState.Name;
                participant.Cards.Clear();
                participant.Cards.AddRange(participantState.Cards);
                participant.IsSpectator = participantState.IsSpectator;
            }

            // Le host peut avoir invité des PNJ après l'ouverture de la session réseau.
            // Le client ne possède pas leurs vraies Entity, mais il doit tout de même les
            // afficher comme participants de la table et recevoir leurs cartes masquées.
            foreach (PouilleuxParticipantStateMsg participantState in state.Participants.Where(item => item.ConnectionId < 0))
            {
                List<Participant> networkNpcs = _participants.Where(participant => participant.NetworkConnectionId < 0).ToList();
                Participant? participant = networkNpcs.ElementAtOrDefault(state.Participants
                    .Where(item => item.ConnectionId < 0)
                    .ToList().IndexOf(participantState));
                if (participant == null)
                {
                    Entity proxy = new(Program.GetPlayerPosition(), "human")
                    {
                        FirstName = participantState.Name
                    };
                    participant = new Participant { Npc = proxy, NetworkConnectionId = -1 };
                    _participants.Add(participant);
                }
                participant.Npc.FirstName = participantState.Name;
                participant.Cards.Clear();
                participant.Cards.AddRange(participantState.Cards);
                participant.IsSpectator = participantState.IsSpectator;
            }

            _currentTurn = state.CurrentOwnerConnectionId == localConnectionId
                ? 0
                : _participants.FindIndex(participant => participant.NetworkConnectionId == state.CurrentOwnerConnectionId) + 1;
            _status = state.Status;
            TablePhase incomingPhase = (TablePhase)state.Phase;
            bool phaseChanged = _phase != incomingPhase;
            if (!string.IsNullOrEmpty(state.Result))
            {
                _result = state.Result;
                _phase = TablePhase.Finished;
            }
            else if (incomingPhase == TablePhase.FormingCircle)
            {
                _gameStarted = true;
                _phase = TablePhase.FormingCircle;
            }
            else if (incomingPhase == TablePhase.Dealing)
            {
                _gameStarted = true;
                if (phaseChanged)
                {
                    _dealingDeck.Clear();
                    _dealingDeck.AddRange(state.DealingDeck);
                    _dealtCardCount = 0;
                    _dealTimer = 0f;
                    _dealVisualCards.Clear();
                }
                _phase = TablePhase.Dealing;
            }
            else if (incomingPhase == TablePhase.MovingCard ||
                     incomingPhase == TablePhase.ReceivingCard ||
                     incomingPhase == TablePhase.ExplodingPair ||
                     incomingPhase == TablePhase.ReflowingAfterPair)
            {
                if (phaseChanged && incomingPhase == TablePhase.MovingCard)
                {
                    _moveSource = FindParticipant(state.TransferSourceConnectionId);
                    _moveTarget = FindParticipant(state.TransferTargetConnectionId);
                    _movingCard = state.TransferCard;
                    _reflowRemovedIndex = state.TransferCardIndex;
                    _receiveInsertIndex = Math.Max(0, state.TransferTargetIndex);
                    _moveDuration = 0.45f;
                    _phaseTimer = 0f;
                    if (_moveSource != null && _moveTarget != null)
                    {
                        _moveStart = GetCardCenter(_moveSource, _reflowRemovedIndex, _moveSource.Cards.Count + 1);
                        _receiveInsertIndex = Math.Clamp(_receiveInsertIndex, 0, _moveTarget.Cards.Count);
                        _moveEnd = GetCardCenter(_moveTarget, _receiveInsertIndex, _moveTarget.Cards.Count + 1);
                    }
                }
                if (phaseChanged && incomingPhase == TablePhase.ExplodingPair)
                {
                    _moveTarget = FindParticipant(state.TransferTargetConnectionId);
                    _explodingRank = state.AnimationRank;
                    if (_moveTarget != null && _explodingRank >= 0)
                    {
                        _explosionCenter = GetCardCenter(_moveTarget, _moveTarget.Cards.IndexOf(_explodingRank), _moveTarget.Cards.Count);
                        _phaseTimer = 0f;
                        SpawnCardParticles(_explosionCenter);
                    }
                }
                _phase = incomingPhase;
            }
            else if (incomingPhase == TablePhase.NpcShuffling)
            {
                //  Le mélange d'un PNJ est une animation purement locale à ce PNJ (deux de
                // ses propres cartes, face cachée, échangent leur place) : elle ne dépend
                // pas de la main de l'hôte, contrairement à NpcThinking/NpcTakingCard
                // ci-dessous. On peut donc la rejouer fidèlement chez le client à partir des
                // index reçus, sans dupliquer la moindre décision de jeu — le swap réel des
                // cartes reste fait par l'hôte seul (voir la phase NpcShuffling dans
                // Update()) ; ici on ne fait qu'afficher la même animation en parallèle.
                _shufflingNpc = FindParticipant(state.ShuffleParticipantConnectionId);
                _shuffleFirstIndex = state.ShuffleFirstIndex;
                _shuffleSecondIndex = state.ShuffleSecondIndex;
                _shuffleDuration = state.ShuffleDuration;
                if (phaseChanged) _shuffleTimer = 0f;
                _phase = TablePhase.NpcShuffling;
            }
            else if (incomingPhase == TablePhase.NpcThinking ||
                     incomingPhase == TablePhase.NpcTakingCard)
            {
                //  Contrairement au mélange ci-dessus, ces deux phases ne concernent
                // aujourd'hui QUE la main du joueur qui héberge la partie (voir
                // BeginNpcAction : la phase "hésitation" n'existe que si source == _player,
                // c'est-à-dire l'hôte). Les afficher fidèlement chez un client demanderait
                // de transmettre en plus la position à l'écran de la main concernée pour ce
                // client précis (GetThinkingHandPosition suppose actuellement que c'est
                // toujours la main du joueur local) : une extension à part, pas encore
                // faite. En attendant, le client reste sur un état d'attente neutre plutôt
                // que d'exécuter l'IA de l'hôte en double.
                _phase = TablePhase.WaitingForRemotePlayer;
            }
            else if (incomingPhase == TablePhase.WaitingForPlayer || incomingPhase == TablePhase.WaitingForRemotePlayer)
            {
                _phase = _currentTurn == 0 ? TablePhase.WaitingForPlayer : TablePhase.WaitingForRemotePlayer;
            }
            else
            {
                _phase = incomingPhase;
            }
        }

        private static Participant? FindParticipant(int connectionId)
        {
            if (connectionId == 0) return _player;
            return _participants.FirstOrDefault(participant => participant.NetworkConnectionId == connectionId);
        }

        private static int GetNetworkConnectionId(Participant? participant)
        {
            if (participant == null) return int.MinValue;
            return ReferenceEquals(participant, _player) ? 0 : participant.NetworkConnectionId;
        }

        public static void HandleNetworkDrawRequest(int connectionId, PouilleuxDrawRequestMsg request)
        {
            if (!IsNetworkSession || !string.Equals(request.SessionId, _networkSessionId, StringComparison.Ordinal)) return;
            Participant? target = _participants.FirstOrDefault(participant => participant.NetworkConnectionId == connectionId);
            if (target == null || _currentTurn != _participants.IndexOf(target) + 1) return;
            Participant source = GetNextActiveParticipant(target);
            if (request.CardIndex < 0 || request.CardIndex >= source.Cards.Count) return;
            StartTransfer(target, source, request.CardIndex);
        }

        private static void OpenPouilleuxMode()
        {
            _cardMode = CardMode.Pouilleux;
            _status = "Choisis quand lancer la partie.";
        }

        private static void OpenSolitaireMode()
        {
            if (_spokenNpc != null)
            {
                _spokenNpc.ReleaseDialogueFreeze();
                _spokenNpc.ResumeMovementAfterDialogue();
            }
            _participants.Clear();
            _availableNpcs.Clear();
            _cardMode = CardMode.Solitaire;
            _solitaireTableau.Clear();
            _solitaireStock.Clear();
            _solitaireWaste.Clear();
            Array.Fill(_solitaireFoundations, -1);
            Array.Fill(_solitaireFoundationSuits, -1);
            _solitaireSelectedColumn = -1;
            _solitaireSelectedCard = -1;
            _solitaireDragging = false;
            _solitaireWon = false;
            _solitaireStatus = "Retourne toutes les cartes pour gagner.";

            List<int> deck = Enumerable.Range(0, 52).ToList();
            Shuffle(deck);
            for (int column = 0; column < 7; column++)
            {
                List<int> pile = new();
                for (int row = 0; row <= column; row++)
                {
                    int card = deck[0];
                    deck.RemoveAt(0);
                    pile.Add(row == column ? card : -(card + 1));
                }
                _solitaireTableau.Add(pile);
            }
            _solitaireStock.AddRange(deck);
        }

        private static void OpenPokerMode()
        {
            if (_spokenNpc != null)
            {
                _spokenNpc.ReleaseDialogueFreeze();
                _spokenNpc.ResumeMovementAfterDialogue();
            }
            _participants.Clear();
            _availableNpcs.Clear();
            _cardMode = CardMode.Poker;
            _pokerPlayerChips = 1000;
            _pokerOpponentChips = 1000;
            StartPokerRound();
        }

        private static void StartPokerRound()
        {
            _pokerDeck.Clear();
            _pokerDeck.AddRange(Enumerable.Range(0, 52));
            Shuffle(_pokerDeck);
            _pokerPlayerHand.Clear();
            _pokerOpponentHand.Clear();
            Array.Fill(_pokerSelected, false);
            for (int i = 0; i < 5; i++)
            {
                _pokerPlayerHand.Add(_pokerDeck[^1]);
                _pokerDeck.RemoveAt(_pokerDeck.Count - 1);
                _pokerOpponentHand.Add(_pokerDeck[^1]);
                _pokerDeck.RemoveAt(_pokerDeck.Count - 1);
            }
            _pokerPot = 100;
            _pokerPlayerChips = Math.Max(0, _pokerPlayerChips - 50);
            _pokerOpponentChips = Math.Max(0, _pokerOpponentChips - 50);
            _pokerPhase = PokerPhase.Selecting;
            _pokerResult = "";
            _pokerStatus = "Choisis jusqu'a trois cartes a echanger, puis tire.";
        }

        private static void UpdateCardModeSelection()
        {
            if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
            Vector2 mouse = Raylib.GetMousePosition();
            if (Raylib.CheckCollisionPointRec(mouse, _closeRect))
            {
                Close();
                return;
            }
            if (Raylib.CheckCollisionPointRec(mouse, _pouilleuxModeRect))
            {
                OpenPouilleuxMode();
                return;
            }
            if (Raylib.CheckCollisionPointRec(mouse, _pouilleuxModeHelpRect))
            {
                SetRulesMode(false, false, false);
                _showRules = true;
                return;
            }
            if (Raylib.CheckCollisionPointRec(mouse, _solitaireModeHelpRect))
            {
                SetRulesMode(true, false, false);
                _showRules = true;
                return;
            }
            if (Raylib.CheckCollisionPointRec(mouse, _chessModeHelpRect))
            {
                SetRulesMode(false, true, false);
                _showRules = true;
                return;
            }
            if (Raylib.CheckCollisionPointRec(mouse, _pokerModeHelpRect))
            {
                SetRulesMode(false, false, true);
                _showRules = true;
                return;
            }
            if (Raylib.CheckCollisionPointRec(mouse, _solitaireModeRect))
            {
                OpenSolitaireMode();
                return;
            }
            if (Raylib.CheckCollisionPointRec(mouse, _chessModeRect))
            {
                OpenChessMode();
                return;
            }
            if (Raylib.CheckCollisionPointRec(mouse, _pokerModeRect))
                OpenPokerMode();
        }

        private static void SetRulesMode(bool solitaire, bool chess, bool poker)
        {
            _rulesForSolitaire = solitaire;
            _rulesForChess = chess;
            _rulesForPoker = poker;
        }

        private static bool IsSolitaireRed(int rank)
        {
            int suit = rank % 4;
            return suit == 1 || suit == 2;
        }

        private static int SolitaireCardValue(int rank) => rank % 13 + 1;

        private static bool CanPlaceSolitaireFoundationCard(int card, int foundation)
        {
            int expectedValue = _solitaireFoundations[foundation] < 0
                ? 1
                : SolitaireCardValue(_solitaireFoundations[foundation]) + 1;
            return SolitaireCardValue(card) == expectedValue
                && (_solitaireFoundationSuits[foundation] < 0
                    || card % 4 == _solitaireFoundationSuits[foundation]);
        }

        private static bool CanPlaceSolitaireCard(int card, List<int> target)
        {
            if (target.Count == 0) return SolitaireCardValue(card) == 13;
            int targetCard = target[^1];
            return targetCard >= 0
                && SolitaireCardValue(targetCard) == SolitaireCardValue(card) + 1
                && IsSolitaireRed(targetCard) != IsSolitaireRed(card);
        }

        private static void RevealSolitaireCard(List<int> pile)
        {
            if (pile.Count > 0 && pile[^1] < 0)
                pile[^1] = -pile[^1] - 1;
        }

        private static void UpdateSolitaire()
        {
            Vector2 mouse = Raylib.GetMousePosition();
            if (_solitaireDragging)
            {
                if (Raylib.IsMouseButtonDown(MouseButton.Left))
                    return;
                if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                {
                    _solitaireDragging = false;
                    TryDropSolitaireSelection(mouse);
                    return;
                }
            }
            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                if (Raylib.CheckCollisionPointRec(mouse, _closeRect))
                {
                    Close();
                    return;
                }
                if (Raylib.CheckCollisionPointRec(mouse, _solitaireRestartRect))
                {
                    OpenSolitaireMode();
                    return;
                }
                if (Raylib.CheckCollisionPointRec(mouse, _solitaireStockRect))
                {
                    if (_solitaireStock.Count > 0)
                    {
                        _solitaireWaste.Add(_solitaireStock[^1]);
                        _solitaireStock.RemoveAt(_solitaireStock.Count - 1);
                    }
                    else if (_solitaireWaste.Count > 0)
                    {
                        _solitaireStock.AddRange(_solitaireWaste.AsEnumerable().Reverse());
                        _solitaireWaste.Clear();
                    }
                    return;
                }
                if (Raylib.CheckCollisionPointRec(mouse, _solitaireWasteRect) && _solitaireWaste.Count > 0)
                {
                    _solitaireSelectedColumn = -2;
                    _solitaireSelectedCard = _solitaireWaste.Count - 1;
                    _solitaireDragging = true;
                    return;
                }

                float cardWidth = GetSolitaireCardWidth(Raylib.GetScreenWidth());
                float cardHeight = GetSolitaireCardHeight(cardWidth);
                for (int foundation = 0; foundation < 4; foundation++)
                {
                    Rectangle foundationRect = GetSolitaireFoundationRect(foundation, cardWidth, cardHeight, Raylib.GetScreenWidth());
                    if (!Raylib.CheckCollisionPointRec(mouse, foundationRect) || _solitaireSelectedColumn < -2)
                        continue;

                    List<int>? source = _solitaireSelectedColumn >= 0 ? _solitaireTableau[_solitaireSelectedColumn] : null;
                    int card = _solitaireSelectedColumn == -2 ? _solitaireWaste[^1] : source![^1];
                    if (_solitaireSelectedColumn == -2 || (_solitaireSelectedCard == source!.Count - 1 && source.Count > 0))
                    {
                        if (CanPlaceSolitaireFoundationCard(card, foundation))
                        {
                            _solitaireFoundations[foundation] = card;
                            if (_solitaireFoundationSuits[foundation] < 0)
                                _solitaireFoundationSuits[foundation] = card % 4;
                            if (_solitaireSelectedColumn == -2)
                                _solitaireWaste.RemoveAt(_solitaireWaste.Count - 1);
                            else
                            {
                                source!.RemoveAt(source.Count - 1);
                                RevealSolitaireCard(source);
                            }
                            _solitaireWon = _solitaireFoundations.All(value => value >= 0 && SolitaireCardValue(value) == 13);
                            _solitaireStatus = _solitaireWon ? "Victoire !" : "Carte placee dans la fondation.";
                        }
                    }
                    _solitaireSelectedColumn = -1;
                    _solitaireSelectedCard = -1;
                    return;
                }

                for (int column = 0; column < _solitaireTableau.Count; column++)
                {
                    Rectangle columnRect = GetSolitaireColumnRect(column);
                    if (!Raylib.CheckCollisionPointRec(mouse, columnRect)) continue;
                    List<int> pile = _solitaireTableau[column];
                    int clickedIndex = GetSolitaireClickedCardIndex(column, mouse.Y, pile.Count, cardHeight);
                    if (_solitaireSelectedColumn >= 0)
                    {
                        List<int>? source = _solitaireSelectedColumn >= 0 ? _solitaireTableau[_solitaireSelectedColumn] : null;
                        int card = _solitaireSelectedColumn == -2 ? _solitaireWaste[^1] : source![_solitaireSelectedCard];
                        if ((_solitaireSelectedColumn == -2 || column != _solitaireSelectedColumn) && CanPlaceSolitaireCard(card, pile))
                        {
                            if (_solitaireSelectedColumn == -2)
                            {
                                _solitaireWaste.RemoveAt(_solitaireWaste.Count - 1);
                                pile.Add(card);
                            }
                            else
                            {
                                List<int> movingCards = source!.Skip(_solitaireSelectedCard).ToList();
                                source.RemoveRange(_solitaireSelectedCard, movingCards.Count);
                                pile.AddRange(movingCards);
                                RevealSolitaireCard(source);
                            }
                            _solitaireStatus = "Carte deplacee.";
                        }
                        _solitaireSelectedColumn = -1;
                        _solitaireSelectedCard = -1;
                    }
                    else if (clickedIndex >= 0 && pile[clickedIndex] >= 0)
                    {
                        _solitaireSelectedColumn = column;
                        _solitaireSelectedCard = clickedIndex;
                        _solitaireDragging = true;
                    }
                    return;
                }
            }
        }

        private static int GetSolitaireClickedCardIndex(int column, float mouseY, int cardCount, float cardHeight)
        {
            if (cardCount == 0) return -1;
            Rectangle columnRect = GetSolitaireColumnRect(column);
            float spacing = GetSolitaireStackSpacing(cardHeight);
            for (int index = cardCount - 1; index >= 0; index--)
            {
                float cardTop = columnRect.Y + index * spacing;
                float nextCardTop = columnRect.Y + (index + 1) * spacing;
                float cardBottom = index == cardCount - 1 ? cardTop + cardHeight : nextCardTop;
                if (mouseY >= cardTop && mouseY < cardBottom)
                    return index;
            }
            return -1;
        }

        private static void TryDropSolitaireSelection(Vector2 mouse)
        {
            if (_solitaireSelectedColumn < -2) return;
            float cardWidth = GetSolitaireCardWidth(Raylib.GetScreenWidth());
            float cardHeight = GetSolitaireCardHeight(cardWidth);

            for (int foundation = 0; foundation < 4; foundation++)
            {
                Rectangle foundationRect = GetSolitaireFoundationRect(foundation, cardWidth, cardHeight, Raylib.GetScreenWidth());
                if (!Raylib.CheckCollisionPointRec(mouse, foundationRect)) continue;
                List<int>? source = _solitaireSelectedColumn >= 0 ? _solitaireTableau[_solitaireSelectedColumn] : null;
                int card = _solitaireSelectedColumn == -2 ? _solitaireWaste[^1] : source![^1];
                if ((_solitaireSelectedColumn == -2 || _solitaireSelectedCard == source!.Count - 1)
                    && CanPlaceSolitaireFoundationCard(card, foundation))
                {
                    _solitaireFoundations[foundation] = card;
                    if (_solitaireFoundationSuits[foundation] < 0)
                        _solitaireFoundationSuits[foundation] = card % 4;
                    if (_solitaireSelectedColumn == -2)
                        _solitaireWaste.RemoveAt(_solitaireWaste.Count - 1);
                    else
                    {
                        source!.RemoveAt(source.Count - 1);
                        RevealSolitaireCard(source);
                    }
                    _solitaireWon = _solitaireFoundations.All(value => value >= 0 && SolitaireCardValue(value) == 13);
                    _solitaireStatus = _solitaireWon ? "Victoire !" : "Carte placee dans la fondation.";
                }
                _solitaireSelectedColumn = -1;
                _solitaireSelectedCard = -1;
                return;
            }

            for (int column = 0; column < _solitaireTableau.Count; column++)
            {
                if (!Raylib.CheckCollisionPointRec(mouse, GetSolitaireColumnRect(column))) continue;
                List<int> target = _solitaireTableau[column];
                List<int>? source = _solitaireSelectedColumn >= 0 ? _solitaireTableau[_solitaireSelectedColumn] : null;
                int card = _solitaireSelectedColumn == -2 ? _solitaireWaste[^1] : source![_solitaireSelectedCard];
                if ((_solitaireSelectedColumn == -2 || column != _solitaireSelectedColumn) && CanPlaceSolitaireCard(card, target))
                {
                    if (_solitaireSelectedColumn == -2)
                    {
                        _solitaireWaste.RemoveAt(_solitaireWaste.Count - 1);
                        target.Add(card);
                    }
                    else
                    {
                        List<int> movingCards = source!.Skip(_solitaireSelectedCard).ToList();
                        source.RemoveRange(_solitaireSelectedCard, movingCards.Count);
                        target.AddRange(movingCards);
                        RevealSolitaireCard(source);
                    }
                    _solitaireStatus = "Carte deplacee.";
                }
                _solitaireSelectedColumn = -1;
                _solitaireSelectedCard = -1;
                return;
            }

            _solitaireSelectedColumn = -1;
            _solitaireSelectedCard = -1;
        }

        private static Rectangle GetSolitaireColumnRect(int column)
        {
            int screenWidth = Raylib.GetScreenWidth();
            float cardWidth = GetSolitaireCardWidth(screenWidth);
            float tableauY = 120f + GetSolitaireCardHeight(cardWidth) + 58f;
            return new Rectangle(GetSolitaireSlotX(column, screenWidth), tableauY, cardWidth, Raylib.GetScreenHeight() - tableauY - 78f);
        }

        private static Rectangle GetSolitaireFoundationRect(int foundation, float cardWidth, float cardHeight, int screenWidth)
        {
            return new Rectangle(GetSolitaireSlotX(foundation, screenWidth), 120f, cardWidth, cardHeight);
        }

        private static float GetSolitaireSlotX(int slot, int screenWidth)
        {
            const float margin = 42f;
            const float gap = 18f;
            float cardWidth = GetSolitaireCardWidth(screenWidth);
            return margin + slot * (cardWidth + gap);
        }

        private static float GetSolitaireCardWidth(int screenWidth)
        {
            const float margin = 42f;
            const float gap = 18f;
            float availableWidth = screenWidth - margin * 2f - gap * 6f;
            float width = Math.Clamp(availableWidth / 7f, 72f, 170f);
            float screenHeight = Raylib.GetScreenHeight();
            while (width > 72f && 120f + GetSolitaireCardHeight(width) + 58f
                + GetSolitaireCardHeight(width) + 6f * GetSolitaireStackSpacing(GetSolitaireCardHeight(width))
                > screenHeight - 46f)
            {
                width -= 4f;
            }
            return width;
        }

        private static float GetSolitaireCardHeight(float cardWidth)
        {
            return _cardBase.Id != 0
                ? cardWidth * _cardBase.Height / _cardBase.Width
                : cardWidth * 1.49f;
        }

        private static float GetSolitaireStackSpacing(float cardHeight)
        {
            return Math.Clamp(cardHeight * 0.25f, 34f, 52f);
        }

        private static void DrawCardModeSelection(int sw, int sh)
        {
            Raylib.DrawRectangle(0, 0, sw, sh, new Color(20, 24, 30, 248));
            Raylib.DrawRectangle(0, 0, sw, 104, new Color(50, 39, 29, 255));
            string title = "CARTES";
            int titleWidth = FontManager.MeasureText(title, 36);
            FontManager.DrawText(title, sw / 2 - titleWidth / 2, 28, 36, new Color(245, 220, 155, 255));
            string subtitle = "Choisis un mode de jeu";
            int subtitleWidth = FontManager.MeasureText(subtitle, 18);
            FontManager.DrawText(subtitle, sw / 2 - subtitleWidth / 2, 78, 18, Color.White);

            const float buttonWidth = 210f;
            const float buttonHeight = 270f;
            float gap = 14f;
            float startX = (sw - buttonWidth * 4f - gap * 3f) / 2f;
            float buttonY = Math.Max(145f, sh / 2f - buttonHeight / 2f);
            _pouilleuxModeRect = new Rectangle(startX, buttonY, buttonWidth, buttonHeight);
            _solitaireModeRect = new Rectangle(startX + buttonWidth + gap, buttonY, buttonWidth, buttonHeight);
            _chessModeRect = new Rectangle(startX + (buttonWidth + gap) * 2f, buttonY, buttonWidth, buttonHeight);
            _pokerModeRect = new Rectangle(startX + (buttonWidth + gap) * 3f, buttonY, buttonWidth, buttonHeight);
            _pouilleuxModeHelpRect = new Rectangle(startX, buttonY + buttonHeight + 16f, buttonWidth, 38f);
            _solitaireModeHelpRect = new Rectangle(startX + buttonWidth + gap, buttonY + buttonHeight + 16f, buttonWidth, 38f);
            _chessModeHelpRect = new Rectangle(startX + (buttonWidth + gap) * 2f, buttonY + buttonHeight + 16f, buttonWidth, 38f);
            _pokerModeHelpRect = new Rectangle(startX + (buttonWidth + gap) * 3f, buttonY + buttonHeight + 16f, buttonWidth, 38f);

            DrawModeButton(_pouilleuxModeRect, "POUILLEUX", "Avec les PNJ", new Color(125, 91, 46, 255), JOKER_CARD_RANK);
            DrawModeButton(_solitaireModeRect, "SOLITAIRE", "Seul contre le jeu", new Color(62, 103, 92, 255), KING_OF_HEARTS_CARD_RANK);
            DrawModeButton(_chessModeRect, "ECHECS", "Deux joueurs", new Color(76, 83, 105, 255), -1, _chessBlackPawn);
            DrawModeButton(_pokerModeRect, "POKER", "Contre le jeu", new Color(86, 65, 52, 255), -1, _pokerTokens["red"]);
            DrawModeHelpButton(_pouilleuxModeHelpRect);
            DrawModeHelpButton(_solitaireModeHelpRect);
            DrawModeHelpButton(_chessModeHelpRect);
            DrawModeHelpButton(_pokerModeHelpRect);

            _closeRect = new Rectangle(sw - 150, 25, 112, 40);
            UIManager.DrawButton(_closeRect, new Color(95, 70, 48, 255), Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _closeRect));
            FontManager.DrawText("Quitter", (int)_closeRect.X + 28, (int)_closeRect.Y + 11, 16, Color.White);
        }

        private static void DrawModeButton(Rectangle rect, string title, string subtitle, Color color, int cardRank, Texture2D icon = default)
        {
            bool hovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
            Rectangle iconRect = cardRank >= 0
                ? DrawModeCard(cardRank, rect, hovered, color)
                : DrawModeIcon(icon, rect, hovered, color);
            int titleWidth = FontManager.MeasureText(title, 25);
            FontManager.DrawText(title, (int)(rect.X + (rect.Width - titleWidth) / 2f), (int)(iconRect.Y + iconRect.Height) + 12, 25, Color.White);
            int subtitleWidth = FontManager.MeasureText(subtitle, 17);
            FontManager.DrawText(subtitle, (int)(rect.X + (rect.Width - subtitleWidth) / 2f), (int)(iconRect.Y + iconRect.Height) + 50, 17, new Color(245, 230, 195, 255));
        }

        private static Rectangle DrawModeCard(int rank, Rectangle buttonRect, bool hovered, Color color)
        {
            const float maxWidth = 144f;
            const float maxHeight = 174f;
            float scale = _cardBase.Id != 0
                ? MathF.Min(maxWidth / _cardBase.Width, maxHeight / _cardBase.Height)
                : 1f;
            float width = _cardBase.Id != 0 ? _cardBase.Width * scale : maxWidth;
            float height = _cardBase.Id != 0 ? _cardBase.Height * scale : maxHeight;
            Vector2 center = new(buttonRect.X + buttonRect.Width / 2f, buttonRect.Y + maxHeight / 2f);
            if (hovered)
            {
                Rectangle highlight = new(center.X - width / 2f - 8f, center.Y - height / 2f - 8f, width + 16f, height + 16f);
                Raylib.DrawRectangleLinesEx(highlight, 3f, new Color(color.R + 35, color.G + 35, color.B + 35, 255));
            }
            DrawCard(center, width, height, 0f, rank, Color.White, scale);
            return new Rectangle(center.X - width / 2f, center.Y - height / 2f, width, height);
        }

        private static Rectangle DrawModeIcon(Texture2D icon, Rectangle buttonRect, bool hovered, Color color)
        {
            if (icon.Id == 0 || icon.Width <= 0 || icon.Height <= 0) return new Rectangle(buttonRect.X, buttonRect.Y, buttonRect.Width, 0f);
            const float maxWidth = 144f;
            const float maxHeight = 174f;
            float scale = MathF.Min(maxWidth / icon.Width, maxHeight / icon.Height);
            float width = icon.Width * scale;
            float height = icon.Height * scale;
            Rectangle destination = new(
                buttonRect.X + (buttonRect.Width - width) / 2f,
                buttonRect.Y + (maxHeight - height) / 2f,
                width,
                height);
            if (hovered)
            {
                Rectangle highlight = new(destination.X - 8f, destination.Y - 8f, destination.Width + 16f, destination.Height + 16f);
                Raylib.DrawRectangleLinesEx(highlight, 3f, new Color(color.R + 35, color.G + 35, color.B + 35, 255));
            }
            Raylib.DrawTexturePro(icon,
                new Rectangle(0, 0, icon.Width, icon.Height),
                destination,
                Vector2.Zero,
                0f,
                Color.White);
            return destination;
        }

        private static void DrawModeHelpButton(Rectangle rect)
        {
            bool hovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
            Color fill = hovered ? new Color(112, 94, 70, 255) : new Color(86, 72, 54, 255);
            Raylib.DrawRectangleRounded(rect, 0.22f, 8, fill);
            Raylib.DrawRectangleRoundedLines(rect, 0.22f, 8, 1f, new Color(190, 158, 100, hovered ? 255 : 190));
            string label = "COMMENT JOUER ?";
            int textWidth = FontManager.MeasureText(label, 15);
            FontManager.DrawText(label, (int)(rect.X + (rect.Width - textWidth) / 2f), (int)rect.Y + 10, 15, Color.White);
        }

        private static void DrawSolitaire(int sw, int sh)
        {
            Raylib.DrawRectangle(0, 0, sw, sh, new Color(24, 70, 58, 255));
            Raylib.DrawRectangle(0, 0, sw, 92, new Color(35, 48, 42, 255));
            FontManager.DrawText("SOLITAIRE", 34, 24, 30, new Color(245, 220, 155, 255));
            FontManager.DrawText(_solitaireStatus, 36, 61, 16, Color.White);

            _closeRect = new Rectangle(sw - 150, 25, 112, 40);
            UIManager.DrawButton(_closeRect, new Color(95, 70, 48, 255), Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _closeRect));
            FontManager.DrawText("Quitter", (int)_closeRect.X + 28, (int)_closeRect.Y + 11, 16, Color.White);

            DrawHelpButton(sw);

            float cardWidth = GetSolitaireCardWidth(sw);
            float cardHeight = GetSolitaireCardHeight(cardWidth);
            float textureScale = _cardBase.Id != 0 ? cardWidth / _cardBase.Width : 1.45f;
            _solitaireStockRect = new Rectangle(GetSolitaireSlotX(6, sw), 120f, cardWidth, cardHeight);
            if (_solitaireStock.Count > 0)
                DrawCardBack(new Vector2(_solitaireStockRect.X + cardWidth / 2f, _solitaireStockRect.Y + cardHeight / 2f), cardWidth, cardHeight, 0f, false, textureScale);
            else
                Raylib.DrawRectangleLinesEx(_solitaireStockRect, 2f, new Color(210, 190, 130, 255));
            _solitaireWasteRect = new Rectangle(GetSolitaireSlotX(5, sw), 120f, cardWidth, cardHeight);
            if (_solitaireWaste.Count > 0 && !(_solitaireDragging && _solitaireSelectedColumn == -2))
                DrawCard(new Vector2(_solitaireWasteRect.X + cardWidth / 2f, _solitaireWasteRect.Y + cardHeight / 2f), cardWidth, cardHeight, 0f, _solitaireWaste[^1], Color.White, textureScale);
            else
                Raylib.DrawRectangleLinesEx(_solitaireWasteRect, 2f, new Color(210, 190, 130, 255));

            for (int foundation = 0; foundation < 4; foundation++)
            {
                Rectangle rect = GetSolitaireFoundationRect(foundation, cardWidth, cardHeight, sw);
                Raylib.DrawRectangleLinesEx(rect, 2f, new Color(210, 190, 130, 255));
                if (_solitaireFoundations[foundation] >= 0)
                    DrawCard(new Vector2(rect.X + cardWidth / 2f, rect.Y + cardHeight / 2f), cardWidth, cardHeight, 0f, _solitaireFoundations[foundation], Color.White, textureScale);
            }

            float stackSpacing = GetSolitaireStackSpacing(cardHeight);
            for (int column = 0; column < _solitaireTableau.Count; column++)
            {
                Rectangle rect = GetSolitaireColumnRect(column);
                List<int> pile = _solitaireTableau[column];
                for (int index = 0; index < pile.Count; index++)
                {
                    int card = pile[index];
                    if (_solitaireDragging && column == _solitaireSelectedColumn && index >= _solitaireSelectedCard)
                        continue;
                    Vector2 center = new(rect.X + rect.Width / 2f, rect.Y + index * stackSpacing + cardHeight / 2f);
                    bool selected = column == _solitaireSelectedColumn && index == _solitaireSelectedCard;
                    if (card < 0)
                        DrawCardBack(center, cardWidth, cardHeight, 0f, selected, textureScale);
                    else
                        DrawCard(center + (selected ? new Vector2(0, -5f) : Vector2.Zero), cardWidth, cardHeight, 0f, card, Color.White, textureScale);
                }
            }

            if (_solitaireDragging && _solitaireSelectedColumn >= -2)
            {
                Vector2 mouse = Raylib.GetMousePosition();
                if (_solitaireSelectedColumn == -2)
                    DrawCard(mouse, cardWidth, cardHeight, 0f, _solitaireWaste[^1], Color.White, textureScale);
                else
                {
                    List<int> draggedCards = _solitaireTableau[_solitaireSelectedColumn]
                        .Skip(_solitaireSelectedCard).ToList();
                    for (int index = 0; index < draggedCards.Count; index++)
                    {
                        Vector2 draggedPosition = mouse + new Vector2(0f, index * stackSpacing);
                        DrawCard(draggedPosition, cardWidth, cardHeight, 0f, draggedCards[index], Color.White, textureScale);
                    }
                }
            }

            _solitaireRestartRect = new Rectangle(sw / 2f - 70f, sh - 54f, 140f, 36f);
            UIManager.DrawButton(_solitaireRestartRect, new Color(95, 70, 48, 255), Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _solitaireRestartRect));
            FontManager.DrawText("Nouvelle partie", (int)_solitaireRestartRect.X + 15, (int)_solitaireRestartRect.Y + 9, 15, Color.White);

            if (_solitaireWon)
            {
                float panelWidth = Math.Min(460f, sw - 48f);
                float panelHeight = 190f;
                Rectangle panel = new((sw - panelWidth) / 2f, (sh - panelHeight) / 2f, panelWidth, panelHeight);
                Raylib.DrawRectangleRec(panel, new Color(28, 42, 36, 245));
                Raylib.DrawRectangleLinesEx(panel, 3f, new Color(236, 205, 120, 255));
                string title = "VICTOIRE !";
                int titleWidth = FontManager.MeasureText(title, 34);
                FontManager.DrawText(title, (int)(sw / 2f - titleWidth / 2f), (int)panel.Y + 30, 34, new Color(255, 235, 160, 255));
                string message = "Toutes les cartes sont placees.";
                int messageWidth = FontManager.MeasureText(message, 18);
                FontManager.DrawText(message, (int)(sw / 2f - messageWidth / 2f), (int)panel.Y + 84, 18, Color.White);
                string hint = "Lance une nouvelle partie pour rejouer.";
                int hintWidth = FontManager.MeasureText(hint, 15);
                FontManager.DrawText(hint, (int)(sw / 2f - hintWidth / 2f), (int)panel.Y + 126, 15, new Color(215, 225, 215, 255));
            }
        }

        private static void UpdatePoker()
        {
            Vector2 mouse = Raylib.GetMousePosition();
            if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
            if (Raylib.CheckCollisionPointRec(mouse, _closeRect))
            {
                Close();
                return;
            }
            if (Raylib.CheckCollisionPointRec(mouse, _pokerRestartRect))
            {
                StartPokerRound();
                return;
            }
            if (_pokerPhase == PokerPhase.Result) return;
            if (Raylib.CheckCollisionPointRec(mouse, _pokerFoldRect))
            {
                _pokerResult = "Tu te couches. Le pot revient au croupier.";
                _pokerStatus = "Manche terminee.";
                _pokerPhase = PokerPhase.Result;
                return;
            }
            if (Raylib.CheckCollisionPointRec(mouse, _pokerDrawRect))
            {
                ResolvePokerRound();
                return;
            }

            float cardWidth = GetPokerCardWidth(Raylib.GetScreenWidth());
            float cardHeight = GetPokerCardHeight(cardWidth);
            for (int index = 0; index < _pokerPlayerHand.Count; index++)
            {
                Rectangle cardRect = GetPokerCardRect(index, Raylib.GetScreenHeight() - 250f, cardWidth, cardHeight, Raylib.GetScreenWidth());
                if (!Raylib.CheckCollisionPointRec(mouse, cardRect)) continue;
                if (!_pokerSelected[index] && _pokerSelected.Count(selected => selected) >= 3)
                {
                    _pokerStatus = "Tu peux echanger trois cartes maximum.";
                    return;
                }
                _pokerSelected[index] = !_pokerSelected[index];
                _pokerStatus = _pokerSelected.Any(selected => selected)
                    ? "Cartes selectionnees : clique sur Tirer pour les echanger."
                    : "Choisis jusqu'a trois cartes a echanger, puis tire.";
                return;
            }
        }

        private static void ResolvePokerRound()
        {
            int selectedCount = _pokerSelected.Count(selected => selected);
            for (int index = 0; index < _pokerPlayerHand.Count; index++)
            {
                if (!_pokerSelected[index]) continue;
                _pokerPlayerHand[index] = _pokerDeck[^1];
                _pokerDeck.RemoveAt(_pokerDeck.Count - 1);
            }

            int opponentDrawCount = Math.Clamp(Random.Shared.Next(0, 4), 0, 3);
            List<int> opponentIndices = Enumerable.Range(0, 5).OrderBy(_ => Random.Shared.Next()).Take(opponentDrawCount).ToList();
            foreach (int index in opponentIndices)
            {
                _pokerOpponentHand[index] = _pokerDeck[^1];
                _pokerDeck.RemoveAt(_pokerDeck.Count - 1);
            }

            int playerScore = EvaluatePokerHand(_pokerPlayerHand);
            int opponentScore = EvaluatePokerHand(_pokerOpponentHand);
            string playerHandName = GetPokerHandName(playerScore);
            string opponentHandName = GetPokerHandName(opponentScore);
            if (playerScore > opponentScore)
            {
                _pokerPlayerChips += _pokerPot;
                _pokerResult = $"Tu gagnes avec {playerHandName} !";
            }
            else if (playerScore < opponentScore)
            {
                _pokerOpponentChips += _pokerPot;
                _pokerResult = $"Le croupier gagne avec {opponentHandName}.";
            }
            else
            {
                _pokerPlayerChips += _pokerPot / 2;
                _pokerOpponentChips += _pokerPot - _pokerPot / 2;
                _pokerResult = $"Egalite : {playerHandName}. Le pot est partage.";
            }
            _pokerStatus = $"Ta main : {playerHandName} | Croupier : {opponentHandName}";
            _pokerPhase = PokerPhase.Result;
            Array.Fill(_pokerSelected, false);
        }

        private static int EvaluatePokerHand(List<int> hand)
        {
            int[] values = hand.Select(card => card % 13 + 2).OrderByDescending(value => value).ToArray();
            int[] counts = values.GroupBy(value => value).OrderByDescending(group => group.Count()).ThenByDescending(group => group.Key)
                .SelectMany(group => Enumerable.Repeat(group.Key, group.Count())).ToArray();
            bool flush = hand.Select(card => card % 4).Distinct().Count() == 1;
            int highStraight = values.Distinct().Count() == 5 && values.Max() - values.Min() == 4 ? values.Max() : 0;
            if (values.SequenceEqual(new[] { 14, 5, 4, 3, 2 })) highStraight = 5;
            bool straight = highStraight > 0;
            int category;
            if (straight && flush) category = 8;
            else if (counts.GroupBy(value => value).Any(group => group.Count() == 4)) category = 7;
            else if (counts.GroupBy(value => value).Any(group => group.Count() == 3) && counts.GroupBy(value => value).Any(group => group.Count() == 2)) category = 6;
            else if (flush) category = 5;
            else if (straight) category = 4;
            else if (counts.GroupBy(value => value).Any(group => group.Count() == 3)) category = 3;
            else if (counts.GroupBy(value => value).Count(group => group.Count() == 2) == 2) category = 2;
            else if (counts.GroupBy(value => value).Any(group => group.Count() == 2)) category = 1;
            else category = 0;

            IEnumerable<int> tieValues = category is 8 or 4
                ? new[] { highStraight }
                : counts;
            int score = category;
            foreach (int value in tieValues.Take(5).Concat(Enumerable.Repeat(0, 5)).Take(5)) score = score * 15 + value;
            return score;
        }

        private static string GetPokerHandName(int score)
        {
            return (score / (int)Math.Pow(15, Math.Min(5, 5))) switch
            {
                8 => "Quinte flush",
                7 => "Carre",
                6 => "Full",
                5 => "Couleur",
                4 => "Quinte",
                3 => "Brelan",
                2 => "Double paire",
                1 => "Paire",
                _ => "Carte haute"
            };
        }

        private static float GetPokerCardWidth(int screenWidth)
        {
            return Math.Clamp((screenWidth - 240f) / 5.6f, 88f, 126f);
        }

        private static float GetPokerCardHeight(float cardWidth)
        {
            return _cardBase.Id != 0 ? cardWidth * _cardBase.Height / _cardBase.Width : cardWidth * 1.49f;
        }

        private static Rectangle GetPokerCardRect(int index, float top, float cardWidth, float cardHeight, int screenWidth)
        {
            float gap = Math.Min(18f, cardWidth * 0.14f);
            float totalWidth = cardWidth * 5f + gap * 4f;
            float x = (screenWidth - totalWidth) / 2f + index * (cardWidth + gap);
            return new Rectangle(x, top, cardWidth, cardHeight);
        }

        private static void DrawPoker(int sw, int sh)
        {
            Raylib.DrawRectangle(0, 0, sw, sh, new Color(17, 61, 50, 255));
            Raylib.DrawRectangle(0, 0, sw, 92, new Color(28, 40, 35, 255));
            FontManager.DrawText("POKER", 34, 24, 30, new Color(245, 220, 155, 255));
            FontManager.DrawText(_pokerStatus, 36, 61, 16, Color.White);

            _closeRect = new Rectangle(sw - 150, 25, 112, 40);
            UIManager.DrawButton(_closeRect, new Color(95, 70, 48, 255), Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _closeRect));
            FontManager.DrawText("Quitter", (int)_closeRect.X + 28, (int)_closeRect.Y + 11, 16, Color.White);

            DrawPokerStack(new Vector2(96f, 170f), _pokerPlayerChips, "Tes jetons", "blue");
            DrawPokerStack(new Vector2(sw - 96f, 170f), _pokerOpponentChips, "Croupier", "red");
            DrawPokerStack(new Vector2(sw / 2f, 198f), _pokerPot, "Pot", "yellow");

            float cardWidth = GetPokerCardWidth(sw);
            float cardHeight = GetPokerCardHeight(cardWidth);
            float opponentTop = 290f;
            float playerTop = sh - 250f;
            string opponentLabel = _pokerPhase == PokerPhase.Result ? "Main du croupier" : "Main cachee du croupier";
            int opponentLabelWidth = FontManager.MeasureText(opponentLabel, 18);
            FontManager.DrawText(opponentLabel, sw / 2 - opponentLabelWidth / 2, (int)opponentTop - 32, 18, new Color(220, 230, 215, 255));
            for (int index = 0; index < 5; index++)
            {
                Rectangle rect = GetPokerCardRect(index, opponentTop, cardWidth, cardHeight, sw);
                Vector2 center = new(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
                if (_pokerPhase == PokerPhase.Result)
                    DrawCard(center, cardWidth, cardHeight, 0f, _pokerOpponentHand[index], Color.White, cardWidth / _cardBase.Width);
                else
                    DrawCardBack(center, cardWidth, cardHeight, 0f, false, cardWidth / _cardBase.Width);
            }

            int playerLabelWidth = FontManager.MeasureText("Ta main", 18);
            FontManager.DrawText("Ta main", sw / 2 - playerLabelWidth / 2, (int)playerTop - 32, 18, new Color(220, 230, 215, 255));
            for (int index = 0; index < 5; index++)
            {
                Rectangle rect = GetPokerCardRect(index, playerTop - (_pokerSelected[index] ? 18f : 0f), cardWidth, cardHeight, sw);
                Vector2 center = new(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
                DrawCard(center, cardWidth, cardHeight, 0f, _pokerPlayerHand[index], Color.White, cardWidth / _cardBase.Width);
                if (_pokerSelected[index])
                    Raylib.DrawRectangleLinesEx(new Rectangle(rect.X - 3f, rect.Y - 3f, rect.Width + 6f, rect.Height + 6f), 3f, new Color(244, 205, 102, 255));
            }

            if (_pokerPhase == PokerPhase.Selecting)
            {
                _pokerDrawRect = new Rectangle(sw / 2f - 170f, sh - 72f, 150f, 40f);
                _pokerFoldRect = new Rectangle(sw / 2f + 20f, sh - 72f, 150f, 40f);
                DrawPokerButton(_pokerDrawRect, "TIRER", new Color(47, 115, 83, 255));
                DrawPokerButton(_pokerFoldRect, "SE COUCHER", new Color(112, 65, 53, 255));
            }
            else
            {
                _pokerRestartRect = new Rectangle(sw / 2f - 90f, sh - 72f, 180f, 40f);
                DrawPokerButton(_pokerRestartRect, "NOUVELLE MANCHE", new Color(95, 70, 48, 255));
                string resultWidthText = _pokerResult;
                int resultWidth = FontManager.MeasureText(resultWidthText, 22);
                FontManager.DrawText(resultWidthText, sw / 2 - resultWidth / 2, sh - 118, 22, new Color(255, 225, 150, 255));
            }
        }

        private static void DrawPokerButton(Rectangle rect, string label, Color color)
        {
            bool hovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), rect);
            UIManager.DrawButton(rect, hovered ? new Color(color.R + 18, color.G + 18, color.B + 18, 255) : color, hovered);
            int width = FontManager.MeasureText(label, 15);
            FontManager.DrawText(label, (int)(rect.X + (rect.Width - width) / 2f), (int)rect.Y + 11, 15, Color.White);
        }

        private static void DrawPokerStack(Vector2 center, int amount, string label, string tokenColor)
        {
            if (_pokerTokens.TryGetValue(tokenColor, out Texture2D token) && token.Id != 0)
            {
                float size = 48f;
                Raylib.DrawTexturePro(token, new Rectangle(0, 0, token.Width, token.Height),
                    new Rectangle(center.X, center.Y, size, size), new Vector2(size / 2f, size / 2f), 0f, Color.White);
            }
            int amountWidth = FontManager.MeasureText(amount.ToString(), 18);
            FontManager.DrawText(amount.ToString(), (int)center.X - amountWidth / 2, (int)center.Y + 30, 18, Color.White);
            int labelWidth = FontManager.MeasureText(label, 15);
            FontManager.DrawText(label, (int)center.X - labelWidth / 2, (int)center.Y + 54, 15, new Color(210, 225, 210, 255));
        }

        public static void Close()
        {
            if (_spokenNpc != null)
            {
                _spokenNpc.ReleaseDialogueFreeze();
            }
            foreach (Participant participant in _participants)
            {
                participant.Npc.ReleaseDialogueFreeze();
            }
            _spokenNpc = null;
            _participants.Clear();
            _availableNpcs.Clear();
            _cardParticles.Clear();
            _playerIsSpectator = false;
            _gameStarted = false;
            _networkSessionId = null;
            _networkOwnerConnectionId = -1;
            _showRules = false;
            ChessGameUI.Close();
            _cardMode = CardMode.Selection;
            _isOpen = false;
            _phase = TablePhase.Finished;
        }

        public static void Update(float dt)
        {
            if (!_isOpen) return;
            if (Raylib.IsKeyPressed(KeyboardKey.Escape))
            {
                if (_showRules)
                {
                    _showRules = false;
                    return;
                }
                Close();
                return;
            }

            Vector2 mouse = Raylib.GetMousePosition();
            if (_showRules)
            {
                if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mouse, _rulesCloseRect))
                    _showRules = false;
                return;
            }

            if (_cardMode == CardMode.Selection)
            {
                UpdateCardModeSelection();
                return;
            }
            if (_cardMode == CardMode.Solitaire)
            {
                UpdateSolitaire();
                return;
            }
            if (_cardMode == CardMode.Chess)
            {
                ChessGameUI.Update(dt);
                return;
            }
            if (_cardMode == CardMode.Poker)
            {
                UpdatePoker();
                return;
            }

            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(mouse, _helpRect))
            {
                _rulesForSolitaire = false;
                _rulesForChess = false;
                _showRules = true;
                return;
            }

            if (IsNetworkSession && NetworkManager.IsClient)
            {
                AdvanceClientAnimationTimers(dt);
                UpdateNetworkClientInput();
                return;
            }

            UpdateTurnIndicator(dt);
            if (_phase == TablePhase.Finished)
            {
                _resultFade = MathF.Min(1f, _resultFade + dt / 0.65f);
                return;
            }

            //  Tant que la table est ouverte, chaque participant garde la main haute (Play) sur
            // sa propre IA, quelle que soit la phase de la partie (distribution, tour de jeu...).
            // Sans ça, le verrou posé lors de l'invitation/du suivi expire au bout de
            // DEFAULT_GOAL_LOCK (0.6s) et rien n'empêche plus un PNJ assis à table de décrocher
            // en pleine partie pour aller discuter avec un voisin ou rentrer se coucher.
            foreach (Participant participant in _participants)
            {
                Entity npc = participant.Npc;
                if (npc.CurrentGoalPriority <= Entity.NpcPriority.Play)
                    npc.ForceAiPriority(Entity.NpcPriority.Play);
            }

            if (_phase == TablePhase.WaitingForPlayer)
            {
                Vector2 playerPos = Program.GetPlayerPosition();
                for (int i = 0; i < _participants.Count; i++)
                {
                    Participant participant = _participants[i];
                    Entity npc = participant.Npc;
                    //  Force la priorité Play (et rafraîchit son verrou) à CHAQUE frame : ça
                    // empêche une envie plus banale de reprendre la main pendant un "trou" de
                    // verrou, SANS jamais conditionner le déplacement lui-même à la réussite d'une
                    // revendication — sinon le PNJ reste figé sur son ancienne cible tant que le
                    // verrou précédent n'est pas retombé à zéro (~0,6s). On ne le fait que si rien
                    // de plus urgent (fuite, combat...) n'est déjà en cours pour ce PNJ : la
                    // condition <= (et pas <) laisse aussi passer le cas où il détient déjà Play.
                    if (npc.CurrentGoalPriority <= Entity.NpcPriority.Play)
                    {
                        npc.ForceAiPriority(Entity.NpcPriority.Play);
                        npc.AiState = NpcAiState.WalkToTarget;
                        npc.AiTarget = playerPos + new Vector2(-i * 48f, 0f);
                    }
                }

                if (Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    mouse = Raylib.GetMousePosition();
                    if (CanShowPlayButton() && Raylib.CheckCollisionPointRec(mouse, _playRect))
                    {
                        BeginCircleFormation();
                        return;
                    }
                }
            }

            if (_phase == TablePhase.FormingCircle)
            {
                _phaseTimer += dt;
                bool allAtCircle = true;
                for (int i = 0; i < _participants.Count; i++)
                {
                    Participant participant = _participants[i];
                    if (participant.NetworkConnectionId >= 0)
                        continue;
                    Entity npc = participant.Npc;

                    if (npc.CurrentGoalPriority <= Entity.NpcPriority.Play)
                    {
                        npc.ForceAiPriority(Entity.NpcPriority.Play);
                    }

                    // L'IA de pathfinding considère l'arrivée à la cible lorsque le PNJ
                    // est à proximité du point qu'on lui a demandé de viser (rayon de
                    // terminaison ~65f dans MoveTowardTarget). Il faut donc tester la
                    // distance au point de cercle avec cette marge et non avec un rayon
                    // de 24 px trop strict, sous peine de rester bloqué au status.
                    if (npc.AiTarget != Vector2.Zero && Vector2.Distance(npc.WorldPos, npc.AiTarget) >= 65f)
                    {
                        allAtCircle = false;
                    }
                }

                if (allAtCircle || _phaseTimer >= 2.5f)
                {
                    StartDealing();
                }
                return;
            }

            if (_phase == TablePhase.Dealing)
            {
                _dealTimer += dt;
                _dealtCardCount = Math.Clamp((int)MathF.Floor((_dealTimer - DEAL_FLIGHT_DURATION) / DEAL_INTERVAL) + 1, 0, _dealingDeck.Count);
                UpdateDealVisualCards();
                if (_dealTimer >= DEAL_DURATION)
                {
                    FinishDealing();
                    StartInitialPairResolution();
                }
                return;
            }

            if (_phase == TablePhase.PlayerTakingCard)
            {
                _playerTakeTimer += dt;
                if (_playerTakeTimer >= 0.3f && _pendingSource != null && _pendingTarget != null)
                    StartTransfer(_pendingTarget, _pendingSource, _pendingCardIndex);
                return;
            }

            if (_phase == TablePhase.MovingCard)
            {
                _phaseTimer += dt;
                if (_phaseTimer >= _moveDuration)
                {
                    _moveTarget!.Cards.Insert(_receiveInsertIndex, _movingCard);
                    _receiveParticipant = _moveTarget;
                    _receiveOldCount = _moveTarget.Cards.Count - 1;
                    _receiveDuration = 0.35f;
                    _phaseTimer = 0f;
                    _phase = TablePhase.ReceivingCard;
                }
                return;
            }
            if (_phase == TablePhase.ReceivingCard)
            {
                _phaseTimer += dt;
                if (_phaseTimer >= _receiveDuration)
                    ResolvePairs(_receiveParticipant!);
                return;
            }
            if (_phase == TablePhase.ExplodingPair)
            {
                _phaseTimer += dt;
                UpdateCardParticles(dt);
                if (_phaseTimer >= 0.48f)
                {
                    _pairReflowParticipant = _moveTarget;
                    _pairReflowFirstIndex = _moveTarget!.Cards.IndexOf(_explodingRank);
                    _pairReflowSecondIndex = _pairReflowFirstIndex < 0
                        ? -1
                        : _moveTarget.Cards.IndexOf(_explodingRank, _pairReflowFirstIndex + 1);
                    _pairReflowOldCount = _moveTarget.Cards.Count;
                    RemovePair(_moveTarget!, _explodingRank);
                    _explodingRank = -1;
                    _phaseTimer = 0f;
                    _phase = TablePhase.ReflowingAfterPair;
                }
                return;
            }
            if (_phase == TablePhase.ReflowingAfterPair)
            {
                _phaseTimer += dt;
                if (_phaseTimer >= PAIR_REFLOW_DURATION)
                {
                    _pairReflowParticipant = null;
                    if (_resolvingInitialPairs)
                    {
                        if (!TryStartAnyPairExplosion())
                        {
                            _resolvingInitialPairs = false;
                            _phase = TablePhase.WaitingForPlayer;
                            _status = "A ton tour : choisis un paquet PNJ.";
                        }
                    }
                    else
                    {
                        UpdateSpectators();
                        if (GetActiveParticipantCount() <= 1)
                            FinishActiveWinner();
                        else
                            AdvanceTurn();
                    }
                }
                return;
            }
            if (_phase == TablePhase.WaitingForNpc)
            {
                _phaseTimer += dt;
                if (_phaseTimer >= 0.8f) StartNpcTurn();
                return;
            }

            if (_phase == TablePhase.WaitingForRemotePlayer)
                return;

            if (_phase == TablePhase.NpcThinking)
            {
                _phaseTimer += dt;
                _thinkingMoveTimer += dt;
                _thinkingChangeTimer -= dt;
                if (_thinkingChangeTimer <= 0f)
                {
                    _thinkingChangeTimer = 0.16f + Random.Shared.NextSingle() * 0.24f;
                    if (_player.Cards.Count > 0)
                    {
                        int previous = _thinkingCard;
                        _thinkingCard = Random.Shared.Next(_player.Cards.Count);
                        if (Random.Shared.NextDouble() < 0.35 && previous >= 0)
                            _thinkingCard = previous;
                        _thinkingHandStart = GetThinkingHandPosition(previous);
                        _thinkingHandEnd = GetThinkingHandPosition(_thinkingCard);
                        _thinkingMoveTimer = 0f;
                    }
                }
                if (_phaseTimer >= _thinkingDuration)
                {
                    _thinkingHandStart = GetThinkingHandPosition(_thinkingCard);
                    _thinkingHandEnd = _thinkingHandStart;
                    _thinkingMoveTimer = 0f;
                    _thinkingMoveDuration = 0.35f;
                    _phaseTimer = 0f;
                    _phase = TablePhase.NpcTakingCard;
                }
                return;
            }
            if (_phase == TablePhase.NpcTakingCard)
            {
                _phaseTimer += dt;
                if (_phaseTimer >= _thinkingMoveDuration)
                    StartTransfer(_thinkingNpc!, _player, _thinkingCard);
                return;
            }

            if (_phase == TablePhase.NpcShuffling)
            {
                _shuffleTimer += dt;
                if (_shuffleTimer >= _shuffleDuration)
                {
                    (_shufflingNpc!.Cards[_shuffleFirstIndex], _shufflingNpc.Cards[_shuffleSecondIndex]) =
                        (_shufflingNpc.Cards[_shuffleSecondIndex], _shufflingNpc.Cards[_shuffleFirstIndex]);
                    _shuffleTriggered = false;
                    BeginCurrentNpcAction();
                }
                return;
            }

            if (_phase == TablePhase.WaitingForPlayer)
            {
                mouse = Raylib.GetMousePosition();
                if (_draggingPlayerCard)
                {
                    _draggedPlayerCardPosition = mouse;
                    if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                        FinishPlayerCardDrag(mouse);
                    return;
                }
                if (Raylib.IsMouseButtonPressed(MouseButton.Left))
                {
                    Participant? facingParticipant = GetPlayerFacingParticipant();
                    if (facingParticipant != null)
                    {
                        int hoveredCard = GetHoveredNpcCard(_participants.IndexOf(facingParticipant), mouse);
                        if (hoveredCard >= 0)
                        {
                            if (IsNetworkSession && NetworkManager.IsClient)
                            {
                                NetworkManager.RequestPouilleuxDraw(_networkSessionId!, _participants.IndexOf(facingParticipant), hoveredCard);
                                _status = "Demande de pioche envoyée...";
                                return;
                            }
                            _pendingSource = facingParticipant;
                            _pendingTarget = _player;
                            _pendingCardIndex = hoveredCard;
                            _playerTakeTimer = 0f;
                            _phase = TablePhase.PlayerTakingCard;
                            _status = "Tu prends une carte...";
                            return;
                        }
                    }

                    if (!_draggingPlayerCard)
                    {
                        int playerCard = GetHoveredPlayerCard(mouse);
                        if (playerCard >= 0)
                        {
                            _draggingPlayerCard = true;
                            _draggedPlayerCardIndex = playerCard;
                            _draggedPlayerCardPosition = mouse;
                        }
                    }
                }
            }
        }

        //  MULTIJOUEUR : côté client, on ne doit JAMAIS décider nous-mêmes des transitions
        // de phase (insertion de carte, résolution de paires, tour suivant...) — c'est
        // l'hôte qui en est seul autoritaire et qui nous les impose via ApplyNetworkState.
        // Rien n'empêche en revanche de faire progresser localement les minuteries qui ne
        // pilotent QUE l'interpolation visuelle (position d'une carte en mouvement,
        // avancement de la distribution, particules d'explosion, mélange d'un PNJ...).
        //
        // AVANT ce correctif, Update() sortait immédiatement pour un client réseau : ces
        // minuteries restaient bloquées à 0 entre deux paquets réseau (reçus toutes les
        // ~80 ms, voir Network.cs), si bien que l'animation "sautait" d'un état figé à un
        // autre au lieu de se jouer, alors que côté hôte (qui exécute Update() en entier
        // à chaque frame) tout s'anime normalement. On ne duplique ici AUCUNE décision de
        // jeu : si notre estimation locale dérive un peu, le prochain PouilleuxStateMsg
        // du host la corrige de toute façon (voir ApplyNetworkState).
        private static void AdvanceClientAnimationTimers(float dt)
        {
            _phaseTimer += dt;
            switch (_phase)
            {
                case TablePhase.Dealing:
                    _dealTimer += dt;
                    _dealtCardCount = Math.Clamp((int)MathF.Floor((_dealTimer - DEAL_FLIGHT_DURATION) / DEAL_INTERVAL) + 1, 0, _dealingDeck.Count);
                    UpdateDealVisualCards();
                    break;
                case TablePhase.ExplodingPair:
                    UpdateCardParticles(dt);
                    break;
                case TablePhase.NpcShuffling:
                    _shuffleTimer += dt;
                    break;
            }
        }

        private static void UpdateNetworkClientInput()
        {
            if (_phase != TablePhase.WaitingForPlayer) return;
            if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
            if (CanShowPlayButton() && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _playRect))
            {
                NetworkManager.RequestPouilleuxStart(_networkSessionId!);
                _status = "Démarrage de la partie...";
                return;
            }
            if (_playerIsSpectator) return;
            Participant? source = GetPlayerFacingParticipant();
            if (source == null) return;
            int cardIndex = GetHoveredNpcCard(_participants.IndexOf(source), Raylib.GetMousePosition());
            if (cardIndex < 0) return;
            NetworkManager.RequestPouilleuxDraw(_networkSessionId!, _participants.IndexOf(source), cardIndex);
            _status = "Demande de pioche envoyée...";
        }

        private static void BeginCircleFormation()
        {
            _gameStarted = true;
            _phase = TablePhase.FormingCircle;
            _phaseTimer = 0f;
            _status = "Le cercle se forme...";

            Vector2 center = Program.GetPlayerPosition();
            int count = _participants.Count;
            for (int i = 0; i < count; i++)
            {
                Participant participant = _participants[i];
                Entity npc = participant.Npc;
                if (participant.NetworkConnectionId >= 0)
                    continue;
                npc.ReleaseDialogueFreeze();
                npc.ResumeMovementAfterDialogue();
                float angle = MathF.PI * 2f * i / Math.Max(1, count);
                Vector2 target = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 72f;
                npc.AiState = NpcAiState.WalkToTarget;
                npc.AiTarget = target;
                npc.AiTimer = 30f;
            }
        }

        private static void StartDealing()
        {
            _phase = TablePhase.Dealing;
            _phaseTimer = 0f;
            _dealTimer = 0f;
            _dealtCardCount = 0;
            _dealVisualCards.Clear();
            _status = "Distribution des cartes...";
        }

        private static void StartNpcTurn()
        {
            Participant current = _participants[_currentTurn - 1];
            Participant source = GetNextActiveParticipant(current);
            if (!_shuffleTriggered && source != _player && current.Cards.Count >= 2 && Random.Shared.NextDouble() < 0.45)
            {
                _shufflingNpc = current;
                _shuffleFirstIndex = Random.Shared.Next(current.Cards.Count);
                do
                {
                    _shuffleSecondIndex = Random.Shared.Next(current.Cards.Count);
                } while (_shuffleSecondIndex == _shuffleFirstIndex);
                _shuffleTimer = 0f;
                _shuffleDuration = 0.45f + Random.Shared.NextSingle() * 0.35f;
                _shuffleTriggered = true;
                _phase = TablePhase.NpcShuffling;
                _status = $"{current.Npc.DisplayName ?? "Le PNJ"} mélange ses cartes...";
                return;
            }
            BeginNpcAction();
        }

        private static void BeginCurrentNpcAction()
        {
            _shuffleTriggered = false;
            BeginNpcAction();
        }

        private static void BeginNpcAction()
        {
            Participant current = _participants[_currentTurn - 1];
            Participant source = GetNextActiveParticipant(current);
            if (source.Cards.Count == 0)
            {
                AdvanceTurn();
                return;
            }
            if (ReferenceEquals(source, _player))
            {
                _thinkingNpc = current;
                _thinkingCard = Random.Shared.Next(_player.Cards.Count);
                _thinkingChangeTimer = 0.12f;
                _thinkingDuration = 1.8f + Random.Shared.NextSingle() * 0.8f;
                _thinkingHandStart = GetThinkingHandPosition(_thinkingCard);
                _thinkingHandEnd = _thinkingHandStart;
                _thinkingMoveTimer = 0f;
                _thinkingMoveDuration = 0.25f;
                _phaseTimer = 0f;
                _phase = TablePhase.NpcThinking;
                _status = $"{current.Npc.DisplayName ?? "Le PNJ"} hésite...";
                return;
            }
            StartTransfer(current, source);
        }

        private static void StartTransfer(Participant target, Participant source, int sourceIndex = -1)
        {
            if (source.Cards.Count == 0) return;
            int index = sourceIndex >= 0 && sourceIndex < source.Cards.Count
                ? sourceIndex
                : Random.Shared.Next(source.Cards.Count);
            Vector2 sourceCardCenter = GetCardCenter(source, index, source.Cards.Count);
            _reflowParticipant = source;
            _reflowRemovedIndex = index;
            _reflowOldCount = source.Cards.Count;
            _movingCard = source.Cards[index];
            source.Cards.RemoveAt(index);
            UpdateSpectators();
            _moveSource = source;
            _moveTarget = target;
            _moveStart = sourceCardCenter;
            _receiveInsertIndex = Random.Shared.Next(target.Cards.Count + 1);
            _moveEnd = GetCardCenter(target, _receiveInsertIndex, target.Cards.Count + 1);
            _moveDuration = 0.45f;
            _phaseTimer = 0f;
            _phase = TablePhase.MovingCard;
            _status = "Une carte change de main...";
        }

        private static void ResolvePairs(Participant hand)
        {
            for (int rank = 0; rank < 16; rank++)
            {
                if (hand.Cards.Count(card => card == rank) >= 2)
                {
                    _explodingRank = rank;
                    int pairIndex = hand.Cards.IndexOf(rank);
                    _explosionCenter = GetCardCenter(hand, pairIndex, hand.Cards.Count);
                    _phaseTimer = 0f;
                    _phase = TablePhase.ExplodingPair;
                    SpawnCardParticles(_explosionCenter);
                    _status = "Une paire ! Elle explose !";
                    return;
                }
            }
            UpdateSpectators();
            if (GetActiveParticipantCount() <= 1)
                FinishActiveWinner();
            else
                AdvanceTurn();
        }

        private static void AdvanceTurn()
        {
            int totalSlots = _participants.Count + 1;
            for (int step = 1; step <= totalSlots; step++)
            {
                int candidate = (_currentTurn + step) % totalSlots;
                if (!IsActiveSlot(candidate)) continue;
                _currentTurn = candidate;
                if (_currentTurn == 0)
                {
                    _phase = TablePhase.WaitingForPlayer;
                    _status = "A ton tour : choisis un paquet PNJ.";
                }
                else
                {
                    Participant participant = _participants[_currentTurn - 1];
                    _phaseTimer = 0f;
                    if (participant.NetworkConnectionId >= 0)
                    {
                        _phase = TablePhase.WaitingForRemotePlayer;
                        _status = $"{participant.Npc.DisplayName ?? "Le joueur"} choisit une carte...";
                    }
                    else
                    {
                        _phase = TablePhase.WaitingForNpc;
                        _status = $"{participant.Npc.DisplayName ?? "Le PNJ"} joue...";
                    }
                }
                return;
            }
            FinishActiveWinner();
        }

        private static void RemovePair(Participant hand, int rank)
        {
            int first = hand.Cards.IndexOf(rank);
            int second = first < 0 ? -1 : hand.Cards.IndexOf(rank, first + 1);
            if (second >= 0)
            {
                hand.Cards.RemoveAt(second);
                hand.Cards.RemoveAt(first);
            }
        }

        private static void UpdateSpectators()
        {
            if (!_playerIsSpectator && _player.Cards.Count == 0)
                _playerIsSpectator = true;
            foreach (Participant participant in _participants)
                if (!participant.IsSpectator && participant.Cards.Count == 0)
                    participant.IsSpectator = true;
        }

        private static bool IsActiveSlot(int slot)
        {
            return slot == 0 ? !_playerIsSpectator : !_participants[slot - 1].IsSpectator;
        }

        private static int GetActiveParticipantCount()
        {
            return (_playerIsSpectator ? 0 : 1) + _participants.Count(participant => !participant.IsSpectator);
        }

        private static void FinishActiveWinner()
        {
            UpdateSpectators();
            if (!_playerIsSpectator)
            {
                Finish("Tu as perdu : il te reste le Pouilleux.");
                return;
            }

            Participant? loser = _participants.FirstOrDefault(participant => !participant.IsSpectator);
            Finish(loser == null
                ? "Tout le monde a fini."
                : $"{loser.Npc.DisplayName ?? "Le PNJ"} a perdu : il lui reste le Pouilleux.");
        }

        private static bool CanShowPlayButton()
        {
            if (IsNetworkSession)
                return NetworkManager.LocalConnectionId == _networkOwnerConnectionId && _participants.Count > 0;
            return _participants.Count > 0 || _availableNpcs.Count > 0;
        }

        private static Participant GetNextActiveParticipant(Participant current)
        {
            var active = new List<Participant>();
            if (!_playerIsSpectator) active.Add(_player);
            active.AddRange(_participants.Where(participant => !participant.IsSpectator));
            int currentIndex = active.IndexOf(current);
            if (currentIndex < 0 || active.Count < 2) return current;
            return active[(currentIndex + 1) % active.Count];
        }

        public static bool TryInviteNpc(Entity npc)
        {
            if (!IsOpen || _phase != TablePhase.WaitingForPlayer) return false;
            if (!npc.IsAlive || !npc.IsVillager || (npc.IsTamed && !npc.IsGuildMember)) return false;
            if (ReferenceEquals(_spokenNpc, npc)) return false;
            if (_participants.Any(participant => ReferenceEquals(participant.Npc, npc))) return false;
            if (_participants.Count >= 8) return false;

            Participant invited = new() { Npc = npc };
            _participants.Add(invited);
            if (_availableNpcs.Contains(npc))
                _availableNpcs.Remove(npc);

            // L'invitation casse explicitement la routine sociale / de dialogue de l'entité
            // et la remet sur un trajet de suivi propre, sans lui laisser son chatter courant
            // ou son partenaire de discussion l'obliger à “revenir” une fois lancé.
            npc.ReleaseDialogueFreeze();
            npc.ResumeMovementAfterDialogue();
            //  Revendique la priorité Play (très haute) dès l'invitation : c'est ce qui empêche
            // toute envie plus banale (aller discuter avec un voisin, rentrer chez soi...) de
            // reprendre la main sur ce PNJ tant qu'il fait partie de la table. Tant qu'IsOpen
            // reste vrai, Update() ci-dessous réclame cette priorité à chaque frame pour la
            // garder verrouillée ; elle se relâchera d'elle-même (le verrou expirera) si la
            // table se ferme sans appeler Close() proprement.
            npc.TryClaimAiPriority(Entity.NpcPriority.Play);
            npc.AiState = NpcAiState.WalkToTarget;
            npc.AiTarget = Program.GetPlayerPosition() + new Vector2(-(_participants.Count - 1) * 48f, 0f);
            npc.AiTimer = 30f;
            _status = $"{npc.DisplayName ?? "Le PNJ"} vient s'installer pour jouer.";
            return true;
        }

        private static void Finish(string result)
        {
            _result = result;
            _resultFade = 0f;
            _status = "La partie est terminee.";
            _phase = TablePhase.Finished;
        }

        private static void UpdateTurnIndicator(float dt)
        {
            if (_phase == TablePhase.Finished || _participants.Count == 0)
                return;

            int width = Raylib.GetScreenWidth();
            int height = Raylib.GetScreenHeight();
            Vector2 center = new(width / 2f, height / 2f + 18f);
            Participant targetParticipant = _currentTurn == 0
                ? _player
                : _participants[Math.Clamp(_currentTurn - 1, 0, _participants.Count - 1)];
            Vector2 target = GetParticipantCardAnchor(targetParticipant);
            Vector2 direction = target - center;
            if (direction.LengthSquared() < 1f)
                return;

            _turnArrowTargetAngle = MathF.Atan2(direction.Y, direction.X);
            float delta = MathF.Atan2(
                MathF.Sin(_turnArrowTargetAngle - _turnArrowAngle),
                MathF.Cos(_turnArrowTargetAngle - _turnArrowAngle));
            _turnArrowAngle += delta * MathF.Min(1f, dt * 7.5f);
        }

        private static void Shuffle(List<int> cards)
        {
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (cards[i], cards[j]) = (cards[j], cards[i]);
            }
        }

        private static void RemovePairs(Participant hand)
        {
            for (int rank = 0; rank < 16; rank++)
                RemovePair(hand, rank);
        }

        private static Rectangle GetNpcDeckRect(int index)
        {
            int width = Raylib.GetScreenWidth();
            int height = Raylib.GetScreenHeight();
            float cardScale = GetNpcCardTextureScale(index);
            float cardWidth = _cardBase.Id != 0 ? _cardBase.Width * cardScale : 54f * cardScale / NPC_CARD_TEXTURE_SCALE;
            float cardHeight = _cardBase.Id != 0 ? _cardBase.Height * cardScale : 68f * cardScale / NPC_CARD_TEXTURE_SCALE;

            float sideMargin = 96f;
            float horizontalPadding = 16f;
            float topY = 138f;
            float sideY = 330f;
            float bottomY = Math.Max(560f, height - cardHeight - 150f);

            // 8 places maximum de jeu total : humain (joueur en bas), un haut, deux latéraux,
            // puis les quatre coins quand le nombre d'invités dépasse 4. C'est la seule façon
            // d'éviter qu'une liste de PNJ plus longue que 3 s'empile dans le même coin gauche.
            switch (index)
            {
                case 0:
                    return new Rectangle(width / 2f - cardWidth / 2f, topY, cardWidth, cardHeight);
                case 1:
                    return new Rectangle(sideMargin, sideY, cardWidth, cardHeight);
                case 2:
                    return new Rectangle(width - sideMargin - cardWidth, sideY, cardWidth, cardHeight);
                case 3:
                    return new Rectangle(sideMargin, topY, cardWidth, cardHeight);
                case 4:
                    return new Rectangle(width - sideMargin - cardWidth, topY, cardWidth, cardHeight);
                case 5:
                    return new Rectangle(sideMargin, bottomY, cardWidth, cardHeight);
                case 6:
                    return new Rectangle(width - sideMargin - cardWidth, bottomY, cardWidth, cardHeight);
                default:
                    // Sécurise les indices encore plus grands en les rabattant sur la dernière
                    // case connue, plutôt que de les faire flotter en colonne gauche.
                    return new Rectangle(sideMargin + (index % 2) * (width - sideMargin * 2f - cardWidth),
                        topY + ((index / 2) % 3) * 120f,
                        cardWidth,
                        cardHeight);
            }
        }

        private static Vector2 GetParticipantCardAnchor(Participant participant)
        {
            if (ReferenceEquals(participant, _player))
                return new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() - 128f);
            int index = GetNpcLayoutIndex(participant);
            Rectangle rect = GetNpcDeckRect(Math.Max(0, index));
            return new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        }

        private static Vector2 GetCardCenter(Participant participant, int cardIndex, int cardCount)
        {
            if (ReferenceEquals(participant, _player))
                return GetPlayerCardCenter(cardIndex, cardCount);

            int participantIndex = GetNpcLayoutIndex(participant);
            GetNpcCardLayout(participantIndex, cardCount, cardIndex, out Vector2 center, out _, out _);
            return center;
        }

        private static int GetNpcLayoutIndex(Participant participant)
        {
            if (participant.IsSpectator)
            {
                int spectatorIndex = _participants.Where(candidate => candidate.IsSpectator).ToList().IndexOf(participant);
                return _participants.Count(participant => !participant.IsSpectator) + spectatorIndex;
            }
            return _participants.Where(candidate => !candidate.IsSpectator).ToList().IndexOf(participant);
        }

        private static Participant? GetPlayerFacingParticipant()
        {
            return _participants.FirstOrDefault(participant => !participant.IsSpectator);
        }

        private static bool IsCurrentParticipant(Participant participant)
        {
            if (_currentTurn == 0)
                return ReferenceEquals(participant, _player);
            return !ReferenceEquals(participant, _player)
                && _currentTurn > 0
                && _currentTurn <= _participants.Count
                && ReferenceEquals(_participants[_currentTurn - 1], participant);
        }

        private static bool IsNpcDeckHighlighted(Participant participant)
        {
            if (IsCurrentParticipant(participant))
                return true;

            if (_moveSource != null && ReferenceEquals(_moveSource, participant))
                return true;
            if (_pendingSource != null && ReferenceEquals(_pendingSource, participant))
                return true;

            if (_currentTurn > 0 && _currentTurn <= _participants.Count)
            {
                Participant current = _participants[_currentTurn - 1];
                return ReferenceEquals(GetNextActiveParticipant(current), participant);
            }

            return false;
        }

        private static float GetNpcCardTextureScale(int participantIndex)
        {
            List<Participant> activeParticipants = _participants.Where(participant => !participant.IsSpectator).ToList();
            if (participantIndex >= 0 && participantIndex < activeParticipants.Count && IsNpcDeckHighlighted(activeParticipants[participantIndex]))
                return ACTIVE_NPC_CARD_TEXTURE_SCALE;
            return NPC_CARD_TEXTURE_SCALE;
        }

        private static (Vector2 pupilLeft, Vector2 pupilRight, Vector2 browLeft, Vector2 browRight) GetPreviewGaze(Participant participant, Vector2 previewPos)
        {
            Vector2 target;
            if (_currentTurn == 0)
            {
                target = GetParticipantCardAnchor(_player);
            }
            else if (_currentTurn > 0 && _currentTurn <= _participants.Count)
            {
                Participant activeParticipant = _participants[_currentTurn - 1];
                target = ReferenceEquals(activeParticipant, participant)
                    ? new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f)
                    : GetParticipantCardAnchor(activeParticipant);
            }
            else
            {
                target = new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f);
            }

            return Program.ComputeGazeOffsetsFor(previewPos, target, participant.Npc.Facing);
        }

        private static void DrawParticipantNpcBehindDeck(Participant participant)
        {
            if (participant.IsSpectator) return;

            int participantIndex = GetNpcLayoutIndex(participant);
            Rectangle deckRect = GetNpcDeckRect(participantIndex);
            Vector2 previewPos = new(deckRect.X + deckRect.Width / 2f, deckRect.Y + deckRect.Height * 0.72f);

            if (IsCurrentParticipant(participant))
                Raylib.DrawCircleLines((int)previewPos.X, (int)previewPos.Y, 46f, new Color(255, 224, 156, 255));

            var hairBase = (participant.Npc.Species == "human" && Program.hairBaseTextures.Count > 0)
                ? Program.hairBaseTextures[Math.Clamp(participant.Npc.HairStyle, 0, Program.hairBaseTextures.Count - 1)]
                : new Texture2D();
            var hairOverlay = (participant.Npc.Species == "human" && Program.hairOverlayTextures.Count > 0)
                ? Program.hairOverlayTextures[Math.Clamp(participant.Npc.HairStyle, 0, Program.hairOverlayTextures.Count - 1)]
                : new Texture2D();
            var eyeBase = (participant.Npc.Species == "human" && Program.EyeBaseTextures.Count > 0)
                ? Program.EyeBaseTextures[Math.Clamp(Program.EyeStyle, 0, Program.EyeBaseTextures.Count - 1)]
                : new Texture2D();
            var eyeOverlay = (participant.Npc.Species == "human" && Program.EyeOverlayTextures.Count > 0)
                ? Program.EyeOverlayTextures[Math.Clamp(Program.EyeStyle, 0, Program.EyeOverlayTextures.Count - 1)]
                : new Texture2D();

            Color tint = participant.Npc.Tint.R != 0 || participant.Npc.Tint.G != 0 || participant.Npc.Tint.B != 0 || participant.Npc.Tint.A != 0
                ? participant.Npc.Tint
                : Color.White;
            Color hairColor = participant.Npc.HairColor.R != 0 || participant.Npc.HairColor.G != 0 || participant.Npc.HairColor.B != 0 || participant.Npc.HairColor.A != 0
                ? participant.Npc.HairColor
                : Color.White;

            float scale = Math.Clamp(Math.Min(deckRect.Width, deckRect.Height) / 88f, 2.1f, 2.8f);
            var gaze = GetPreviewGaze(participant, previewPos);
            EntityRenderer.DrawEntity(
                participant.Npc.Species,
                "idle",
                0,
                0f,
                participant.Npc.Facing,
                previewPos,
                tint,
                hairBase,
                hairOverlay,
                hairColor,
                SpeciesData.Skeletons,
                eyeBase,
                eyeOverlay,
                customScale: scale,
                equipment: participant.Npc.Equipment,
                isCarrying: false,
                inWater: false,
                attackSwingProgress: 0f,
                heldItemTexture: default,
                headAngle: 0f,
                keepItemHorizontal: false,
                isBow: false,
                underwearTexture: default,
                prevAnim: "",
                prevFrame: 0,
                prevProg: 0f,
                transitionWeight: 0f,
                flashWhite: false,
                beardStyle: participant.Npc.BeardStyle,
                pupilLeftOffset: gaze.pupilLeft,
                pupilRightOffset: gaze.pupilRight,
                eyebrowLeftOffset: gaze.browLeft,
                eyebrowRightOffset: gaze.browRight,
                forceBlinking: participant.Npc.IsBlinking,
                randomFeatureVariant: participant.Npc.RandomFeatureVariant,
                randomFeatureColor: participant.Npc.RandomFeatureColor,
                shadowGroundOffset: 0f,
                drawCarriedEntity: null,
                drawMount: null);
        }

        public static void Draw()
        {
            if (!_isOpen) return;
            EnsureCardTexturesLoaded();
            int sw = Raylib.GetScreenWidth();
            int sh = Raylib.GetScreenHeight();

            if (_cardMode == CardMode.Selection)
            {
                DrawCardModeSelection(sw, sh);
                if (_showRules) DrawRulesPopup(sw, sh);
                return;
            }
            if (_cardMode == CardMode.Solitaire)
            {
                DrawSolitaire(sw, sh);
                if (_showRules) DrawRulesPopup(sw, sh);
                return;
            }
            if (_cardMode == CardMode.Chess)
            {
                ChessGameUI.Draw(sw, sh);
                return;
            }
            if (_cardMode == CardMode.Poker)
            {
                DrawPoker(sw, sh);
                return;
            }

            if (_phase == TablePhase.WaitingForPlayer && !_gameStarted)
            {
                DrawWaitingForPlayerSelection(sw, sh);
                DrawHelpButton(sw);
                if (_showRules) DrawRulesPopup(sw, sh);
                return;
            }

            Raylib.DrawRectangle(0, 0, sw, sh, new Color(20, 24, 30, 248));
            Raylib.DrawRectangle(0, 0, sw, 92, new Color(50, 39, 29, 255));
            FontManager.DrawText("POUILLEUX", 34, 24, 30, new Color(245, 220, 155, 255));
            FontManager.DrawText(_status, 36, 61, 16, Color.White);
            //  PAS de bouton "Jouer" ici : on est forcément dans une phase APRÈS WaitingForPlayer
            // (cette dernière retourne plus haut via DrawWaitingForPlayerSelection). Un bouton
            // "Jouer" était pourtant dessiné ici tant que CanShowPlayButton() restait vrai — donc
            // visible pendant la formation du cercle et la partie elle-même — alors qu'aucun clic
            // n'y était rattaché pour ces phases (le seul gestionnaire vit dans le bloc
            // WaitingForPlayer d'Update()) : un bouton fantôme, cliquable en apparence mais mort.

            if (_phase != TablePhase.Finished && _phase != TablePhase.FormingCircle && _phase != TablePhase.Dealing && !_resolvingInitialPairs)
                DrawTurnIndicator(sw, sh);

            for (int i = 0; i < _participants.Count; i++)
            {
                Participant participant = _participants[i];
                DrawParticipantNpcBehindDeck(participant);
                Rectangle rect = GetNpcDeckRect(GetNpcLayoutIndex(participant));
                FontManager.DrawText(participant.Npc.DisplayName ?? "PNJ", (int)rect.X - 12, (int)rect.Y - 34, 18, new Color(220, 190, 130, 255));
                if (!participant.IsSpectator && _phase != TablePhase.Dealing && !_resolvingInitialPairs)
                    DrawCardBacks(i, participant.Cards.Count, Raylib.GetMousePosition());
                FontManager.DrawText(participant.IsSpectator ? "Spectateur" : $"{participant.Cards.Count}", (int)rect.X + 22, (int)rect.Y + 142, 17, participant.IsSpectator ? new Color(180, 180, 180, 255) : Color.White);
            }

            DrawNpcShufflingCards();

            if (!_playerIsSpectator && _phase != TablePhase.Dealing && !_resolvingInitialPairs)
                DrawPlayerHand(sh - 190f);
            FontManager.DrawText("Ta main", 36, sh - 224, 20, new Color(220, 190, 130, 255));
            FontManager.DrawText(_playerIsSpectator ? "Spectateur" : $"Cartes : {_player.Cards.Count}", sw - 190, sh - 48, 18, _playerIsSpectator ? new Color(180, 180, 180, 255) : Color.White);

            if (_phase == TablePhase.WaitingForPlayer && !_playerIsSpectator)
            {
                Vector2 mouse = Raylib.GetMousePosition();
                Participant? facingParticipant = GetPlayerFacingParticipant();
                if (facingParticipant != null)
                {
                    int hovered = GetHoveredNpcCard(_participants.IndexOf(facingParticipant), mouse);
                    if (hovered >= 0)
                    {
                        Vector2 hoveredCenter = GetCardCenter(facingParticipant, hovered, facingParticipant.Cards.Count);
                        string action = Raylib.IsMouseButtonDown(MouseButton.Left) ? "grab" : "point";
                        DrawHandTexture(hoveredCenter, "palm", action, 255);
                    }
                }
            }

            if (_phase == TablePhase.Dealing)
                DrawDealAnimation();
            else if (_resolvingInitialPairs)
                DrawInitialFaceUpHands();

            if (_phase == TablePhase.NpcThinking && _thinkingNpc != null && _thinkingCard >= 0)
            {
                Vector2 handPosition = GetAnimatedThinkingHandPosition();
                DrawHandTexture(handPosition, "back", "point", 255);
            }
            else if (_phase == TablePhase.PlayerTakingCard && _pendingSource != null)
            {
                Vector2 cardPosition = GetCardCenter(_pendingSource, _pendingCardIndex, _pendingSource.Cards.Count);
                DrawHandTexture(cardPosition, "palm", "grab", 255);
            }
            else if (_phase == TablePhase.NpcTakingCard && _thinkingNpc != null)
            {
                float progress = Math.Clamp(_phaseTimer / _thinkingMoveDuration, 0f, 1f);
                progress = progress * progress * (3f - 2f * progress);
                Vector2 handPosition = Vector2.Lerp(_thinkingHandStart, _thinkingHandEnd, progress);
                DrawHandTexture(handPosition, "back", "grab", 255);
            }
            if (_phase == TablePhase.MovingCard)
            {
                float progress = Math.Clamp(_phaseTimer / _moveDuration, 0f, 1f);
                progress = progress * progress * (3f - 2f * progress);
                Vector2 position = Vector2.Lerp(_moveStart, _moveEnd, progress);
                if (_moveSource != null && ReferenceEquals(_moveSource, _player))
                    DrawCard(position, 82f, 122f, 0f, _movingCard, Color.White);
                else
                    DrawCardBack(position, _cardBase.Width * CARD_TEXTURE_SCALE, _cardBase.Height * CARD_TEXTURE_SCALE, 0f, false);
                if (_moveSource != null)
                {
                    int alpha = (int)Math.Clamp(255f * (1f - progress), 0f, 255f);
                    string side = ReferenceEquals(_moveTarget, _player) ? "palm" : "back";
                    DrawHandTexture(position, side, "pick", alpha);
                }
            }
            else if (_phase == TablePhase.ExplodingPair)
            {
                DrawExplodingCard(_explosionCenter, _explodingRank, _phaseTimer);
                DrawCardParticles();
            }

            if (!string.IsNullOrEmpty(_result))
            {
                int alpha = (int)Math.Clamp(255f * _resultFade, 0f, 255f);
                bool playerLost = _result.StartsWith("Tu as perdu", StringComparison.Ordinal);
                Color resultColor = playerLost
                    ? new Color((byte)226, (byte)82, (byte)82, (byte)alpha)
                    : new Color((byte)100, (byte)170, (byte)255, (byte)alpha);
                Raylib.DrawRectangle(0, sh / 2 - 68, sw, 136, new Color((byte)20, (byte)16, (byte)12, (byte)(Math.Min(245, alpha * 245 / 255))));
                int resultWidth = FontManager.MeasureText(_result, 30);
                FontManager.DrawText(_result, sw / 2 - resultWidth / 2, sh / 2 - 26, 30, resultColor);
                Color hintColor = new Color((byte)255, (byte)255, (byte)255, (byte)Math.Clamp(alpha * 0.85f, 0f, 255f));
                FontManager.DrawText("Echap pour fermer", sw / 2 - 75, sh / 2 + 22, 16, hintColor);
            }

            _closeRect = new Rectangle(sw - 150, 25, 112, 40);
            UIManager.DrawButton(_closeRect, new Color(95, 70, 48, 255), Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _closeRect));
            FontManager.DrawText("Quitter", (int)_closeRect.X + 28, (int)_closeRect.Y + 11, 16, Color.White);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _closeRect))
                Close();

            DrawHelpButton(sw);
            if (_showRules) DrawRulesPopup(sw, sh);
        }

        private static void DrawHelpButton(int screenWidth)
        {
            _helpRect = new Rectangle(screenWidth - 380, 20, 218, 50);
            bool hovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _helpRect);
            UIManager.DrawButton(_helpRect, new Color(125, 91, 46, 255), hovered);
            string label = "COMMENT JOUER ?";
            int textWidth = FontManager.MeasureText(label, 17);
            FontManager.DrawText(label, (int)(_helpRect.X + (_helpRect.Width - textWidth) / 2), (int)_helpRect.Y + 15, 17, Color.White);
        }

        private static void DrawRulesPopup(int screenWidth, int screenHeight)
        {
            Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color(0, 0, 0, 150));

            float panelWidth = Math.Min(650f, screenWidth - 48f);
            float panelHeight = 350f;
            float panelX = (screenWidth - panelWidth) / 2f;
            float panelY = (screenHeight - panelHeight) / 2f;
            Rectangle panel = new(panelX, panelY, panelWidth, panelHeight);
            Raylib.DrawRectangleRec(panel, new Color(38, 30, 24, 255));
            Raylib.DrawRectangleLinesEx(panel, 2f, new Color(225, 190, 115, 255));

            string title = _rulesForChess
                ? "COMMENT JOUER AUX ECHECS"
                : _rulesForPoker ? "COMMENT JOUER AU POKER"
                : _rulesForSolitaire ? "COMMENT JOUER AU SOLITAIRE" : "COMMENT JOUER AU POUILLEUX";
            int titleWidth = FontManager.MeasureText(title, 24);
            FontManager.DrawText(title, (int)(screenWidth / 2f - titleWidth / 2f), (int)panelY + 26, 24, new Color(245, 220, 155, 255));

            string[] rules = _rulesForChess
                ? new[]
                {
                    "1. Les blancs commencent et chaque joueur deplace une piece a son tour.",
                    "2. Capture le roi adverse en le mettant echec et mat pour gagner.",
                    "3. Chaque piece possede son propre deplacement sur l'echiquier.",
                    "Les roques, promotions et prises en passant sont geres."
                }
                : _rulesForPoker
                ? new[]
                {
                    "1. Chaque manche commence avec cinq cartes et un ante de 50 jetons.",
                    "2. Selectionne jusqu'a trois cartes, puis clique sur Tirer pour les echanger.",
                    "3. La meilleure combinaison de cinq cartes remporte le pot.",
                    "Les jetons et les cartes du croupier sont reveles a la fin de la manche."
                }
                : _rulesForSolitaire
                ? new[]
                {
                    "1. Retourne les cartes du paquet pour alimenter la défausse.",
                    "2. Place les cartes en alternant les couleurs et en descendant les valeurs.",
                    "3. Les fondations commencent par un As et montent jusqu'au Roi.",
                    "Tu gagnes lorsque les quatre fondations sont complètes."
                }
                : new[]
                {
                    "1. A ton tour, pioche une carte dans la main du PNJ en face de toi.",
                    "2. Deux cartes de meme valeur forment une paire et sont retirees.",
                    "3. Le premier joueur qui n'a plus de cartes gagne.",
                    "Attention : le joueur qui garde le Joker a la fin perd la partie."
                };
            int ruleY = (int)panelY + 92;
            for (int i = 0; i < rules.Length; i++)
                FontManager.DrawText(rules[i], (int)panelX + 30, ruleY + i * 42, 17, i == rules.Length - 1 ? new Color(255, 205, 125, 255) : Color.White);

            _rulesCloseRect = new Rectangle(screenWidth / 2f - 70, panelY + panelHeight - 62, 140, 40);
            bool hovered = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _rulesCloseRect);
            UIManager.DrawButton(_rulesCloseRect, new Color(95, 70, 48, 255), hovered);
            FontManager.DrawText("Compris", (int)_rulesCloseRect.X + 36, (int)_rulesCloseRect.Y + 11, 16, Color.White);
        }

        private static void DrawTurnIndicator(int sw, int sh)
        {
            Vector2 center = new(sw / 2f, sh / 2f - 6f);
            if (_turnArrowTexture.Id != 0)
            {
                const float arrowWidth = 220f;
                float arrowHeight = arrowWidth * _turnArrowTexture.Height / _turnArrowTexture.Width;
                Rectangle source = new(0f, 0f, _turnArrowTexture.Width, _turnArrowTexture.Height);
                Rectangle destination = new(center.X, center.Y, arrowWidth, arrowHeight);
                Vector2 origin = new(arrowWidth / 2f, arrowHeight / 2f);
                float rotation = _turnArrowAngle * 180f / MathF.PI;
                Raylib.DrawTexturePro(_turnArrowTexture, source, destination, origin, rotation, Color.White);
            }

            string label = _currentTurn == 0
                ? "Ton tour"
                : _participants[Math.Clamp(_currentTurn - 1, 0, _participants.Count - 1)].Npc.DisplayName ?? "PNJ";
            const int labelFontSize = 24;
            int labelWidth = FontManager.MeasureText(label, labelFontSize);
            int labelX = (int)center.X - labelWidth / 2;
            int labelY = (int)(center.Y + 78f);
            Color outline = new(0, 0, 0, 255);
            for (int offsetX = -2; offsetX <= 2; offsetX++)
            {
                for (int offsetY = -2; offsetY <= 2; offsetY++)
                    if (offsetX != 0 || offsetY != 0)
                        FontManager.DrawText(label, labelX + offsetX, labelY + offsetY, labelFontSize, outline);
            }
            FontManager.DrawText(label, labelX, labelY, labelFontSize, Color.White);
        }

        private static void DrawWaitingForPlayerSelection(int sw, int sh)
        {
            float buttonWidth = 128f;
            float buttonHeight = 40f;
            _playRect = new Rectangle(sw / 2f - buttonWidth / 2f, 24f, buttonWidth, buttonHeight);
            if (CanShowPlayButton())
            {
                UIManager.DrawButton(_playRect, new Color(95, 70, 48, 255), true);
                FontManager.DrawText("Jouer", (int)_playRect.X + 40, (int)_playRect.Y + 11, 16, Color.White);
            }

            string countText = $"Membres : {GetPlayingMemberCount()}";
            int countX = (int)(sw / 2f - FontManager.MeasureText(countText, 18) / 2f);
            FontManager.DrawText(countText, countX, 78, 18, Color.White);

            float panelWidth = 250f;
            float panelHeight = Math.Max(160f, sh - 130f);
            float panelX = sw - panelWidth - 26f;
            float panelY = 120f;
            Raylib.DrawRectangle((int)panelX, (int)panelY, (int)panelWidth, (int)panelHeight, new Color(35, 30, 26, 210));
            FontManager.DrawText("Proposés", (int)panelX + 16, (int)panelY + 12, 18, new Color(245, 220, 155, 255));

            int startY = (int)panelY + 46;
            for (int i = 0; i < _participants.Count; i++)
            {
                Participant participant = _participants[i];
                if (ReferenceEquals(participant.Npc, _spokenNpc))
                    continue;
                string label = participant.Npc.DisplayName ?? "PNJ";
                FontManager.DrawText($"- {label}", (int)panelX + 16, startY + i * 26, 16, Color.White);
            }
        }

        private static void DrawPlayerHand(float y)
        {
            float cardWidth = _cardBase.Id != 0 ? _cardBase.Width * CARD_TEXTURE_SCALE : 82f;
            float cardHeight = _cardBase.Id != 0 ? _cardBase.Height * CARD_TEXTURE_SCALE : 122f;
            float spacing = Math.Min(cardWidth * 0.78f, (Raylib.GetScreenWidth() - 120f) / Math.Max(1, _player.Cards.Count));
            float startX = Raylib.GetScreenWidth() / 2f - ((_player.Cards.Count - 1) * spacing) / 2f;
            for (int i = 0; i < _player.Cards.Count; i++)
            {
                if (_draggingPlayerCard && i == _draggedPlayerCardIndex)
                    continue;
                float offset = i - (_player.Cards.Count - 1) / 2f;
                float angle = offset * 7f;
                Vector2 center = GetDisplayedCardCenter(_player, i, _player.Cards.Count, y);
                DrawCard(center, cardWidth, cardHeight, angle, _player.Cards[i], Color.White);
            }
            if (_draggingPlayerCard && _draggedPlayerCardIndex >= 0 && _draggedPlayerCardIndex < _player.Cards.Count)
                DrawCard(_draggedPlayerCardPosition, cardWidth, cardHeight, 0f, _player.Cards[_draggedPlayerCardIndex], Color.White);
        }

        private static void DrawDealAnimation()
        {
            DrawDealVisualCards();
            Vector2 deckCenter = new(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f);

            for (int cardIndex = _dealtCardCount; cardIndex < _dealingDeck.Count; cardIndex++)
            {
                float startTime = cardIndex * DEAL_INTERVAL;
                float progress = Math.Clamp((_dealTimer - startTime) / DEAL_FLIGHT_DURATION, 0f, 1f);
                if (_dealTimer < startTime) continue;

                Participant recipient = GetDealRecipient(cardIndex);
                Vector2 destination = GetParticipantCardAnchor(recipient);
                progress = progress * progress * (3f - 2f * progress);
                Vector2 position = Vector2.Lerp(deckCenter, destination, progress);
                DrawCardBack(position, 82f, 122f, 0f, false);
            }
        }

        private static void DrawDealVisualCards()
        {
            foreach (DealVisualCard visualCard in _dealVisualCards)
            {
                int slot = GetDealVisualSlot(visualCard, _dealVisualCards.IndexOf(visualCard));
                float angle = 0f;
                if (!ReferenceEquals(visualCard.Owner, _player))
                {
                    int participantIndex = _participants.IndexOf(visualCard.Owner);
                    GetNpcCardLayout(participantIndex, CountDealVisualCards(visualCard.Owner), slot, out _, out angle, out _);
                }
                else
                {
                    angle = (slot - (CountDealVisualCards(_player) - 1) / 2f) * 7f;
                }
                if (ReferenceEquals(visualCard.Owner, _player))
                    DrawCard(visualCard.Position, _cardBase.Width * CARD_TEXTURE_SCALE, _cardBase.Height * CARD_TEXTURE_SCALE, angle, visualCard.Rank, Color.White);
                else
                    DrawCardBack(visualCard.Position, _cardBase.Width * CARD_TEXTURE_SCALE, _cardBase.Height * CARD_TEXTURE_SCALE, angle, false);
            }
        }

        private static void DrawInitialFaceUpHands()
        {
            DrawFaceUpHand(_player, true);
            foreach (Participant participant in _participants)
                DrawFaceUpHand(participant, false);
        }

        private static void DrawFaceUpHand(Participant owner, bool isPlayer)
        {
            int count = owner.Cards.Count;
            for (int i = 0; i < count; i++)
            {
                float angle;
                Vector2 center;
                if (isPlayer)
                {
                    center = GetDisplayedCardCenter(_player, i, count);
                    angle = (i - (count - 1) / 2f) * 7f;
                }
                else
                {
                    int participantIndex = GetNpcLayoutIndex(owner);
                    center = GetDisplayedCardCenter(owner, i, count);
                    GetNpcCardLayout(participantIndex, count, i, out _, out angle, out _);
                }
                if (isPlayer)
                    DrawCard(center, _cardBase.Width * CARD_TEXTURE_SCALE, _cardBase.Height * CARD_TEXTURE_SCALE, angle, owner.Cards[i], Color.White);
                else
                {
                    float cardScale = GetNpcCardTextureScale(GetNpcLayoutIndex(owner));
                    DrawCardBack(center, _cardBase.Width * cardScale, _cardBase.Height * cardScale, angle, false, cardScale);
                }
            }
        }

        private static Participant GetDealRecipient(int cardIndex)
        {
            int recipientIndex = cardIndex % (_participants.Count + 1);
            return recipientIndex == 0 ? _player : _participants[recipientIndex - 1];
        }

        private static void DrawLandedDealCards()
        {
            int[] counts = new int[_participants.Count + 1];
            for (int i = 0; i < _dealtCardCount; i++)
                counts[(_participants.IndexOf(GetDealRecipient(i)) + 1) % (_participants.Count + 1)]++;

            for (int recipientIndex = 0; recipientIndex < counts.Length; recipientIndex++)
            {
                int count = counts[recipientIndex];
                Participant recipient = recipientIndex == 0 ? _player : _participants[recipientIndex - 1];
                for (int cardIndex = 0; cardIndex < count; cardIndex++)
                {
                    Vector2 center;
                    float angle;
                    if (recipientIndex == 0)
                    {
                        center = GetPlayerCardCenter(cardIndex, count);
                        angle = (cardIndex - (count - 1) / 2f) * 7f;
                    }
                    else
                    {
                        GetNpcCardLayout(recipientIndex - 1, count, cardIndex, out center, out angle, out _);
                    }
                    float cardScale = recipientIndex == 0
                        ? CARD_TEXTURE_SCALE
                        : GetNpcCardTextureScale(recipientIndex - 1);
                    DrawCardBack(center, _cardBase.Width * cardScale, _cardBase.Height * cardScale, angle, false, cardScale);
                }
            }
        }

        private static void FinishDealing()
        {
            _player.Cards.Clear();
            foreach (Participant participant in _participants) participant.Cards.Clear();
            for (int i = 0; i < _dealingDeck.Count; i++)
                GetDealRecipient(i).Cards.Add(_dealingDeck[i]);
            _dealtCardCount = _dealingDeck.Count;
        }

        private static void UpdateDealVisualCards()
        {
            _dealReflowTimer += Raylib.GetFrameTime();
            bool addedCard = false;
            while (_dealVisualCards.Count < _dealtCardCount)
            {
                int cardIndex = _dealVisualCards.Count;
                Participant owner = GetDealRecipient(cardIndex);
                Vector2 position = GetDealVisualTarget(owner, CountDealVisualCards(owner));
                _dealVisualCards.Add(new DealVisualCard
                {
                    Owner = owner,
                    Rank = _dealingDeck[cardIndex],
                    Position = position,
                    StartPosition = position,
                    TargetPosition = position
                });
                addedCard = true;
            }

            if (addedCard)
            {
                foreach (DealVisualCard visualCard in _dealVisualCards)
                {
                    visualCard.StartPosition = visualCard.Position;
                    int slot = GetDealVisualSlot(visualCard, _dealVisualCards.IndexOf(visualCard));
                    visualCard.TargetPosition = GetDealVisualTarget(visualCard.Owner, slot);
                }
                _dealReflowTimer = 0f;
            }

            float progress = Math.Clamp(_dealReflowTimer / DEAL_REFLOW_DURATION, 0f, 1f);
            progress = progress * progress * (3f - 2f * progress);
            for (int i = 0; i < _dealVisualCards.Count; i++)
            {
                DealVisualCard visualCard = _dealVisualCards[i];
                visualCard.Position = Vector2.Lerp(visualCard.StartPosition, visualCard.TargetPosition, progress);
            }
        }

        private static int CountDealVisualCards(Participant owner)
        {
            return _dealVisualCards.Count(card => ReferenceEquals(card.Owner, owner));
        }

        private static int GetDealVisualSlot(DealVisualCard card, int visualIndex)
        {
            int slot = 0;
            for (int i = 0; i <= visualIndex; i++)
                if (ReferenceEquals(_dealVisualCards[i].Owner, card.Owner)) slot++;
            return slot - 1;
        }

        private static Vector2 GetDealVisualTarget(Participant owner, int slot)
        {
            int count = CountDealVisualCards(owner) + (slot >= CountDealVisualCards(owner) ? 1 : 0);
            if (ReferenceEquals(owner, _player))
                return GetPlayerCardCenter(slot, Math.Max(1, count));

            int participantIndex = _participants.IndexOf(owner);
            GetNpcCardLayout(participantIndex, Math.Max(1, count), slot, out Vector2 center, out _, out _);
            return center;
        }

        private static void StartInitialPairResolution()
        {
            _resolvingInitialPairs = true;
            if (TryStartAnyPairExplosion()) return;
            _resolvingInitialPairs = false;
            _phase = TablePhase.WaitingForPlayer;
            _status = "A ton tour : choisis un paquet PNJ.";
        }

        private static bool TryStartAnyPairExplosion()
        {
            var hands = new[] { _player }.Concat(_participants).ToList();
            foreach (Participant hand in hands)
            {
                for (int rank = 0; rank < 16; rank++)
                {
                    if (hand.Cards.Count(card => card == rank) < 2) continue;
                    _moveTarget = hand;
                    _explodingRank = rank;
                    _explosionCenter = GetCardCenter(hand, hand.Cards.IndexOf(rank), hand.Cards.Count);
                    _phaseTimer = 0f;
                    _phase = TablePhase.ExplodingPair;
                    SpawnCardParticles(_explosionCenter);
                    _status = "Une paire explose...";
                    return true;
                }
            }
            return false;
        }

        private static void DrawCardBacks(int participantIndex, int count, Vector2 mouse)
        {
            int hoveredIndex = GetHoveredNpcCard(participantIndex, mouse);
            float cardScale = GetNpcCardTextureScale(GetNpcLayoutIndex(_participants[participantIndex]));
            float cardWidth = _cardBase.Id != 0 ? _cardBase.Width * cardScale : 56f * cardScale / NPC_CARD_TEXTURE_SCALE;
            float cardHeight = _cardBase.Id != 0 ? _cardBase.Height * cardScale : 74f * cardScale / NPC_CARD_TEXTURE_SCALE;
            for (int i = 0; i < count; i++)
            {
                if (_phase == TablePhase.NpcShuffling && _shufflingNpc == _participants[participantIndex] &&
                    (i == _shuffleFirstIndex || i == _shuffleSecondIndex))
                    continue;
                GetNpcCardLayout(GetNpcLayoutIndex(_participants[participantIndex]), count, i, out _, out float angle, out _);
                Vector2 center = GetDisplayedCardCenter(_participants[participantIndex], i, count);
                bool hovered = i == hoveredIndex;
                DrawCardBack(center, cardWidth, cardHeight, angle, hovered, cardScale);
            }
        }

        private static void DrawNpcShufflingCards()
        {
            if (_phase != TablePhase.NpcShuffling || _shufflingNpc == null) return;
            int participantIndex = GetNpcLayoutIndex(_shufflingNpc);
            int count = _shufflingNpc.Cards.Count;
            GetNpcCardLayout(participantIndex, count, _shuffleFirstIndex, out Vector2 first, out float firstAngle, out _);
            GetNpcCardLayout(participantIndex, count, _shuffleSecondIndex, out Vector2 second, out float secondAngle, out _);
            float progress = Math.Clamp(_shuffleTimer / _shuffleDuration, 0f, 1f);
            float wave = MathF.Sin(progress * MathF.PI);
            Vector2 firstPosition = Vector2.Lerp(first, second, progress) + new Vector2(0f, -wave * 34f);
            Vector2 secondPosition = Vector2.Lerp(second, first, progress) + new Vector2(0f, wave * 34f);
            float cardScale = GetNpcCardTextureScale(participantIndex);
            float cardWidth = _cardBase.Width * cardScale;
            float cardHeight = _cardBase.Height * cardScale;
            DrawCardBack(firstPosition, cardWidth, cardHeight, firstAngle, false, cardScale);
            DrawCardBack(secondPosition, cardWidth, cardHeight, secondAngle, false, cardScale);
        }

        private static int GetHoveredPlayerCard(Vector2 mouse)
        {
            float cardWidth = _cardBase.Width * CARD_TEXTURE_SCALE;
            float cardHeight = _cardBase.Height * CARD_TEXTURE_SCALE;
            for (int i = _player.Cards.Count - 1; i >= 0; i--)
            {
                Vector2 center = GetPlayerCardCenter(i, _player.Cards.Count);
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(center.X - cardWidth / 2f, center.Y - cardHeight / 2f, cardWidth, cardHeight)))
                    return i;
            }
            return -1;
        }

        private static void FinishPlayerCardDrag(Vector2 mouse)
        {
            int oldIndex = _draggedPlayerCardIndex;
            int newIndex = GetHoveredPlayerCard(mouse);
            if (newIndex >= 0 && newIndex != oldIndex)
            {
                int card = _player.Cards[oldIndex];
                _player.Cards.RemoveAt(oldIndex);
                if (newIndex > oldIndex) newIndex--;
                _player.Cards.Insert(Math.Clamp(newIndex, 0, _player.Cards.Count), card);
            }
            _draggingPlayerCard = false;
            _draggedPlayerCardIndex = -1;
        }

        private static Vector2 GetDisplayedCardCenter(Participant participant, int cardIndex, int cardCount, float playerY = -1f)
        {
            Vector2 newCenter = ReferenceEquals(participant, _player)
                ? GetPlayerCardCenter(cardIndex, cardCount, playerY)
                : GetCardCenter(participant, cardIndex, cardCount);

            if (_phase == TablePhase.ReflowingAfterPair && ReferenceEquals(participant, _pairReflowParticipant))
            {
                int pairFirst = _pairReflowFirstIndex;
                int pairSecond = _pairReflowSecondIndex;
                int pairReflowOldIndex = cardIndex >= pairSecond ? cardIndex + 2 : cardIndex >= pairFirst ? cardIndex + 1 : cardIndex;
                Vector2 pairReflowOldCenter = ReferenceEquals(participant, _player)
                    ? GetPlayerCardCenter(pairReflowOldIndex, _pairReflowOldCount, playerY)
                    : GetCardCenter(participant, pairReflowOldIndex, _pairReflowOldCount);
                float pairProgress = Math.Clamp(_phaseTimer / PAIR_REFLOW_DURATION, 0f, 1f);
                pairProgress = pairProgress * pairProgress * (3f - 2f * pairProgress);
                return Vector2.Lerp(pairReflowOldCenter, newCenter, pairProgress);
            }

            if (_phase == TablePhase.ReceivingCard && ReferenceEquals(participant, _receiveParticipant) && _receiveOldCount >= 0)
            {
                float receivingProgress = Math.Clamp(_phaseTimer / _receiveDuration, 0f, 1f);
                receivingProgress = receivingProgress * receivingProgress * (3f - 2f * receivingProgress);
                if (cardIndex == _receiveInsertIndex)
                    return Vector2.Lerp(_moveEnd, newCenter, receivingProgress);

                int receivingOldIndex = cardIndex > _receiveInsertIndex ? cardIndex - 1 : cardIndex;
                Vector2 receivingOldCenter = ReferenceEquals(participant, _player)
                    ? GetPlayerCardCenter(receivingOldIndex, _receiveOldCount, playerY)
                    : GetCardCenter(participant, receivingOldIndex, _receiveOldCount);
                return Vector2.Lerp(receivingOldCenter, newCenter, receivingProgress);
            }

            if (_phase != TablePhase.MovingCard || !ReferenceEquals(participant, _reflowParticipant) || _reflowOldCount <= 0)
                return newCenter;

            int oldIndex = cardIndex >= _reflowRemovedIndex ? cardIndex + 1 : cardIndex;
            if (oldIndex >= _reflowOldCount) return newCenter;

            Vector2 oldCenter = ReferenceEquals(participant, _player)
                ? GetPlayerCardCenter(oldIndex, _reflowOldCount, playerY)
                : GetCardCenter(participant, oldIndex, _reflowOldCount);
            float progress = Math.Clamp(_phaseTimer / _moveDuration, 0f, 1f);
            progress = progress * progress * (3f - 2f * progress);
            return Vector2.Lerp(oldCenter, newCenter, progress);
        }

        private static Vector2 GetPlayerCardCenter(int cardIndex, int count, float y = -1f)
        {
            if (y < 0f) y = Raylib.GetScreenHeight() - 190f;
            float cardWidth = _cardBase.Id != 0 ? _cardBase.Width * CARD_TEXTURE_SCALE : 82f;
            float spacing = Math.Min(cardWidth * 0.78f, (Raylib.GetScreenWidth() - 120f) / Math.Max(1, count));
            float startX = Raylib.GetScreenWidth() / 2f - ((count - 1) * spacing) / 2f;
            float offset = cardIndex - (count - 1) / 2f;
            return new Vector2(startX + cardIndex * spacing, y + Math.Abs(offset) * 5f + 60f);
        }

        private static Vector2 GetThinkingHandPosition(int cardIndex)
        {
            if (cardIndex < 0 || _player.Cards.Count == 0)
                return new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() - 190f);
            return GetPlayerCardCenter(cardIndex, _player.Cards.Count);
        }

        private static Vector2 GetAnimatedThinkingHandPosition()
        {
            if (_thinkingMoveDuration <= 0f) return _thinkingHandEnd;
            float progress = Math.Clamp(_thinkingMoveTimer / _thinkingMoveDuration, 0f, 1f);
            progress = progress * progress * (3f - 2f * progress);
            return Vector2.Lerp(_thinkingHandStart, _thinkingHandEnd, progress);
        }

        private static int GetHoveredNpcCard(int participantIndex, Vector2 mouse)
        {
            if (_participants[participantIndex].IsSpectator) return -1;
            int count = _participants[participantIndex].Cards.Count;
            for (int i = count - 1; i >= 0; i--)
            {
                GetNpcCardLayout(GetNpcLayoutIndex(_participants[participantIndex]), count, i, out _, out _, out Rectangle hitRect);
                if (Raylib.CheckCollisionPointRec(mouse, hitRect)) return i;
            }
            return -1;
        }

        private static void GetNpcCardLayout(int participantIndex, int count, int cardIndex, out Vector2 center, out float angle, out Rectangle hitRect)
        {
            Rectangle deckRect = GetNpcDeckRect(participantIndex);
            float cardScale = GetNpcCardTextureScale(participantIndex);
            float cardWidth = _cardBase.Id != 0 ? _cardBase.Width * cardScale : 56f * cardScale / NPC_CARD_TEXTURE_SCALE;
            float cardHeight = _cardBase.Id != 0 ? _cardBase.Height * cardScale : 74f * cardScale / NPC_CARD_TEXTURE_SCALE;
            float spacing = Math.Min(cardWidth * 0.58f, 310f / Math.Max(1, count));
            float offset = cardIndex - (count - 1) / 2f;
            if (participantIndex == 0)
            {
                center = new Vector2(deckRect.X + deckRect.Width / 2f + offset * spacing, deckRect.Y + deckRect.Height / 2f + Math.Abs(offset) * 5f);
                angle = offset * 7f;
            }
            else
            {
                center = new Vector2(deckRect.X + deckRect.Width / 2f + Math.Abs(offset) * 5f, deckRect.Y + deckRect.Height / 2f + offset * spacing);
                angle = -offset * 7f;
            }
            hitRect = new Rectangle(center.X - cardWidth / 2f, center.Y - cardHeight / 2f, cardWidth, cardHeight);
        }

        private static void DrawCardBack(Vector2 center, float width, float height, float angle, bool hovered, float textureScale = CARD_TEXTURE_SCALE)
        {
            EnsureCardTexturesLoaded();
            if (_cardBack.Id != 0)
            {
            DrawCardTexture(_cardBack, center, angle, hovered ? new Color(255, 238, 190, 255) : Color.White, textureScale);
                if (hovered)
                    Raylib.DrawCircle((int)center.X, (int)center.Y, 7f, new Color(255, 225, 150, 190));
                return;
            }
            Color fill = hovered ? new Color(105, 78, 54, 255) : new Color(65, 48, 38, 255);
            Raylib.DrawRectanglePro(new Rectangle(center.X, center.Y, width, height), new Vector2(width / 2f, height / 2f), angle, fill);
            Raylib.DrawRectanglePro(new Rectangle(center.X, center.Y, width - 10f, height - 10f), new Vector2((width - 10f) / 2f, (height - 10f) / 2f), angle, new Color(105, 76, 53, 255));
            if (hovered)
                Raylib.DrawCircle((int)center.X, (int)center.Y, 7f, new Color(255, 225, 150, 190));
        }

        private static void DrawCard(Vector2 center, float width, float height, float angle, int rank, Color tint, float textureScale = CARD_TEXTURE_SCALE)
        {
            EnsureCardTexturesLoaded();
            float cardWidth = _cardBase.Id != 0 ? _cardBase.Width * textureScale : width;
            float cardHeight = _cardBase.Id != 0 ? _cardBase.Height * textureScale : height;
            string suit = CardSuits[Math.Abs(rank) % CardSuits.Length];
            if (_cardBase.Id != 0)
                DrawCardTexture(_cardBase, center, angle, tint, textureScale);
            else
                Raylib.DrawRectanglePro(new Rectangle(center.X, center.Y, width, height), new Vector2(width / 2f, height / 2f), angle, tint);

            if (rank == 99)
            {
                if (_cardJoker.Id != 0)
                    DrawCardTexture(_cardJoker, center, angle, Color.White, textureScale);
                else
                {
                    int jokerFontSize = GetCardLabelFontSize(cardWidth);
                    FontManager.DrawText("J", (int)center.X - jokerFontSize / 2, (int)center.Y - jokerFontSize / 2, jokerFontSize, new Color(180, 45, 45, 255));
                }
                return;
            }

            int cardValue = rank >= 0 ? (rank % 13) + 1 : 0;
            bool isAce = cardValue == 1;
            bool isNumberCard = cardValue >= 2 && cardValue <= 10;
            string? faceName = cardValue switch
            {
                11 => "jack",
                12 => "queen",
                13 => "king",
                _ => null
            };
            Texture2D mainSymbol = isAce ? _cardAces[suit] : _cardSymbols[suit];
            if (isAce && mainSymbol.Id != 0)
            {
                DrawCardTexture(mainSymbol, center, angle, Color.White, textureScale);
            }

            if (isNumberCard && _cardSymbols[suit].Id != 0)
                DrawPips(_cardSymbols[suit], cardValue, center, cardWidth, cardHeight, angle, textureScale);

            if (_cardSmallSymbols[suit].Id != 0)
            {
                Vector2 topLeft = center + RotateOffset(new Vector2(-cardWidth * 0.34f, -cardHeight * 0.32f), angle);
                Vector2 bottomRight = center + RotateOffset(new Vector2(cardWidth * 0.34f, cardHeight * 0.32f), angle);
                DrawCardTexture(_cardSmallSymbols[suit], topLeft, angle, Color.White, textureScale);
                DrawCardTexture(_cardSmallSymbols[suit], bottomRight, angle + 180f, Color.White, textureScale);
            }

            if (faceName != null && _cardFaces.TryGetValue($"{faceName}_{suit}", out Texture2D faceTexture) && faceTexture.Id != 0)
                DrawCardTexture(faceTexture, center, angle, Color.White, textureScale);

            string label = GetCardLabel(rank);
            if (isNumberCard || isAce || faceName != null)
            {
                DrawCardCornerLabel(label, center, cardWidth, cardHeight, angle, false);
                DrawCardCornerLabel(label, center, cardWidth, cardHeight, angle, true);
            }
            else
            {
                int fontSize = GetCardLabelFontSize(cardWidth);
                FontManager.DrawText(label, (int)center.X - fontSize / 2, (int)center.Y - fontSize / 2, fontSize, new Color(55, 42, 35, 255));
            }
        }

        private static int GetCardLabelFontSize(float cardWidth)
        {
            return Math.Clamp((int)(cardWidth * 0.18f), 20, 36);
        }

        private static string GetCardLabel(int rank)
        {
            if (rank == 99) return "P";
            int cardValue = rank >= 0 ? (rank % 13) + 1 : 0;
            return cardValue switch
            {
                1 => "A",
                11 => "J",
                12 => "Q",
                13 => "K",
                _ => cardValue.ToString()
            };
        }

        private static void DrawPips(Texture2D symbol, int count, Vector2 center, float width, float height, float angle, float textureScale)
        {
            float columnOffset = width * 0.22f;
            float rowOffset = height * 0.18f;
            var positions = count switch
            {
                2 => new[] { new Vector2(-columnOffset, -rowOffset * 1.8f), new Vector2(columnOffset, rowOffset * 1.8f) },
                3 => new[] { new Vector2(-columnOffset, rowOffset * 1.8f), Vector2.Zero, new Vector2(columnOffset, -rowOffset * 1.8f) },
                4 => new[] { new Vector2(-columnOffset, -rowOffset), new Vector2(columnOffset, -rowOffset), new Vector2(-columnOffset, rowOffset), new Vector2(columnOffset, rowOffset) },
                5 => new[] { new Vector2(-columnOffset, -rowOffset), new Vector2(columnOffset, -rowOffset), Vector2.Zero, new Vector2(-columnOffset, rowOffset), new Vector2(columnOffset, rowOffset) },
                6 => new[] { new Vector2(-columnOffset, -rowOffset * 1.8f), new Vector2(columnOffset, -rowOffset * 1.8f), new Vector2(-columnOffset, 0f), new Vector2(columnOffset, 0f), new Vector2(-columnOffset, rowOffset * 1.8f), new Vector2(columnOffset, rowOffset * 1.8f) },
                7 => new[] { new Vector2(-columnOffset, -rowOffset * 1.5f), new Vector2(columnOffset, -rowOffset * 1.5f), new Vector2(0f, -rowOffset * 0.5f), new Vector2(-columnOffset, rowOffset * 0.5f), new Vector2(columnOffset, rowOffset * 0.5f), new Vector2(-columnOffset, rowOffset * 1.5f), new Vector2(columnOffset, rowOffset * 1.5f) },
                8 => new[] { new Vector2(-columnOffset, -rowOffset * 2f), new Vector2(columnOffset, -rowOffset * 2f), new Vector2(0f, -rowOffset), new Vector2(-columnOffset, 0f), new Vector2(columnOffset, 0f), new Vector2(0f, rowOffset), new Vector2(-columnOffset, rowOffset * 2f), new Vector2(columnOffset, rowOffset * 2f) },
                9 => new[] { new Vector2(-columnOffset, -rowOffset * 1.55f), new Vector2(columnOffset, -rowOffset * 1.55f), new Vector2(-columnOffset, -rowOffset * 0.65f), new Vector2(columnOffset, -rowOffset * 0.65f), Vector2.Zero, new Vector2(-columnOffset, rowOffset * 0.65f), new Vector2(columnOffset, rowOffset * 0.65f), new Vector2(-columnOffset, rowOffset * 1.55f), new Vector2(columnOffset, rowOffset * 1.55f) },
                10 => new[] { new Vector2(-columnOffset, -rowOffset * 1.55f), new Vector2(columnOffset, -rowOffset * 1.55f), new Vector2(-columnOffset, -rowOffset * 0.65f), new Vector2(columnOffset, -rowOffset * 0.65f), Vector2.Zero + new Vector2(0f, -rowOffset * 1.1f), new Vector2(-columnOffset, rowOffset * 0.65f), new Vector2(columnOffset, rowOffset * 0.65f), new Vector2(-columnOffset, rowOffset * 1.55f), new Vector2(columnOffset, rowOffset * 1.55f), Vector2.Zero + new Vector2(0f, rowOffset * 1.1f) },
                _ => Array.Empty<Vector2>()
            };

            foreach (Vector2 offset in positions)
            {
                Vector2 pipCenter = center + RotateOffset(offset, angle);
                DrawCardTexture(symbol, pipCenter, angle, Color.White, textureScale);
            }
        }

        private static void DrawCardCornerLabel(string label, Vector2 center, float width, float height, float angle, bool bottomRight)
        {
            Vector2 localOffset = bottomRight
                ? new Vector2(width * 0.31f, height * 0.39f)
                : new Vector2(-width * 0.34f, -height * 0.39f);
            int fontSize = GetCardLabelFontSize(width);
            int textWidth = FontManager.MeasureText(label, fontSize);
            Vector2 position = center + RotateOffset(localOffset, angle);
            if (MathF.Abs(angle) < 0.01f)
            {
                FontManager.DrawText(label, (int)(position.X - textWidth / 2f), (int)(position.Y - fontSize / 2f), fontSize, new Color(55, 42, 35, 255));
                return;
            }

            // Les labels tournés des cartes gardent la même police chargée que le reste de l'UI.
            Font font = FontManager.CustomFont.Texture.Id != 0 ? FontManager.CustomFont : Raylib.GetFontDefault();
            Vector2 origin = new(textWidth / 2f, fontSize / 2f);
            float textAngle = bottomRight ? angle + 180f : angle;
            Raylib.DrawTextPro(font, label, position, origin, textAngle, fontSize, 1f, new Color(55, 42, 35, 255));
        }

        private static Vector2 RotateOffset(Vector2 offset, float angle)
        {
            float radians = angle * MathF.PI / 180f;
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);
            return new Vector2(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos);
        }

        private static void DrawCardTexture(Texture2D texture, Vector2 center, float angle, Color tint, float textureScale = CARD_TEXTURE_SCALE)
        {
            float width = texture.Width * textureScale;
            float height = texture.Height * textureScale;
            Raylib.DrawTexturePro(
                texture,
                new Rectangle(0f, 0f, texture.Width, texture.Height),
                new Rectangle(center.X, center.Y, width, height),
                new Vector2(width / 2f, height / 2f),
                angle,
                tint);
        }

        private static void DrawHandTexture(Vector2 cardCenter, string side, string action, int alpha)
        {
            if (!_handTextures.TryGetValue($"{side}_{action}", out Texture2D texture) || texture.Id == 0)
                return;

            float scale = CARD_TEXTURE_SCALE;
            float width = texture.Width * scale;
            float height = texture.Height * scale;
            float cardHeight = _cardBase.Id != 0 ? _cardBase.Height * CARD_TEXTURE_SCALE : 122f;
            Vector2 handCenter = cardCenter + new Vector2(0f, -cardHeight / 2f - height / 2f - 8f);
            byte opacity = (byte)Math.Clamp(alpha, 0, 255);
            Raylib.DrawTexturePro(
                texture,
                new Rectangle(0f, 0f, texture.Width, texture.Height),
                new Rectangle(handCenter.X, handCenter.Y, width, height),
                new Vector2(width / 2f, height / 2f),
                0f,
                new Color((byte)255, (byte)255, (byte)255, opacity));
        }

        private static void DrawExplodingCard(Vector2 center, int rank, float elapsed)
        {
            const float flashDuration = 0.14f;
            if (elapsed >= flashDuration) return;

            float progress = Math.Clamp(elapsed / flashDuration, 0f, 1f);
            float flash = 1f - progress;
            byte alpha = (byte)Math.Clamp(235f * flash, 0f, 255f);
            float scale = 1f + progress * 0.12f;
            float cardWidth = _cardBase.Id != 0 ? _cardBase.Width * CARD_TEXTURE_SCALE : 82f;
            float cardHeight = _cardBase.Id != 0 ? _cardBase.Height * CARD_TEXTURE_SCALE : 122f;
            Raylib.DrawRectanglePro(
                new Rectangle(center.X, center.Y, cardWidth * scale, cardHeight * scale),
                new Vector2(cardWidth * scale / 2f, cardHeight * scale / 2f),
                0f,
                new Color((byte)255, (byte)255, (byte)255, alpha));
        }

        private static void SpawnCardParticles(Vector2 center)
        {
            _cardParticles.Clear();
            for (int i = 0; i < 34; i++)
            {
                float angle = Random.Shared.NextSingle() * MathF.PI * 2f;
                float speed = 80f + Random.Shared.NextSingle() * 190f;
                byte shade = (byte)(215 + Random.Shared.Next(0, 41));
                float life = 0.28f + Random.Shared.NextSingle() * 0.26f;
                _cardParticles.Add(new CardParticle
                {
                    Position = center,
                    Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed,
                    Size = 7f + Random.Shared.NextSingle() * 10f,
                    Rotation = Random.Shared.NextSingle() * 360f,
                    RotationSpeed = -360f + Random.Shared.NextSingle() * 720f,
                    Life = life,
                    MaxLife = life,
                    Color = new Color(shade, shade, shade, (byte)255)
                });
            }
        }

        private static void UpdateCardParticles(float dt)
        {
            for (int i = _cardParticles.Count - 1; i >= 0; i--)
            {
                CardParticle particle = _cardParticles[i];
                particle.Life -= dt;
                if (particle.Life <= 0f)
                {
                    _cardParticles.RemoveAt(i);
                    continue;
                }

                particle.Velocity += new Vector2(0f, 260f) * dt;
                particle.Position += particle.Velocity * dt;
                particle.Rotation += particle.RotationSpeed * dt;
            }
        }

        private static void DrawCardParticles()
        {
            foreach (CardParticle particle in _cardParticles)
            {
                float lifeRatio = Math.Clamp(particle.Life / particle.MaxLife, 0f, 1f);
                byte alpha = (byte)(255f * lifeRatio);
                Color color = new Color(particle.Color.R, particle.Color.G, particle.Color.B, alpha);
                float size = particle.Size * (0.72f + lifeRatio * 0.38f);
                Raylib.DrawRectanglePro(
                    new Rectangle(particle.Position.X, particle.Position.Y, size, size * 0.68f),
                    new Vector2(size / 2f, size * 0.34f),
                    particle.Rotation,
                    color);
            }
        }
    }
}
