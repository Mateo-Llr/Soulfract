#nullable enable
using Raylib_cs;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Soulfract
{
    /// <summary>Edition clavier/souris partagee par les champs texte de l'interface.</summary>
    public static class TextInput
    {
        private static string? _activeId;
        private static int _cursor;
        private static int _anchor;
        private static bool _dragging;
        private static double _lastClickTime = -1;
        private static int _lastClickIndex = -1;
        private static readonly Stack<string> _undo = new();
        private static readonly Stack<string> _redo = new();
        private const int MaxHistory = 100;

        public static bool IsFocused(string id) => _activeId == id;

        public static void FocusAtMouse(string id, string text, Rectangle bounds)
        {
            Vector2 mouse = Raylib.GetMousePosition();
            if (!Raylib.CheckCollisionPointRec(mouse, bounds)) return;
            _activeId = id;
            _cursor = GetIndexAt(text, mouse.X - bounds.X);
            _anchor = _cursor;
            _dragging = false;
        }

        public static void Focus(string id, int textLength)
        {
            _activeId = id;
            _cursor = textLength;
            _anchor = _cursor;
            _dragging = false;
            _lastClickTime = -1;
            _lastClickIndex = -1;
            _undo.Clear();
            _redo.Clear();
        }

        public static void Reset(string? id = null)
        {
            if (id == null || _activeId == id)
            {
                _activeId = null;
                _cursor = 0;
                _anchor = 0;
                _dragging = false;
                _lastClickTime = -1;
                _lastClickIndex = -1;
                _undo.Clear();
                _redo.Clear();
            }
        }

        public static bool Update(ref string text, string id, Rectangle bounds, int maxLength, Func<char, bool>? accept = null)
        {
            Vector2 mouse = Raylib.GetMousePosition();
            bool inside = Raylib.CheckCollisionPointRec(mouse, bounds);

            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                if (inside)
                {
                    if (_activeId != id)
                    {
                        _activeId = id;
                        _cursor = text.Length;
                        _anchor = _cursor;
                    }
                    int clickedIndex = GetIndexAt(text, mouse.X - bounds.X);
                    double now = Raylib.GetTime();
                    if (_lastClickIndex == clickedIndex && now - _lastClickTime <= 0.35)
                    {
                        SelectWord(text, clickedIndex);
                        _lastClickTime = -1;
                        _lastClickIndex = -1;
                    }
                    else
                    {
                        _cursor = clickedIndex;
                        _anchor = _cursor;
                        _lastClickTime = now;
                        _lastClickIndex = clickedIndex;
                    }
                    _dragging = true;
                }
                else if (_activeId == id)
                {
                    Reset(id);
                }
            }

            if (_activeId != id)
                return false;

            if (_dragging && Raylib.IsMouseButtonDown(MouseButton.Left))
                _cursor = GetIndexAt(text, Math.Clamp(mouse.X - bounds.X, 0, bounds.Width));
            if (_dragging && Raylib.IsMouseButtonReleased(MouseButton.Left))
                _dragging = false;

            bool control = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
            int key = Raylib.GetKeyPressed();
            while (key != 0)
            {
                KeyboardKey pressed = (KeyboardKey)key;
                if (control && pressed == KeyboardKey.A)
                {
                    _anchor = 0;
                    _cursor = text.Length;
                }
                else if (control && pressed == KeyboardKey.Z)
                {
                    if (shift) Redo(ref text);
                    else Undo(ref text);
                }
                else if (control && pressed == KeyboardKey.Y)
                {
                    Redo(ref text);
                }
                else if (control && (pressed == KeyboardKey.C || pressed == KeyboardKey.X))
                {
                    CopySelection(text);
                    if (pressed == KeyboardKey.X)
                        DeleteSelection(ref text);
                }
                else if (control && pressed == KeyboardKey.V)
                {
                    string pasted;
                    unsafe
                    {
                        pasted = Marshal.PtrToStringAnsi((IntPtr)Raylib.GetClipboardText()) ?? "";
                    }
                    var filtered = new string(pasted.Where(character =>
                        character != '\r' && character != '\n' && (accept == null || accept(character))).ToArray());
                    ReplaceSelection(ref text, filtered, maxLength);
                }
                else if (pressed == KeyboardKey.Backspace)
                {
                    if (!DeleteSelection(ref text) && _cursor > 0)
                    {
                        int start = control ? FindWordStart(text, _cursor) : _cursor - 1;
                        RecordUndo(text);
                        text = text.Remove(start, _cursor - start);
                        _cursor = start;
                        _anchor = _cursor;
                    }
                }
                else if (pressed == KeyboardKey.Delete)
                {
                    if (!DeleteSelection(ref text) && _cursor < text.Length)
                    {
                        int end = control ? FindWordEnd(text, _cursor) : _cursor + 1;
                        RecordUndo(text);
                        text = text.Remove(_cursor, end - _cursor);
                    }
                }
                else if (pressed == KeyboardKey.Left)
                {
                    MoveCursor(control ? FindWordStart(text, _cursor) : _cursor - 1, shift, text.Length, true);
                }
                else if (pressed == KeyboardKey.Right)
                {
                    MoveCursor(control ? FindWordEnd(text, _cursor) : _cursor + 1, shift, text.Length, true);
                }
                else if (pressed == KeyboardKey.Home)
                {
                    SetCursor(0, shift);
                }
                else if (pressed == KeyboardKey.End)
                {
                    SetCursor(text.Length, shift);
                }
                key = Raylib.GetKeyPressed();
            }

            int characterCode = Raylib.GetCharPressed();
            while (characterCode != 0)
            {
                string character = char.ConvertFromUtf32(characterCode);
                if (!char.IsControl(character, 0) && character.Length == 1 && (accept == null || accept(character[0])))
                    ReplaceSelection(ref text, character, maxLength);
                characterCode = Raylib.GetCharPressed();
            }

            return true;
        }

        /// <summary>Draws the editable content, including selection and caret.</summary>
        public static void DrawSingleLine(string text, string id, Rectangle bounds, int fontSize,
            Color textColor, Color selectionColor, Color caretColor, int padding = 8,
            string prefix = "", string placeholder = "")
        {
            bool focused = IsFocused(id);
            string visible = string.IsNullOrEmpty(text) && !focused ? placeholder : text;
            int textX = (int)bounds.X + padding + FontManager.MeasureText(prefix, fontSize);
            int textY = (int)bounds.Y + Math.Max(0, ((int)bounds.Height - fontSize) / 2);
            int availableWidth = Math.Max(1, (int)bounds.Width - padding * 2 - FontManager.MeasureText(prefix, fontSize));
            int visibleStart = 0;
            if (focused)
            {
                while (visibleStart < _cursor
                    && FontManager.MeasureText(text[visibleStart..Math.Clamp(_cursor, 0, text.Length)], fontSize) > availableWidth)
                    visibleStart++;
            }
            string visibleText = text[visibleStart..];

            Raylib.BeginScissorMode((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
            if (!string.IsNullOrEmpty(prefix))
                FontManager.DrawText(prefix, (int)bounds.X + padding, textY, fontSize, textColor);

            int start = focused ? SelectionStart : -1;
            int length = focused ? SelectionLength : 0;
            if (length > 0 && start + length > visibleStart)
            {
                int visibleSelectionStart = Math.Max(start, visibleStart);
                int visibleSelectionEnd = Math.Min(text.Length, start + length);
                int selectionX = textX + FontManager.MeasureText(text[visibleStart..visibleSelectionStart], fontSize);
                int selectionWidth = FontManager.MeasureText(text[visibleSelectionStart..visibleSelectionEnd], fontSize);
                Raylib.DrawRectangle(selectionX, textY, Math.Max(1, selectionWidth), fontSize + 2, selectionColor);
            }

            FontManager.DrawText(string.IsNullOrEmpty(text) ? visible : visibleText, textX, textY, fontSize,
                string.IsNullOrEmpty(text) && !focused ? new Color(textColor.R, textColor.G, textColor.B, (byte)150) : textColor);

            if (focused && (int)(Raylib.GetTime() * 2) % 2 == 0)
            {
                int caretX = textX + FontManager.MeasureText(text[visibleStart..Math.Clamp(_cursor, 0, text.Length)], fontSize);
                Raylib.DrawRectangle(caretX, textY - 1, 2, fontSize + 4, caretColor);
            }
            Raylib.EndScissorMode();
        }

        private static int GetIndexAt(string text, float x)
        {
            if (x <= 0) return 0;
            for (int index = 1; index <= text.Length; index++)
            {
                float width = FontManager.MeasureText(text[..index], 16);
                if (x < width - FontManager.MeasureText(text[index - 1].ToString(), 16) / 2f)
                    return index - 1;
            }
            return text.Length;
        }

        private static void MoveCursor(int position, bool extend, int length, bool absolute)
        {
            _cursor = Math.Clamp(absolute ? position : _cursor + position, 0, length);
            if (!extend) _anchor = _cursor;
        }

        private static int FindWordStart(string text, int position)
        {
            position = Math.Clamp(position, 0, text.Length);
            while (position > 0 && char.IsWhiteSpace(text[position - 1])) position--;
            while (position > 0 && !char.IsWhiteSpace(text[position - 1])) position--;
            return position;
        }

        private static int FindWordEnd(string text, int position)
        {
            position = Math.Clamp(position, 0, text.Length);
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            while (position < text.Length && !char.IsWhiteSpace(text[position])) position++;
            return position;
        }

        private static void SelectWord(string text, int position)
        {
            if (text.Length == 0)
            {
                _cursor = _anchor = 0;
                return;
            }
            position = Math.Clamp(position, 0, text.Length - 1);
            if (char.IsWhiteSpace(text[position]))
            {
                _anchor = position;
                _cursor = FindWordEnd(text, position);
            }
            else
            {
                _anchor = FindWordStart(text, position + 1);
                _cursor = FindWordEnd(text, position);
            }
        }

        private static void SetCursor(int position, bool extend)
        {
            _cursor = position;
            if (!extend) _anchor = _cursor;
        }

        private static bool HasSelection => _cursor != _anchor;
        private static int SelectionStart => Math.Min(_cursor, _anchor);
        private static int SelectionLength => Math.Abs(_cursor - _anchor);

        private static void CopySelection(string text)
        {
            if (HasSelection)
                Raylib.SetClipboardText(text.Substring(SelectionStart, SelectionLength));
        }

        private static bool DeleteSelection(ref string text)
        {
            return DeleteSelection(ref text, true);
        }

        private static void ReplaceSelection(ref string text, string replacement, int maxLength)
        {
            if (HasSelection || replacement.Length > 0)
                RecordUndo(text);
            DeleteSelection(ref text, false);
            int available = maxLength - text.Length;
            if (available <= 0) return;
            replacement = replacement.Length <= available ? replacement : replacement[..available];
            text = text.Insert(_cursor, replacement);
            _cursor += replacement.Length;
            _anchor = _cursor;
            _redo.Clear();
        }

        private static bool DeleteSelection(ref string text, bool record)
        {
            if (!HasSelection) return false;
            if (record) RecordUndo(text);
            text = text.Remove(SelectionStart, SelectionLength);
            _cursor = SelectionStart;
            _anchor = _cursor;
            _redo.Clear();
            return true;
        }

        private static void RecordUndo(string text)
        {
            if (_undo.Count == 0 || _undo.Peek() != text)
            {
                _undo.Push(text);
                while (_undo.Count > MaxHistory)
                {
                    var items = _undo.Take(MaxHistory).ToArray();
                    _undo.Clear();
                    for (int index = items.Length - 1; index >= 0; index--)
                        _undo.Push(items[index]);
                }
            }
            _redo.Clear();
        }

        private static void Undo(ref string text)
        {
            if (_undo.Count == 0) return;
            _redo.Push(text);
            text = _undo.Pop();
            _cursor = _anchor = text.Length;
        }

        private static void Redo(ref string text)
        {
            if (_redo.Count == 0) return;
            _undo.Push(text);
            text = _redo.Pop();
            _cursor = _anchor = text.Length;
        }
    }
}
