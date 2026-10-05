#nullable enable
using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class ChessGameUI
    {
        private enum PieceType { None, Pawn, Knight, Bishop, Rook, Queen, King }

        private struct Piece
        {
            public PieceType Type;
            public bool White;

            public bool IsEmpty => Type == PieceType.None;
            public Piece(PieceType type, bool white)
            {
                Type = type;
                White = white;
            }
        }

        private readonly struct Move
        {
            public readonly int FromX;
            public readonly int FromY;
            public readonly int ToX;
            public readonly int ToY;
            public readonly bool IsCastle;
            public readonly bool IsEnPassant;

            public Move(int fromX, int fromY, int toX, int toY, bool isCastle = false, bool isEnPassant = false)
            {
                FromX = fromX;
                FromY = fromY;
                ToX = toX;
                ToY = toY;
                IsCastle = isCastle;
                IsEnPassant = isEnPassant;
            }
        }

        private static readonly Piece[,] _board = new Piece[8, 8];
        private static readonly Dictionary<string, Texture2D> _pieceTextures = new();
        private static readonly string[] _pieceNames = { "pawn", "knight", "bishop", "rook", "queen", "king" };
        private static Rectangle _closeRect;
        private static Rectangle _restartRect;
        private static Rectangle _boardRect;
        private static bool _isOpen;
        private static bool _whiteTurn;
        private static bool _whiteKingMoved;
        private static bool _blackKingMoved;
        private static bool _whiteRookKingMoved;
        private static bool _whiteRookQueenMoved;
        private static bool _blackRookKingMoved;
        private static bool _blackRookQueenMoved;
        private static int _enPassantX = -1;
        private static int _enPassantY = -1;
        private static int _selectedX = -1;
        private static int _selectedY = -1;
        private static bool _gameOver;
        private static bool _sideToMoveInCheck;
        private static bool _sideToMoveCheckmated;
        private static string _message = "Les blancs commencent.";
        private static bool _animatingMove;
        private static Move _animatedMove;
        private static Piece _animatedPiece;
        private static Piece _capturedPiece;
        private static float _animationElapsed;
        private const float MOVE_ANIMATION_DURATION = 0.42f;
        private const float MOVE_LIFT = 0.28f;
        private static bool _playBlackAfterAnimation;
        private static Sound _moveSound;
        private static Sound _captureSound;
        private static bool _soundsLoaded;
        private static bool _captureSoundPending;

        public static bool IsOpen => _isOpen;

        public static void Open()
        {
            EnsureTexturesLoaded();
            Reset();
            _isOpen = true;
        }

        public static void Close()
        {
            _isOpen = false;
            _selectedX = -1;
            _selectedY = -1;
        }

        private static void EnsureTexturesLoaded()
        {
            if (_pieceTextures.Count > 0) return;
            foreach (string color in new[] { "w", "b" })
            {
                foreach (string piece in _pieceNames)
                    _pieceTextures[$"{color}_{piece}"] = File.Exists($"assets/gui/chess_{color}_{piece}.png")
                        ? Raylib.LoadTexture($"assets/gui/chess_{color}_{piece}.png")
                        : new Texture2D();
            }
        }

        private static void EnsureSoundsLoaded()
        {
            if (_soundsLoaded) return;
            _soundsLoaded = true;
            _moveSound = LoadSound("chess_move");
            _captureSound = LoadSound("chess_capture");
        }

        private static Sound LoadSound(string name)
        {
            foreach (string extension in new[] { ".mp3", ".wav", ".ogg" })
            {
                string path = Path.Combine("assets", "sounds", name + extension);
                if (!File.Exists(path)) continue;
                Sound sound = Raylib.LoadSound(path);
                if (Raylib.IsSoundReady(sound)) return sound;
            }
            return default;
        }

        private static void Reset()
        {
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    _board[x, y] = new Piece(PieceType.None, false);

            PieceType[] backRank =
            {
                PieceType.Rook, PieceType.Knight, PieceType.Bishop, PieceType.Queen,
                PieceType.King, PieceType.Bishop, PieceType.Knight, PieceType.Rook
            };
            for (int x = 0; x < 8; x++)
            {
                _board[x, 0] = new Piece(backRank[x], false);
                _board[x, 1] = new Piece(PieceType.Pawn, false);
                _board[x, 6] = new Piece(PieceType.Pawn, true);
                _board[x, 7] = new Piece(backRank[x], true);
            }

            _whiteTurn = true;
            _whiteKingMoved = false;
            _blackKingMoved = false;
            _whiteRookKingMoved = false;
            _whiteRookQueenMoved = false;
            _blackRookKingMoved = false;
            _blackRookQueenMoved = false;
            _enPassantX = -1;
            _enPassantY = -1;
            _selectedX = -1;
            _selectedY = -1;
            _gameOver = false;
            _sideToMoveInCheck = false;
            _sideToMoveCheckmated = false;
            _message = "Les blancs commencent.";
            _animatingMove = false;
            _animationElapsed = 0f;
            _playBlackAfterAnimation = false;
            _capturedPiece = new Piece(PieceType.None, false);
            _captureSoundPending = false;
        }

        public static void Update(float dt)
        {
            if (!_isOpen) return;
            if (_animatingMove)
            {
                _animationElapsed += dt;
                if (_animationElapsed >= MOVE_ANIMATION_DURATION)
                {
                    _animationElapsed = MOVE_ANIMATION_DURATION;
                    _animatingMove = false;
                    EnsureSoundsLoaded();
                    Sound landingSound = _captureSoundPending ? _captureSound : _moveSound;
                    if (Raylib.IsSoundReady(landingSound)) Raylib.PlaySound(landingSound);
                    _captureSoundPending = false;
                    if (_playBlackAfterAnimation && !_gameOver)
                    {
                        _playBlackAfterAnimation = false;
                        PlayBlackTurn();
                    }
                }
                return;
            }
            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                Vector2 mouse = Raylib.GetMousePosition();
                if (Raylib.CheckCollisionPointRec(mouse, _closeRect))
                {
                    CartesGameUI.Close();
                    return;
                }
                if (Raylib.CheckCollisionPointRec(mouse, _restartRect))
                {
                    Reset();
                    return;
                }
                if (!_gameOver && !_boardRect.Width.Equals(0f))
                    HandleBoardClick(mouse);
            }
        }

        private static void HandleBoardClick(Vector2 mouse)
        {
            float square = _boardRect.Width / 8f;
            int x = (int)((mouse.X - _boardRect.X) / square);
            int y = (int)((mouse.Y - _boardRect.Y) / square);
            if (x < 0 || x >= 8 || y < 0 || y >= 8) return;

            if (_selectedX < 0)
            {
                if (!_board[x, y].IsEmpty && _board[x, y].White == _whiteTurn)
                {
                    _selectedX = x;
                    _selectedY = y;
                }
                return;
            }

            List<Move> legalMoves = GetLegalMoves(_selectedX, _selectedY, _whiteTurn);
            int chosenIndex = legalMoves.FindIndex(move => move.ToX == x && move.ToY == y);
            if (chosenIndex >= 0)
            {
                Move chosenMove = legalMoves[chosenIndex];
                _animatedPiece = _board[chosenMove.FromX, chosenMove.FromY];
                _capturedPiece = GetCapturedPiece(_board, chosenMove);
                ApplyMove(_board, chosenMove, true);
                _whiteTurn = !_whiteTurn;
                _selectedX = -1;
                _selectedY = -1;
                UpdateGameStatus();
                if (!_gameOver && !_whiteTurn)
                    StartMoveAnimation(chosenMove, _animatedPiece, _capturedPiece, true);
                else
                    StartMoveAnimation(chosenMove, _animatedPiece, _capturedPiece, false);
            }
            else if (!_board[x, y].IsEmpty && _board[x, y].White == _whiteTurn)
            {
                _selectedX = x;
                _selectedY = y;
            }
            else
            {
                _selectedX = -1;
                _selectedY = -1;
            }
        }

        private static void UpdateGameStatus()
        {
            bool sideToMoveInCheck = IsInCheck(_board, _whiteTurn);
            bool hasMove = HasAnyLegalMove(_whiteTurn);
            _sideToMoveInCheck = sideToMoveInCheck;
            _sideToMoveCheckmated = sideToMoveInCheck && !hasMove;
            if (!hasMove)
            {
                _gameOver = true;
                _message = sideToMoveInCheck
                    ? $"Echec et mat : {(_whiteTurn ? "les noirs" : "les blancs")} gagnent."
                    : "Pat : partie nulle.";
            }
            else
            {
                _message = sideToMoveInCheck
                    ? $"Echec : {(_whiteTurn ? "aux blancs" : "aux noirs")}."
                    : $"Tour des {(_whiteTurn ? "blancs" : "noirs")}.";
            }
        }

        private static void PlayBlackTurn()
        {
            List<Move> legalMoves = new();
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (!_board[x, y].IsEmpty && !_board[x, y].White)
                        legalMoves.AddRange(GetLegalMoves(x, y, false));

            if (legalMoves.Count == 0)
            {
                UpdateGameStatus();
                return;
            }

            int bestScore = int.MinValue;
            List<Move> bestMoves = new();
            foreach (Move move in legalMoves)
            {
                int score = Random.Shared.Next(0, 4);
                Piece captured = _board[move.ToX, move.ToY];
                if (!captured.IsEmpty) score += PieceValue(captured.Type) * 10;
                if (move.IsEnPassant) score += 10;
                if (_board[move.FromX, move.FromY].Type == PieceType.Pawn && move.ToY == 7) score += 80;

                Piece[,] copy = CloneBoard(_board);
                ApplyMove(copy, move, false);
                if (IsInCheck(copy, true)) score += 35;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMoves.Clear();
                    bestMoves.Add(move);
                }
                else if (score == bestScore)
                {
                    bestMoves.Add(move);
                }
            }

            Move chosenMove = bestMoves[Random.Shared.Next(bestMoves.Count)];
            Piece movingPiece = _board[chosenMove.FromX, chosenMove.FromY];
            Piece capturedPiece = GetCapturedPiece(_board, chosenMove);
            ApplyMove(_board, chosenMove, true);
            _whiteTurn = true;
            UpdateGameStatus();
            StartMoveAnimation(chosenMove, movingPiece, capturedPiece, false);
        }

        private static Piece GetCapturedPiece(Piece[,] board, Move move)
        {
            if (move.IsEnPassant)
                return board[move.ToX, move.FromY];
            return board[move.ToX, move.ToY];
        }

        private static void StartMoveAnimation(Move move, Piece piece, Piece capturedPiece, bool playBlackAfterAnimation)
        {
            _animatedMove = move;
            _animatedPiece = piece;
            _capturedPiece = capturedPiece;
            _animationElapsed = 0f;
            _animatingMove = true;
            _playBlackAfterAnimation = playBlackAfterAnimation;
            _captureSoundPending = !capturedPiece.IsEmpty;
        }

        private static int PieceValue(PieceType type) => type switch
        {
            PieceType.Pawn => 1,
            PieceType.Knight => 3,
            PieceType.Bishop => 3,
            PieceType.Rook => 5,
            PieceType.Queen => 9,
            PieceType.King => 100,
            _ => 0
        };

        private static List<Move> GetLegalMoves(int x, int y, bool white)
        {
            List<Move> legal = new();
            if (x < 0 || x >= 8 || y < 0 || y >= 8 || _board[x, y].IsEmpty || _board[x, y].White != white)
                return legal;

            foreach (Move move in GetPseudoMoves(_board, x, y, white, true))
            {
                Piece[,] copy = CloneBoard(_board);
                ApplyMove(copy, move, false);
                if (!IsInCheck(copy, white))
                    legal.Add(move);
            }
            return legal;
        }

        private static bool HasAnyLegalMove(bool white)
        {
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (!_board[x, y].IsEmpty && _board[x, y].White == white && GetLegalMoves(x, y, white).Count > 0)
                        return true;
            return false;
        }

        private static List<Move> GetPseudoMoves(Piece[,] board, int x, int y, bool white, bool includeCastle)
        {
            List<Move> moves = new();
            Piece piece = board[x, y];
            if (piece.IsEmpty) return moves;

            if (piece.Type == PieceType.Pawn)
            {
                int direction = white ? -1 : 1;
                int startY = white ? 6 : 1;
                int nextY = y + direction;
                if (Inside(x, nextY) && board[x, nextY].IsEmpty)
                {
                    moves.Add(new Move(x, y, x, nextY));
                    if (y == startY && board[x, y + direction * 2].IsEmpty)
                        moves.Add(new Move(x, y, x, y + direction * 2));
                }
                foreach (int dx in new[] { -1, 1 })
                {
                    int targetX = x + dx;
                    if (!Inside(targetX, nextY)) continue;
                    if (!board[targetX, nextY].IsEmpty && board[targetX, nextY].White != white)
                        moves.Add(new Move(x, y, targetX, nextY));
                    else if (targetX == _enPassantX && nextY == _enPassantY)
                        moves.Add(new Move(x, y, targetX, nextY, false, true));
                }
            }
            else if (piece.Type == PieceType.Knight)
            {
                int[,] offsets = { { 1, 2 }, { 2, 1 }, { 2, -1 }, { 1, -2 }, { -1, -2 }, { -2, -1 }, { -2, 1 }, { -1, 2 } };
                for (int i = 0; i < 8; i++) AddIfReachable(board, moves, x, y, x + offsets[i, 0], y + offsets[i, 1], white);
            }
            else if (piece.Type == PieceType.Bishop || piece.Type == PieceType.Rook || piece.Type == PieceType.Queen)
            {
                if (piece.Type is PieceType.Bishop or PieceType.Queen)
                    AddSlidingMoves(board, moves, x, y, white, new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) });
                if (piece.Type is PieceType.Rook or PieceType.Queen)
                    AddSlidingMoves(board, moves, x, y, white, new[] { (1, 0), (-1, 0), (0, 1), (0, -1) });
            }
            else if (piece.Type == PieceType.King)
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (dx != 0 || dy != 0) AddIfReachable(board, moves, x, y, x + dx, y + dy, white);

                if (includeCastle && !IsInCheck(board, white))
                {
                    bool kingMoved = white ? _whiteKingMoved : _blackKingMoved;
                    bool rookKingMoved = white ? _whiteRookKingMoved : _blackRookKingMoved;
                    bool rookQueenMoved = white ? _whiteRookQueenMoved : _blackRookQueenMoved;
                    int homeY = white ? 7 : 0;
                    if (!kingMoved && !rookKingMoved && board[7, homeY].Type == PieceType.Rook && board[7, homeY].White == white
                        && board[5, homeY].IsEmpty && board[6, homeY].IsEmpty
                        && !IsSquareAttacked(board, 5, homeY, !white) && !IsSquareAttacked(board, 6, homeY, !white))
                        moves.Add(new Move(x, y, 6, homeY, true));
                    if (!kingMoved && !rookQueenMoved && board[0, homeY].Type == PieceType.Rook && board[0, homeY].White == white
                        && board[1, homeY].IsEmpty && board[2, homeY].IsEmpty && board[3, homeY].IsEmpty
                        && !IsSquareAttacked(board, 3, homeY, !white) && !IsSquareAttacked(board, 2, homeY, !white))
                        moves.Add(new Move(x, y, 2, homeY, true));
                }
            }
            return moves;
        }

        private static void AddIfReachable(Piece[,] board, List<Move> moves, int fromX, int fromY, int toX, int toY, bool white)
        {
            if (Inside(toX, toY) && (board[toX, toY].IsEmpty || board[toX, toY].White != white) && board[toX, toY].Type != PieceType.King)
                moves.Add(new Move(fromX, fromY, toX, toY));
        }

        private static void AddSlidingMoves(Piece[,] board, List<Move> moves, int x, int y, bool white, IEnumerable<(int dx, int dy)> directions)
        {
            foreach (var (dx, dy) in directions)
            {
                int targetX = x + dx;
                int targetY = y + dy;
                while (Inside(targetX, targetY))
                {
                    if (board[targetX, targetY].IsEmpty)
                        moves.Add(new Move(x, y, targetX, targetY));
                    else
                    {
                        if (board[targetX, targetY].White != white && board[targetX, targetY].Type != PieceType.King)
                            moves.Add(new Move(x, y, targetX, targetY));
                        break;
                    }
                    targetX += dx;
                    targetY += dy;
                }
            }
        }

        private static void ApplyMove(Piece[,] board, Move move, bool updateRights)
        {
            Piece moving = board[move.FromX, move.FromY];
            Piece captured = board[move.ToX, move.ToY];
            board[move.FromX, move.FromY] = new Piece(PieceType.None, false);
            if (move.IsEnPassant)
                board[move.ToX, move.FromY] = new Piece(PieceType.None, false);
            board[move.ToX, move.ToY] = moving.Type == PieceType.Pawn && (move.ToY == 0 || move.ToY == 7)
                ? new Piece(PieceType.Queen, moving.White)
                : moving;

            if (move.IsCastle)
            {
                int homeY = moving.White ? 7 : 0;
                if (move.ToX == 6)
                {
                    board[5, homeY] = board[7, homeY];
                    board[7, homeY] = new Piece(PieceType.None, false);
                }
                else
                {
                    board[3, homeY] = board[0, homeY];
                    board[0, homeY] = new Piece(PieceType.None, false);
                }
            }

            if (!updateRights) return;
            if (moving.Type == PieceType.King)
            {
                if (moving.White) _whiteKingMoved = true; else _blackKingMoved = true;
            }
            if (moving.Type == PieceType.Rook)
            {
                if (moving.White && move.FromY == 7 && move.FromX == 7) _whiteRookKingMoved = true;
                if (moving.White && move.FromY == 7 && move.FromX == 0) _whiteRookQueenMoved = true;
                if (!moving.White && move.FromY == 0 && move.FromX == 7) _blackRookKingMoved = true;
                if (!moving.White && move.FromY == 0 && move.FromX == 0) _blackRookQueenMoved = true;
            }
            if (captured.Type == PieceType.Rook)
            {
                if (captured.White && move.ToY == 7 && move.ToX == 7) _whiteRookKingMoved = true;
                if (captured.White && move.ToY == 7 && move.ToX == 0) _whiteRookQueenMoved = true;
                if (!captured.White && move.ToY == 0 && move.ToX == 7) _blackRookKingMoved = true;
                if (!captured.White && move.ToY == 0 && move.ToX == 0) _blackRookQueenMoved = true;
            }
            _enPassantX = -1;
            _enPassantY = -1;
            if (moving.Type == PieceType.Pawn && Math.Abs(move.ToY - move.FromY) == 2)
            {
                _enPassantX = move.FromX;
                _enPassantY = (move.FromY + move.ToY) / 2;
            }
        }

        private static bool IsInCheck(Piece[,] board, bool white)
        {
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (board[x, y].Type == PieceType.King && board[x, y].White == white)
                        return IsSquareAttacked(board, x, y, !white);
            return true;
        }

        private static bool IsSquareAttacked(Piece[,] board, int x, int y, bool byWhite)
        {
            int pawnY = y + (byWhite ? 1 : -1);
            foreach (int pawnX in new[] { x - 1, x + 1 })
                if (Inside(pawnX, pawnY) && board[pawnX, pawnY].Type == PieceType.Pawn && board[pawnX, pawnY].White == byWhite) return true;

            int[,] knightOffsets = { { 1, 2 }, { 2, 1 }, { 2, -1 }, { 1, -2 }, { -1, -2 }, { -2, -1 }, { -2, 1 }, { -1, 2 } };
            for (int i = 0; i < 8; i++)
            {
                int nx = x + knightOffsets[i, 0];
                int ny = y + knightOffsets[i, 1];
                if (Inside(nx, ny) && board[nx, ny].Type == PieceType.Knight && board[nx, ny].White == byWhite) return true;
            }

            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if ((dx != 0 || dy != 0) && Inside(x + dx, y + dy) && board[x + dx, y + dy].Type == PieceType.King && board[x + dx, y + dy].White == byWhite) return true;

            if (RayAttacked(board, x, y, byWhite, new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }, PieceType.Rook)) return true;
            return RayAttacked(board, x, y, byWhite, new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) }, PieceType.Bishop);
        }

        private static bool RayAttacked(Piece[,] board, int x, int y, bool byWhite, IEnumerable<(int dx, int dy)> directions, PieceType slidingType)
        {
            foreach (var (dx, dy) in directions)
            {
                int nx = x + dx;
                int ny = y + dy;
                while (Inside(nx, ny))
                {
                    if (!board[nx, ny].IsEmpty)
                    {
                        if (board[nx, ny].White == byWhite && (board[nx, ny].Type == slidingType || board[nx, ny].Type == PieceType.Queen)) return true;
                        break;
                    }
                    nx += dx;
                    ny += dy;
                }
            }
            return false;
        }

        private static Piece[,] CloneBoard(Piece[,] source)
        {
            Piece[,] copy = new Piece[8, 8];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++) copy[x, y] = source[x, y];
            return copy;
        }

        private static bool Inside(int x, int y) => x >= 0 && x < 8 && y >= 0 && y < 8;

        public static void Draw(int screenWidth, int screenHeight)
        {
            if (!_isOpen) return;
            EnsureTexturesLoaded();
            Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, new Color(22, 28, 38, 255));
            Raylib.DrawRectangle(0, 0, screenWidth, 88, new Color(43, 35, 29, 255));
            FontManager.DrawText("ECHECS", 32, 22, 30, new Color(245, 220, 155, 255));
            FontManager.DrawText(_message, 34, 58, 16, Color.White);

            _closeRect = new Rectangle(screenWidth - 150, 24, 112, 40);
            UIManager.DrawButton(_closeRect, new Color(95, 70, 48, 255), Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _closeRect));
            FontManager.DrawText("Quitter", (int)_closeRect.X + 28, (int)_closeRect.Y + 11, 16, Color.White);

            float boardSize = MathF.Min(720f, MathF.Min(screenHeight - 142f, screenWidth - 330f));
            boardSize = MathF.Max(360f, boardSize);
            float boardX = (screenWidth - 250f - boardSize) / 2f;
            float boardY = 105f;
            _boardRect = new Rectangle(boardX, boardY, boardSize, boardSize);
            float square = boardSize / 8f;
            Color light = new Color(232, 220, 194, 255);
            Color dark = new Color(105, 122, 139, 255);
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    Color tile = (x + y) % 2 == 0 ? light : dark;
                    Raylib.DrawRectangle((int)(boardX + x * square), (int)(boardY + y * square), (int)MathF.Ceiling(square), (int)MathF.Ceiling(square), tile);
                    if (_sideToMoveInCheck && IsKingAt(x, y, _whiteTurn))
                    {
                        Color checkColor = _sideToMoveCheckmated
                            ? new Color(220, 55, 45, 220)
                            : new Color(245, 205, 55, 220);
                        Raylib.DrawRectangle((int)(boardX + x * square), (int)(boardY + y * square), (int)MathF.Ceiling(square), (int)MathF.Ceiling(square), checkColor);
                    }
                    if (x == _selectedX && y == _selectedY)
                        Raylib.DrawRectangleLinesEx(new Rectangle(boardX + x * square, boardY + y * square, square, square), 4f, new Color(245, 195, 80, 255));
                }
            }

            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (!(_animatingMove && x == _animatedMove.ToX && y == _animatedMove.ToY))
                        DrawPiece(_board[x, y], x, y, boardX, boardY, square);

            if (_animatingMove)
            {
                if (!_capturedPiece.IsEmpty)
                {
                    float captureProgress = Math.Clamp(_animationElapsed / MOVE_ANIMATION_DURATION, 0f, 1f);
                    DrawCapturedPiece(boardX, boardY, square, captureProgress);
                }
                DrawAnimatedPiece(boardX, boardY, square);
            }

            DrawLegalMoveIndicators(boardX, boardY, square);

            _restartRect = new Rectangle(screenWidth - 218, 140, 175, 40);
            UIManager.DrawButton(_restartRect, new Color(95, 70, 48, 255), Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), _restartRect));
            FontManager.DrawText("Nouvelle partie", (int)_restartRect.X + 24, (int)_restartRect.Y + 11, 16, Color.White);
            FontManager.DrawText("Mode local a deux joueurs", screenWidth - 218, 205, 15, new Color(215, 220, 225, 255));
        }

        private static bool IsKingAt(int x, int y, bool white)
        {
            return _board[x, y].Type == PieceType.King && _board[x, y].White == white;
        }

        private static void DrawLegalMoveIndicators(float boardX, float boardY, float square)
        {
            if (_selectedX < 0 || _selectedY < 0 || _gameOver || _animatingMove)
                return;

            if (_board[_selectedX, _selectedY].IsEmpty || _board[_selectedX, _selectedY].White != _whiteTurn)
            {
                _selectedX = -1;
                _selectedY = -1;
                return;
            }

            foreach (Move move in GetLegalMoves(_selectedX, _selectedY, _whiteTurn))
            {
                int centerX = (int)(boardX + (move.ToX + 0.5f) * square);
                int centerY = (int)(boardY + (move.ToY + 0.5f) * square);
                Piece target = move.IsEnPassant ? new Piece(PieceType.Pawn, !_whiteTurn) : _board[move.ToX, move.ToY];
                bool capture = move.IsEnPassant || !target.IsEmpty;
                if (!capture)
                {
                    Raylib.DrawCircle(centerX, centerY, MathF.Max(7f, square * 0.085f), new Color(26, 48, 43, 210));
                    Raylib.DrawCircle(centerX, centerY, MathF.Max(3f, square * 0.045f), new Color(220, 235, 210, 190));
                }
                else
                {
                    float radius = MathF.Max(15f, square * 0.27f);
                    Raylib.DrawCircleLines(centerX, centerY, radius, new Color(238, 104, 76, 245));
                    Raylib.DrawCircleLines(centerX, centerY, radius - 4f, new Color(255, 210, 105, 210));
                    Raylib.DrawLineEx(new Vector2(centerX - radius * 0.52f, centerY - radius * 0.52f), new Vector2(centerX + radius * 0.52f, centerY + radius * 0.52f), 3f, new Color(238, 104, 76, 220));
                    Raylib.DrawLineEx(new Vector2(centerX + radius * 0.52f, centerY - radius * 0.52f), new Vector2(centerX - radius * 0.52f, centerY + radius * 0.52f), 3f, new Color(238, 104, 76, 220));
                }
            }
        }

        private static void DrawPiece(Piece piece, int x, int y, float boardX, float boardY, float square)
        {
            if (piece.IsEmpty) return;
            DrawPieceAt(piece,
                boardX + (x + 0.5f) * square,
                boardY + (y + 1f) * square,
                square,
                0f);
        }

        private static void DrawAnimatedPiece(float boardX, float boardY, float square)
        {
            float progress = Math.Clamp(_animationElapsed / MOVE_ANIMATION_DURATION, 0f, 1f);
            float eased = progress * progress * (3f - 2f * progress);
            float x = MathHelper.Lerp(_animatedMove.FromX, _animatedMove.ToX, eased);
            float y = MathHelper.Lerp(_animatedMove.FromY, _animatedMove.ToY, eased);
            float lift = MathF.Sin(progress * MathF.PI) * square * MOVE_LIFT;
            float centerX = boardX + (x + 0.5f) * square;
            float groundY = boardY + (y + 1f) * square;

            Raylib.DrawEllipse((int)centerX, (int)(groundY - square * 0.035f),
                MathF.Max(5f, square * (0.16f - 0.04f * MathF.Sin(progress * MathF.PI))),
                MathF.Max(2f, square * (0.055f - 0.015f * MathF.Sin(progress * MathF.PI))),
                new Color(18, 24, 28, 115));
            DrawPieceAt(_animatedPiece, centerX, groundY, square, lift);
        }

        private static void DrawCapturedPiece(float boardX, float boardY, float square, float progress)
        {
            float fade = 1f - progress * progress;
            float centerX = boardX + (_animatedMove.ToX + 0.5f) * square;
            float groundY = boardY + (_animatedMove.ToY + 1f) * square;
            byte alpha = (byte)Math.Clamp((int)(255f * fade), 0, 255);
            DrawPieceAt(_capturedPiece, centerX, groundY, square, 0f, new Color(255, 255, 255, (int)alpha));
        }

        private static void DrawPieceAt(Piece piece, float centerX, float groundY, float square, float lift, Color? tint = null)
        {
            if (piece.IsEmpty) return;
            string color = piece.White ? "w" : "b";
            string key = $"{color}_{piece.Type.ToString().ToLowerInvariant()}";
            if (!_pieceTextures.TryGetValue(key, out Texture2D texture) || texture.Id == 0) return;
            float scale = square * 0.72f / texture.Width;
            float width = texture.Width * scale;
            float height = texture.Height * scale;
            float left = centerX - width / 2f;
            float bottom = groundY - lift;
            Raylib.DrawTexturePro(texture,
                new Rectangle(0, 0, texture.Width, texture.Height),
                new Rectangle(left, bottom - height, width, height),
                Vector2.Zero, 0f, tint ?? Color.White);
        }
    }
}