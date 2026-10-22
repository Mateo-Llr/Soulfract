// OptionsMenu.cs
// Menu d'options : refonte complète du rendu (anciennement DrawOptionsMenu dans Program.Core.cs).
//
// Principes :
//  - Interface proportionnelle à la taille de la fenêtre (facteur "ui" + S()).
//  - Panneau central de largeur bornée (plus de contrôles étirés sur tout l'écran).
//  - Chaque onglet est défilable (molette) via un seul mécanisme de scroll.
//  - Widgets réutilisables : section, ligne, slider, interrupteur, sélecteur segmenté, bouton.
//  - Un dialogue modal bloque réellement les clics sur ce qu'il y a derrière.
//
// L'état externe (_optionsTab, _masterVolume, _fullscreen, _rebindingAction,
// _showOptionsDiscardConfirmation, _voiceVolumeSliderChanged, ...) et les fonctions
// RequestOptionsBack / AcceptOptionsChanges / DiscardOptionsChanges / ApplyFullscreenToggle
// restent ceux déjà définis ailleurs : rien à changer côté logique.
#nullable enable
using Raylib_cs;
using System.Linq;
using System.Numerics;

namespace Soulfract
{
	public static partial class Program
	{
		// ───────────────────────── État propre au menu ─────────────────────────
		private static float _optUiScroll;
		private static int _optUiContentH;
		private static int _optUiLastTab = -1;
		private static int _optUiActiveSlider = -1;
		private static int _optUiSliderCounter;
		private static float _optUiSliderDragOffset;

		// ───────────────────────── Palette ─────────────────────────
		private static readonly Color OptPanelBg      = new Color(16, 15, 21, 232);
		private static readonly Color OptPanelBorder  = new Color(110, 92, 58, 220);
		private static readonly Color OptRowBg        = new Color(30, 29, 38, 190);
		private static readonly Color OptRowHoverBg   = new Color(44, 41, 52, 220);
		private static readonly Color OptRowBorder    = new Color(72, 66, 58, 150);
		private static readonly Color OptText         = new Color(236, 228, 208, 255);
		private static readonly Color OptTextDim      = new Color(158, 152, 140, 255);
		private static readonly Color OptGold         = new Color(235, 195, 105, 255);
		private static readonly Color OptGoldDim      = new Color(190, 160, 95, 255);
		private static readonly Color OptSelectedBg   = new Color(100, 80, 45, 240);
		private static readonly Color OptTrackBg      = new Color(12, 12, 16, 255);
		private static readonly Color OptOn           = new Color(96, 168, 98, 255);
		private static readonly Color OptOff          = new Color(72, 68, 78, 255);

		// ───────────────────────── Contexte de dessin ─────────────────────────
		private sealed class OptCtx
		{
			public Vector2 Mouse;
			public Rectangle Clip;   // zone visible du contenu (scissor)
			public float Ui;         // facteur d'échelle
			public bool Blocked;     // true si un dialogue modal est ouvert
			public int X;            // bord gauche du contenu
			public int W;            // largeur du contenu
			public float Y;          // curseur vertical courant (déjà décalé par le scroll)

			public int S(float v) => Math.Max(1, (int)MathF.Round(v * Ui));
		}

		private static Color OptLighten(Color col, int amount)
			=> new Color((byte)Math.Min(255, col.R + amount), (byte)Math.Min(255, col.G + amount), (byte)Math.Min(255, col.B + amount), col.A);

		private static bool OptHover(OptCtx c, Rectangle r)
			=> !c.Blocked
			   && Raylib.CheckCollisionPointRec(c.Mouse, r)
			   && Raylib.CheckCollisionPointRec(c.Mouse, c.Clip);

		private static string OptFitText(string text, int size, int maxW)
		{
			if (maxW <= 0 || FontManager.MeasureText(text, size) <= maxW) return text;
			string t = text;
			while (t.Length > 1 && FontManager.MeasureText(t + "...", size) > maxW)
				t = t.Substring(0, t.Length - 1);
			return t + "...";
		}

		private static void OptDrawCentered(string text, Rectangle r, int size, Color color)
		{
			int w = FontManager.MeasureText(text, size);
			FontManager.DrawText(text, (int)(r.X + (r.Width - w) / 2f), (int)(r.Y + (r.Height - size) / 2f), size, color);
		}

		// ───────────────────────── Widgets ─────────────────────────

		/// <summary>Titre de section avec filet de séparation.</summary>
		private static void OptSection(OptCtx c, string title, bool first = false)
		{
			if (!first) c.Y += c.S(16);
			int fs = c.S(15);
			FontManager.DrawText(title.ToUpperInvariant(), c.X + c.S(4), (int)c.Y, fs, OptGoldDim);
			Raylib.DrawRectangle(c.X, (int)c.Y + fs + c.S(6), c.W, 1, new Color(110, 92, 58, 130));
			c.Y += fs + c.S(16);
		}

		/// <summary>
		/// Ligne standard : fond, libellé (+ description optionnelle) à gauche,
		/// zone "ctrl" réservée au contrôle à droite.
		/// </summary>
		private static Rectangle OptRow(OptCtx c, string label, string? desc, int ctrlW, out Rectangle ctrl)
		{
			int fullH = desc != null ? c.S(68) : c.S(56);
			Rectangle row = new Rectangle(c.X, c.Y, c.W, fullH - c.S(6));
			bool hover = OptHover(c, row);
			Raylib.DrawRectangleRounded(row, 0.14f, 6, hover ? OptRowHoverBg : OptRowBg);
			Raylib.DrawRectangleRoundedLines(row, 0.14f, 6, 1, hover ? new Color(125, 105, 70, 190) : OptRowBorder);

			int cw = Math.Min(ctrlW, (int)(row.Width * 0.55f));
			ctrl = new Rectangle(row.X + row.Width - cw - c.S(18), row.Y, cw, row.Height);

			int textMaxW = (int)(ctrl.X - row.X - c.S(36));
			int labelSize = c.S(21);
			if (desc != null)
			{
				FontManager.DrawText(OptFitText(label, labelSize, textMaxW), (int)row.X + c.S(18), (int)row.Y + c.S(10), labelSize, OptText);
				int ds = c.S(14);
				FontManager.DrawText(OptFitText(desc, ds, textMaxW), (int)row.X + c.S(18), (int)row.Y + c.S(10) + labelSize + c.S(5), ds, OptTextDim);
			}
			else
			{
				FontManager.DrawText(OptFitText(label, labelSize, textMaxW), (int)row.X + c.S(18), (int)(row.Y + (row.Height - labelSize) / 2f), labelSize, OptText);
			}

			c.Y += fullH;
			return row;
		}

		/// <summary>Slider entier [0..max]. Le glisser continue même si la souris sort du slider.</summary>
		private static int OptSlider(OptCtx c, Rectangle ctrl, int value, int max, out bool changed)
		{
			int id = _optUiSliderCounter++;
			changed = false;
			value = Math.Clamp(value, 0, max);

			Rectangle track = new Rectangle(ctrl.X + c.S(10), ctrl.Y + ctrl.Height / 2f - c.S(5), ctrl.Width - c.S(20), c.S(22));
			Rectangle hit = new Rectangle(track.X - c.S(12), track.Y - c.S(14), track.Width + c.S(24), track.Height + c.S(28));
			bool hover = OptHover(c, hit);

			if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left) && _optUiActiveSlider == -1)
			{
				float thumbCenterX = track.X + track.Width * (max > 0 ? value / (float)max : 0f);
				_optUiSliderDragOffset = Math.Abs(c.Mouse.X - thumbCenterX) <= c.S(15)
					? c.Mouse.X - thumbCenterX
					: 0f;
				_optUiActiveSlider = id;
			}

			bool active = _optUiActiveSlider == id;
			if (active)
			{
				if (Raylib.IsMouseButtonDown(MouseButton.Left))
				{
					float ratio = Math.Clamp((c.Mouse.X - _optUiSliderDragOffset - track.X) / track.Width, 0f, 1f);
					int nv = (int)Math.Round(ratio * max);
					if (nv != value) { value = nv; changed = true; }
				}
				else
				{
					_optUiActiveSlider = -1;
					active = false;
				}
			}

			float fill = max > 0 ? value / (float)max : 0f;
			bool hasTrackTextures = _optionsSlideBarTop.Id != 0
				&& _optionsSlideBarMid.Id != 0
				&& _optionsSlideBarBottom.Id != 0;
			if (hasTrackTextures)
			{
				float capWidth = Math.Min(track.Height, track.Width / 2f);
				OptDrawRotatedSliderSegment(_optionsSlideBarBottom,
					new Rectangle(track.X, track.Y, capWidth, track.Height));
				OptDrawRotatedSliderSegment(_optionsSlideBarMid,
					new Rectangle(track.X + capWidth, track.Y, track.Width - capWidth * 2f, track.Height));
				OptDrawRotatedSliderSegment(_optionsSlideBarTop,
					new Rectangle(track.X + track.Width - capWidth, track.Y, capWidth, track.Height));
			}
			else
			{
				Raylib.DrawRectangleRounded(track, 0.5f, 8, OptTrackBg);
				if (fill > 0f)
					Raylib.DrawRectangleRounded(new Rectangle(track.X, track.Y, Math.Max(track.Height, track.Width * fill), track.Height), 0.5f, 8, new Color(210, 170, 90, 255));
				Raylib.DrawRectangleRoundedLines(track, 0.5f, 8, 1, new Color(100, 90, 70, 200));
			}

			float kx = track.X + track.Width * fill;
			float ky = track.Y + track.Height / 2f - c.S(5);
			Texture2D thumb = _optionsSliderHorizontal;
			if (thumb.Id != 0)
			{
				float thumbW = c.S(30), thumbH = c.S(15);
				Rectangle thumbRect = new Rectangle(kx - thumbW / 2f, ky - thumbH / 2f, thumbW, thumbH);
				Color thumbTint = active
					? new Color(205, 205, 205, 255)
					: hover ? new Color(255, 255, 230, 255) : Color.White;
				Raylib.DrawTexturePro(thumb, new Rectangle(0, 0, thumb.Width, thumb.Height),
					thumbRect, new Vector2(thumbW / 2f, thumbH / 2f), 90f, thumbTint);
			}
			else
			{
				float kr = c.S(active || hover ? 11 : 9);
				Raylib.DrawCircle((int)kx, (int)ky, kr + 2, new Color(0, 0, 0, 120));
				Raylib.DrawCircle((int)kx, (int)ky, kr, active ? new Color(255, 240, 190, 255) : new Color(255, 225, 150, 255));
			}
			return value;
		}

		private static void OptDrawRotatedSliderSegment(Texture2D texture, Rectangle horizontalRect)
		{
			if (texture.Id == 0 || horizontalRect.Width <= 0f || horizontalRect.Height <= 0f)
				return;

			int tileCount = Math.Max(1, (int)MathF.Round(horizontalRect.Width / horizontalRect.Height));
			float tileWidth = horizontalRect.Width / tileCount;
			for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
			{
				float x = horizontalRect.X + tileIndex * tileWidth;
				Rectangle destination = new Rectangle(
					x + (tileWidth - horizontalRect.Height) / 2f,
					horizontalRect.Y + (horizontalRect.Height - tileWidth) / 2f,
					horizontalRect.Height,
					tileWidth);
				Raylib.DrawTexturePro(texture, new Rectangle(0, 0, texture.Width, texture.Height), destination,
					new Vector2(destination.Width / 2f, destination.Height / 2f), 90f, Color.White);
			}
		}

		/// <summary>Interrupteur On/Off. Toute la ligne est cliquable. Renvoie true si on vient de cliquer.</summary>
		private static bool OptSwitch(OptCtx c, Rectangle row, Rectangle ctrl, bool on)
		{
			int w = c.S(54), h = c.S(28);
			Rectangle pill = new Rectangle(ctrl.X + ctrl.Width - w, ctrl.Y + (ctrl.Height - h) / 2f, w, h);
			bool hover = OptHover(c, row);

			Texture2D toggle = on ? _buttonToggleOn : _buttonToggleOff;
			if (toggle.Id != 0)
			{
				float scale = Math.Min(pill.Width / toggle.Width, pill.Height / toggle.Height);
				Rectangle dst = new Rectangle(
					pill.X + (pill.Width - toggle.Width * scale) / 2f,
					pill.Y + (pill.Height - toggle.Height * scale) / 2f,
					toggle.Width * scale,
					toggle.Height * scale);
				Raylib.DrawTexturePro(toggle, new Rectangle(0, 0, toggle.Width, toggle.Height), dst,
					Vector2.Zero, 0f, hover ? new Color(255, 255, 230, 230) : Color.White);
			}
			else
			{
				Color bg = on ? OptOn : OptOff;
				if (hover) bg = OptLighten(bg, 18);
				Raylib.DrawRectangleRounded(pill, 1f, 10, bg);
				Raylib.DrawRectangleRoundedLines(pill, 1f, 10, 1, new Color(20, 20, 20, 160));

				float kr = h / 2f - c.S(4);
				float kx = on ? pill.X + pill.Width - h / 2f : pill.X + h / 2f;
				Raylib.DrawCircle((int)kx, (int)(pill.Y + h / 2f), kr, new Color(250, 244, 230, 255));
			}

			string state = on
				? Localization.GetOrDefault("options.enabled", "ON")
				: Localization.GetOrDefault("options.disabled", "OFF");
			int fs = c.S(15);
			int tw = FontManager.MeasureText(state, fs);
			FontManager.DrawText(state, (int)(pill.X - tw - c.S(12)), (int)(pill.Y + (h - fs) / 2f), fs, on ? new Color(170, 225, 170, 255) : OptTextDim);

			return hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
		}

		/// <summary>Sélecteur segmenté. Renvoie l'index cliqué, ou -1.</summary>
		private static int OptSegmented(OptCtx c, Rectangle ctrl, string[] labels, int selected)
		{
			int h = c.S(38);
			Rectangle bar = new Rectangle(ctrl.X, ctrl.Y + (ctrl.Height - h) / 2f, ctrl.Width, h);
			Raylib.DrawRectangleRounded(bar, 0.25f, 8, OptTrackBg);
			Raylib.DrawRectangleRoundedLines(bar, 0.25f, 8, 1, new Color(100, 90, 70, 200));

			int clicked = -1;
			float segW = bar.Width / labels.Length;
			for (int i = 0; i < labels.Length; i++)
			{
				Rectangle seg = new Rectangle(bar.X + i * segW + 2, bar.Y + 2, segW - 4, bar.Height - 4);
				bool hover = OptHover(c, seg);
				bool sel = i == selected;
				if (sel)
				{
					Raylib.DrawRectangleRounded(seg, 0.25f, 8, OptSelectedBg);
					Raylib.DrawRectangleRoundedLines(seg, 0.25f, 8, 1, OptGold);
				}
				else if (hover)
				{
					Raylib.DrawRectangleRounded(seg, 0.25f, 8, new Color(60, 56, 66, 220));
				}
				int fs = c.S(16);
				string txt = OptFitText(labels[i], fs, (int)seg.Width - c.S(12));
				OptDrawCentered(txt, seg, fs, sel ? new Color(255, 240, 200, 255) : OptText);
				if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left)) clicked = i;
			}
			return clicked;
		}

		/// <summary>Bouton plat. Renvoie true si cliqué.</summary>
		private static bool OptButton(OptCtx c, Rectangle r, string text, Color baseColor, bool enabled = true)
		{
			bool hover = enabled && OptHover(c, r);
			Color bg = hover ? OptLighten(baseColor, 25) : baseColor;
			Raylib.DrawRectangleRounded(r, 0.25f, 8, bg);
			Raylib.DrawRectangleRoundedLines(r, 0.25f, 8, 1, new Color(190, 170, 130, 180));
			int fs = c.S(17);
			OptDrawCentered(OptFitText(text, fs, (int)r.Width - c.S(14)), r, fs, enabled ? OptText : OptTextDim);
			return hover && Raylib.IsMouseButtonPressed(MouseButton.Left);
		}

		// ───────────────────────── Menu principal ─────────────────────────

		static void DrawOptionsMenu()
		{
			int sw = Raylib.GetScreenWidth();
			int sh = Raylib.GetScreenHeight();
			Vector2 mousePos = Raylib.GetMousePosition();
			float dt = Raylib.GetFrameTime();

			bool modal = _showOptionsDiscardConfirmation; // état au début de la frame

			float ui = Math.Clamp(MathF.Min(sw / 1600f, sh / 900f), 0.7f, 1.35f);
			int S(float v) => Math.Max(1, (int)MathF.Round(v * ui));

			_optUiSliderCounter = 0;

			// ── Fond ──
			DrawScrollingMenuBackground(sw, sh, dt);
			Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 70));

			// ── Titre ──
			int titleSize = S(44);
			int titleY = S(18);
			string optionsTitle = Localization.Get("menu.options");
			int tw = FontManager.MeasureText(optionsTitle, titleSize);
			FontManager.DrawTextOutlined(optionsTitle, sw / 2 - tw / 2, titleY, titleSize, Color.White, Color.Black);

			// ── Géométrie du panneau ──
			int panelW = Math.Min(sw - S(40), S(1040));
			int panelX = (sw - panelW) / 2;
			int tabH = S(46);
			int tabY = titleY + titleSize + S(16);
			int panelTop = tabY + tabH + S(8);
			int bottomBarH = S(92);
			int panelBottom = sh - bottomBarH;
			int panelH = Math.Max(S(120), panelBottom - panelTop);
			Rectangle panelRect = new Rectangle(panelX, panelTop, panelW, panelH);

			// ── Onglets (alignés sur la largeur du panneau) ──
			string[] tabNames =
			{
				Localization.Get("menu.general"),
				Localization.Get("menu.controls"),
				Localization.Get("menu.interface"),
				Localization.GetOrDefault("menu.voice", "VOICE")
			};
			int tabGap = S(8);
			int tabW = (panelW - tabGap * (tabNames.Length - 1)) / tabNames.Length;
			for (int i = 0; i < tabNames.Length; i++)
			{
				Rectangle tabRect = new Rectangle(panelX + i * (tabW + tabGap), tabY, tabW, tabH);
				bool hovered = !modal && Raylib.CheckCollisionPointRec(mousePos, tabRect);
				bool active = _optionsTab == i;

				Color bg = active ? new Color(66, 54, 32, 240) : (hovered ? new Color(46, 43, 54, 235) : new Color(26, 25, 33, 215));
				Raylib.DrawRectangleRounded(tabRect, 0.22f, 8, bg);
				Raylib.DrawRectangleRoundedLines(tabRect, 0.22f, 8, 1, active ? OptGold : new Color(88, 80, 66, 200));
				if (active)
					Raylib.DrawRectangle((int)tabRect.X + S(14), (int)(tabRect.Y + tabRect.Height - S(4)), (int)tabRect.Width - S(28), S(3), OptGold);

				int fs = S(20);
				OptDrawCentered(OptFitText(tabNames[i], fs, tabW - S(16)), tabRect, fs, active ? new Color(255, 236, 175, 255) : OptText);

				if (hovered && Raylib.IsMouseButtonPressed(MouseButton.Left))
					_optionsTab = i;
			}

			if (_optionsTab != _optUiLastTab)
			{
				_optUiLastTab = _optionsTab;
				_optUiScroll = 0f;
				_optUiActiveSlider = -1;
				_rebindingAction = null;
			}

			// ── Panneau ──
			Raylib.DrawRectangleRounded(panelRect, 0.03f, 10, OptPanelBg);

			// ── Scroll (molette) ──
			int viewH = panelH - S(16);
			int maxScroll = Math.Max(0, _optUiContentH - viewH);
			if (!modal && Raylib.CheckCollisionPointRec(mousePos, panelRect))
			{
				float wheel = Raylib.GetMouseWheelMove();
				if (wheel != 0f) _optUiScroll -= wheel * S(54);
			}
			_optUiScroll = Math.Clamp(_optUiScroll, 0f, maxScroll);

			// ── Contenu de l'onglet ──
			var c = new OptCtx
			{
				Mouse = mousePos,
				Ui = ui,
				Blocked = modal,
				Clip = new Rectangle(panelX + 2, panelTop + S(8), panelW - 4, viewH),
				X = panelX + S(22),
				W = panelW - S(44) - (maxScroll > 0 ? S(10) : 0),
			};
			float startY = panelTop + S(12);
			c.Y = startY - _optUiScroll;

			Raylib.BeginScissorMode((int)c.Clip.X, (int)c.Clip.Y, (int)c.Clip.Width, (int)c.Clip.Height);
			switch (_optionsTab)
			{
				case 0: OptDrawGeneralTab(c); break;
				case 1: OptDrawControlsTab(c); break;
				case 2: OptDrawInterfaceTab(c); break;
				case 3: OptDrawVoiceTab(c); break;
			}
			Raylib.EndScissorMode();

			_optUiContentH = (int)(c.Y + _optUiScroll - startY) + S(12);

			// ── Barre de défilement ──
			if (maxScroll > 0)
			{
				float trackH = viewH;
				float thumbH = Math.Max(S(36), trackH * viewH / _optUiContentH);
				float thumbY = c.Clip.Y + (trackH - thumbH) * (_optUiScroll / maxScroll);
				Rectangle thumb = new Rectangle(panelX + panelW - S(14), thumbY, S(6), thumbH);
				Raylib.DrawRectangleRounded(new Rectangle(thumb.X, c.Clip.Y, thumb.Width, trackH), 0.5f, 4, new Color(255, 255, 255, 18));
				Raylib.DrawRectangleRounded(thumb, 0.5f, 4, new Color(210, 190, 140, 200));
			}

			// ── Bordure du panneau (par-dessus le contenu) ──
			Raylib.DrawRectangleRoundedLines(panelRect, 0.03f, 10, 2, OptPanelBorder);

			// ── Bandeau d'aide pendant l'assignation d'une touche ──
			if (_rebindingAction != null)
			{
				string hint = Localization.GetOrDefault("options.rebind_hint",
					"Appuyez sur une touche, un bouton ou une direction de manette (Echap pour annuler)");
				int hs = S(18);
				int hw = FontManager.MeasureText(hint, hs);
				Rectangle hintRect = new Rectangle(sw / 2f - hw / 2f - S(24), panelBottom - S(58), hw + S(48), S(42));
				Raylib.DrawRectangleRounded(hintRect, 0.4f, 8, new Color(60, 46, 20, 245));
				Raylib.DrawRectangleRoundedLines(hintRect, 0.4f, 8, 2, OptGold);
				OptDrawCentered(hint, hintRect, hs, new Color(255, 225, 140, 255));
			}

			// ── Boutons Retour / Accepter ──
			int btnW = S(250), btnH = S(58), btnGap = S(18);
			int btnY = sh - bottomBarH + (bottomBarH - btnH) / 2;
			Rectangle backRect = new Rectangle(sw / 2f - btnW - btnGap / 2f, btnY, btnW, btnH);
			Rectangle acceptRect = new Rectangle(sw / 2f + btnGap / 2f, btnY, btnW, btnH);
			bool backHovered = !modal && Raylib.CheckCollisionPointRec(mousePos, backRect);
			bool acceptHovered = !modal && Raylib.CheckCollisionPointRec(mousePos, acceptRect);
			UIManager.DrawButton(backRect, new Color(185, 75, 65, 255), backHovered, true);
			UIManager.DrawButton(acceptRect, new Color(75, 165, 90, 255), acceptHovered, true);
			int bfs = S(24);
			OptDrawCentered(Localization.Get("options.back"), backRect, bfs, Color.White);
			OptDrawCentered(Localization.Get("options.accept"), acceptRect, bfs, Color.White);
			if (backHovered && Raylib.IsMouseButtonPressed(MouseButton.Left)) RequestOptionsBack();
			if (acceptHovered && Raylib.IsMouseButtonPressed(MouseButton.Left)) AcceptOptionsChanges();

			// ── Sauvegarde des sliders voix au relâchement ──
			if (_voiceVolumeSliderChanged && Raylib.IsMouseButtonReleased(MouseButton.Left))
			{
				SettingsManager.Save();
				_voiceVolumeSliderChanged = false;
			}

			// ── Dialogue de confirmation (modal) ──
			if (_showOptionsDiscardConfirmation)
				DrawOptionsDiscardDialog(sw, sh, mousePos, modal, S);
		}

		private static void DrawOptionsDiscardDialog(int sw, int sh, Vector2 mousePos, bool wasModal, Func<float, int> S)
		{
			Raylib.DrawRectangle(0, 0, sw, sh, new Color(0, 0, 0, 170));
			int dialogW = Math.Min(S(560), sw - 40);
			int dialogH = S(200);
			Rectangle dialog = new Rectangle(sw / 2f - dialogW / 2f, sh / 2f - dialogH / 2f, dialogW, dialogH);
			Raylib.DrawRectangleRounded(dialog, 0.08f, 8, new Color(24, 22, 28, 252));
			Raylib.DrawRectangleRoundedLines(dialog, 0.08f, 8, 2, new Color(190, 75, 65, 240));

			string warning = Localization.GetOrDefault("options.discard.warning", "Des modifications n'ont pas été validées.");
			string question = Localization.GetOrDefault("options.discard.question", "Quitter sans les enregistrer ?");
			int ws = S(20), qs = S(17);
			FontManager.DrawText(warning, sw / 2 - FontManager.MeasureText(warning, ws) / 2, (int)dialog.Y + S(30), ws, new Color(255, 220, 150, 255));
			FontManager.DrawText(question, sw / 2 - FontManager.MeasureText(question, qs) / 2, (int)dialog.Y + S(64), qs, Color.White);

			int bw = (dialogW - S(30) * 2 - S(16)) / 2;
			int bh = S(46);
			Rectangle cancelRect = new Rectangle(dialog.X + S(30), dialog.Y + dialogH - bh - S(26), bw, bh);
			Rectangle discardRect = new Rectangle(cancelRect.X + bw + S(16), cancelRect.Y, bw, bh);
			bool cancelHovered = Raylib.CheckCollisionPointRec(mousePos, cancelRect);
			bool discardHovered = Raylib.CheckCollisionPointRec(mousePos, discardRect);
			UIManager.DrawButton(cancelRect, new Color(75, 165, 90, 255), cancelHovered, true);
			UIManager.DrawButton(discardRect, new Color(185, 75, 65, 255), discardHovered, true);
			int fs = S(18);
			OptDrawCentered(Localization.GetOrDefault("options.discard.cancel", "ANNULER"), cancelRect, fs, Color.White);
			OptDrawCentered(Localization.GetOrDefault("options.discard.confirm", "QUITTER"), discardRect, fs, Color.White);

			// On n'agit que si le dialogue était déjà ouvert au début de la frame
			// (évite qu'un clic qui vient de l'ouvrir ne le referme aussitôt).
			if (!wasModal) return;
			if (cancelHovered && Raylib.IsMouseButtonPressed(MouseButton.Left)) _showOptionsDiscardConfirmation = false;
			if (discardHovered && Raylib.IsMouseButtonPressed(MouseButton.Left)) DiscardOptionsChanges();
		}

		// ───────────────────────── Onglet 0 : Général ─────────────────────────

		private static void OptDrawGeneralTab(OptCtx c)
		{
			// ── Audio ──
			OptSection(c, Localization.GetOrDefault("options.section.audio", "Audio"), first: true);
			OptRow(c, string.Format(Localization.Get("options.volume"), _masterVolume), null, c.S(460), out var volCtrl);
			int newVol = OptSlider(c, volCtrl, _masterVolume, 100, out bool volChanged);
			if (volChanged)
			{
				_masterVolume = newVol;
				Raylib.SetMasterVolume(_masterVolume / 100f);
			}

			// ── Affichage ──
			OptSection(c, Localization.GetOrDefault("options.section.display", "Affichage"));
			var fsRow = OptRow(c, Localization.Get("options.fullscreen"),
				Localization.GetOrDefault("options.fullscreen.desc", "Basculer entre le mode fenêtré et le plein écran."),
				c.S(220), out var fsCtrl);
			if (OptSwitch(c, fsRow, fsCtrl, _fullscreen))
				ApplyFullscreenToggle();

			// ── Langue ──
			OptSection(c, Localization.Get("options.language"));
			int count = Localization.AvailableLanguages.Count;
			int gap = c.S(10);
			int cardH = c.S(60);
			int cols = Math.Max(1, (c.W + gap) / (c.S(210) + gap));
			float cardW = (c.W - gap * (cols - 1)) / (float)cols;

			for (int i = 0; i < count; i++)
			{
				int rowIdx = i / cols, colIdx = i % cols;
				Rectangle card = new Rectangle(c.X + colIdx * (cardW + gap), c.Y + rowIdx * (cardH + gap), cardW, cardH);
				var entry = Localization.AvailableLanguages[i];
				bool hover = OptHover(c, card);
				bool selected = i == _languageSelectionIndex;

				Raylib.DrawRectangleRounded(card, 0.18f, 8, selected ? OptSelectedBg : (hover ? OptRowHoverBg : OptRowBg));
				Raylib.DrawRectangleRoundedLines(card, 0.18f, 8, selected ? 2 : 1, selected ? OptGold : OptRowBorder);

				// Drapeau (ratio conservé)
				float flagBoxW = c.S(56), flagBoxH = cardH - c.S(18);
				float flagX = card.X + c.S(12);
				if (_languageFlagTextures.TryGetValue(entry.Code, out var flag) && flag.Id != 0)
				{
					float sc = Math.Min(flagBoxW / flag.Width, flagBoxH / flag.Height);
					float dw = flag.Width * sc, dh = flag.Height * sc;
					Rectangle dst = new Rectangle(flagX + (flagBoxW - dw) / 2f, card.Y + (card.Height - dh) / 2f, dw, dh);
					Raylib.DrawTexturePro(flag, new Rectangle(0, 0, flag.Width, flag.Height), dst, Vector2.Zero, 0f, Color.White);
					Raylib.DrawRectangleLinesEx(dst, 1, new Color(0, 0, 0, 140));
				}

				string name = Localization.GetOrDefault("language.name." + entry.Code, entry.Code.ToUpperInvariant());
				int fs = c.S(19);
				int textX = (int)(flagX + flagBoxW + c.S(14));
				FontManager.DrawText(OptFitText(name, fs, (int)(card.X + card.Width - textX - c.S(10))), textX,
					(int)(card.Y + (card.Height - fs) / 2f), fs, selected ? new Color(255, 236, 175, 255) : OptText);

				if (hover && Raylib.IsMouseButtonPressed(MouseButton.Left) && _languageSelectionIndex != i)
				{
					_languageSelectionIndex = i;
					Localization.SetLanguage(entry.Code);
				}
			}
			int rows = (count + cols - 1) / cols;
			c.Y += rows * (cardH + gap);
		}

		// ───────────────────────── Onglet 1 : Contrôles ─────────────────────────

		private static void OptDrawControlsTab(OptCtx c)
		{
			var categories = new (string key, string fallback, Color accent, GameAction[] actions)[]
			{
				("options.cat.movement", "Déplacement", new Color(110, 170, 235, 255), new[]
				{
					GameAction.MoveUp, GameAction.MoveDown, GameAction.MoveLeft, GameAction.MoveRight, GameAction.Sprint
				}),
				("options.cat.actions", "Actions", new Color(235, 160, 90, 255), new[]
				{
					GameAction.Interact, GameAction.Drop
				}),
				("options.cat.interface", "Interface", new Color(185, 140, 235, 255), new[]
				{
					GameAction.Inventory, GameAction.Character, GameAction.RadialMenu, GameAction.PauseMenu,
					GameAction.MagicMenu, GameAction.GuildMenu, GameAction.Map, GameAction.Bestiary,
					GameAction.ItemsMenu, GameAction.EmoteWheel
				}),
				("options.cat.gameplay", "Gameplay", new Color(235, 190, 100, 255), new[]
				{
					GameAction.QuickSave, GameAction.ReloadData, GameAction.ToggleDebug, GameAction.TimeSkip,
					GameAction.SelectStructureCorner1, GameAction.SelectStructureCorner2, GameAction.Fullscreen,
					GameAction.ArmorStandPose, GameAction.WagonLink, GameAction.BoatRow, GameAction.CarMode
				}),
				("options.cat.hotbar", "Barre d'objets", new Color(120, 205, 180, 255), new[]
				{
					GameAction.HotbarSlot1, GameAction.HotbarSlot2, GameAction.HotbarSlot3, GameAction.HotbarSlot4,
					GameAction.HotbarSlot5, GameAction.HotbarSlot6, GameAction.HotbarSlot7, GameAction.HotbarSlot8,
					GameAction.HotbarSlot9, GameAction.HotbarSlot10
				}),
				("options.cat.communication", "Communication", new Color(120, 205, 130, 255), new[]
				{
					GameAction.ChatToggle, GameAction.PushToTalk
				}),
			};

			int rowH = c.S(62);
			int keySize = c.S(46);
			int keyGap = c.S(8);
			int addW = c.S(40);
			bool canInteract = _rebindingAction == null;

			for (int catIndex = 0; catIndex < categories.Length; catIndex++)
			{
				var category = categories[catIndex];
				if (catIndex > 0) c.Y += c.S(6);

				// ── Carte de catégorie : bandeau coloré + lignes regroupées ──
				int headerH = c.S(42);
				int cardPad = c.S(6);
				Color accent = category.accent;
				Rectangle card = new Rectangle(c.X, c.Y, c.W, headerH + category.actions.Length * rowH - c.S(6) + cardPad);
				Raylib.DrawRectangleRounded(card, 0.035f, 8, new Color(20, 19, 26, 215));

				Rectangle header = new Rectangle(card.X, card.Y, card.Width, headerH);
				Raylib.DrawRectangleRounded(header, 0.35f, 8, new Color(accent.R, accent.G, accent.B, (byte)46));
				Raylib.DrawRectangle((int)header.X + c.S(10), (int)header.Y + c.S(9), c.S(4), headerH - c.S(18), accent);

				string catTitle = Localization.GetOrDefault(category.key, category.fallback).ToUpperInvariant();
				int catFs = c.S(20);
				FontManager.DrawText(catTitle, (int)header.X + c.S(26), (int)(header.Y + (headerH - catFs) / 2f), catFs, accent);

				string catCount = category.actions.Length.ToString();
				int cntFs = c.S(15);
				int cntW = FontManager.MeasureText(catCount, cntFs);
				FontManager.DrawText(catCount, (int)(header.X + header.Width - cntW - c.S(20)), (int)(header.Y + (headerH - cntFs) / 2f), cntFs, OptTextDim);

				Raylib.DrawRectangleRoundedLines(card, 0.035f, 8, 1, new Color(accent.R, accent.G, accent.B, (byte)110));
				c.Y += headerH;
				int actionIndex = 0;

				foreach (var action in category.actions)
				{
					Rectangle row = new Rectangle(c.X + c.S(8), c.Y, c.W - c.S(16), rowH - c.S(6));
					bool rowHover = OptHover(c, row);
					if (rowHover)
						Raylib.DrawRectangleRounded(row, 0.14f, 6, new Color(accent.R, accent.G, accent.B, (byte)26));
					if (actionIndex < category.actions.Length - 1)
						Raylib.DrawRectangle((int)row.X + c.S(10), (int)(row.Y + row.Height + c.S(3)), (int)row.Width - c.S(20), 1, new Color(255, 255, 255, 18));

					var bindings = KeyBindings.GetBindings(action);
					int bindingsW = bindings.Count > 0 ? bindings.Count * (keySize + keyGap) - keyGap : 0;
					float rightEdge = row.X + row.Width - c.S(16);
					float addX = rightEdge - addW;
					float bindStartX = addX - (bindings.Count > 0 ? keyGap : 0) - bindingsW;

					int ls = c.S(21);
					FontManager.DrawText(OptFitText(KeyBindings.DisplayName(action), ls, (int)(bindStartX - row.X - c.S(32))),
						(int)row.X + c.S(18), (int)(row.Y + (row.Height - ls) / 2f), ls, OptText);

					float bx = bindStartX;
					for (int bi = 0; bi < bindings.Count; bi++)
					{
						var binding = bindings[bi];
						Rectangle keyRect = new Rectangle(bx, row.Y + (row.Height - keySize) / 2f, keySize, keySize);
						bool isRebinding = _rebindingAction?.action == action && _rebindingAction?.index == bi;
						bool keyHover = OptHover(c, keyRect);

						Texture2D tex = binding.Type == InputType.Keyboard ? World.GetInputButtonTexture()
							: binding.Type == InputType.GamepadButton ? GetGamepadButtonTexture((GamepadButton)binding.Code)
							: binding.Type == InputType.GamepadAxis ? GetGamepadJoystickTexture((GamepadAxisDirection)binding.Code)
							: binding.Type == InputType.Mouse ? (MouseButton)binding.Code switch
							{
								MouseButton.Left => _lmouseButtonTex,
								MouseButton.Right => _rmouseButtonTex,
								MouseButton.Middle => _wmouseButtonTex,
								_ => new Texture2D()
							} : new Texture2D();

						Color tint = isRebinding ? new Color(255, 225, 150, 255) : keyHover ? new Color(255, 235, 190, 255) : Color.White;
						if (isRebinding)
							Raylib.DrawRectangleRounded(new Rectangle(keyRect.X - 3, keyRect.Y - 3, keyRect.Width + 6, keyRect.Height + 6), 0.2f, 6, new Color(255, 200, 90, 90));
						if (tex.Id != 0 && tex.Width > 0 && tex.Height > 0)
							Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), keyRect, Vector2.Zero, 0, tint);

						bool isKeyboard = binding.Type == InputType.Keyboard;
						string label = isKeyboard ? ((KeyboardKey)binding.Code switch
						{
							KeyboardKey.LeftShift or KeyboardKey.RightShift => "MAJ",
							KeyboardKey.LeftControl or KeyboardKey.RightControl => "CTRL",
							KeyboardKey.Space => "ESPACE",
							KeyboardKey.Escape => "ECHAP",
							KeyboardKey.Enter => "ENTREE",
							KeyboardKey.Tab => "TAB",
							_ => ((KeyboardKey)binding.Code).ToString().ToUpperInvariant()
						}) : binding.Type == InputType.GamepadAxis ? KeyBindings.BindingDisplayName(binding) : string.Empty;
						if (label.Length > 0 && (isKeyboard || binding.Type == InputType.GamepadAxis))
						{
							int fs = c.S(label.Length > 5 ? 12 : 18);
							OptDrawCentered(label, keyRect, fs, Color.White);
						}

						if (bi > 0)
						{
							Rectangle rm = new Rectangle(keyRect.X + keyRect.Width - c.S(8), keyRect.Y - c.S(8), c.S(18), c.S(18));
							bool rmHover = OptHover(c, rm);
							Raylib.DrawCircle((int)(rm.X + rm.Width / 2), (int)(rm.Y + rm.Height / 2), rm.Width / 2f, rmHover ? new Color(235, 95, 75, 255) : new Color(150, 65, 55, 245));
							OptDrawCentered("x", rm, c.S(12), Color.White);
							if (rmHover && canInteract && Raylib.IsMouseButtonPressed(MouseButton.Left))
								KeyBindings.RemoveBinding(action, bi);
						}

						if (keyHover && canInteract && Raylib.IsMouseButtonPressed(MouseButton.Left))
							_rebindingAction = (action, bi);

						bx += keySize + keyGap;
					}

					Rectangle addRect = new Rectangle(addX, row.Y + (row.Height - c.S(38)) / 2f, addW, c.S(38));
					bool addHover = OptHover(c, addRect);
					Raylib.DrawRectangleRounded(addRect, 0.22f, 6, addHover ? new Color(80, 120, 70, 245) : new Color(42, 72, 48, 230));
					Raylib.DrawRectangleRoundedLines(addRect, 0.22f, 6, 1, new Color(135, 170, 115, 190));
					OptDrawCentered("+", addRect, c.S(24), new Color(215, 245, 195, 255));
					if (addHover && canInteract && Raylib.IsMouseButtonPressed(MouseButton.Left))
						_rebindingAction = (action, KeyBindings.AddBinding(action));

					c.Y += rowH;
					actionIndex++;
				}
				c.Y += cardPad;
			}

			// ── Réinitialisation ──
			OptSection(c, Localization.GetOrDefault("options.section.reset", "Réinitialisation"));
			OptRow(c, Localization.GetOrDefault("options.controls.reset_label", "Touches par défaut"),
				Localization.GetOrDefault("options.controls.reset_desc", "Remet toutes les touches assignables à leur valeur d'origine."),
				c.S(250), out var resetCtrl);
			Rectangle resetBtn = new Rectangle(resetCtrl.X, resetCtrl.Y + (resetCtrl.Height - c.S(38)) / 2f, resetCtrl.Width, c.S(38));
			if (OptButton(c, resetBtn, Localization.GetOrDefault("options.controls.reset", "Réinitialiser"), new Color(96, 52, 46, 230), canInteract))
				KeyBindings.ResetToDefaults();

			// ── Touches fixes ──
			OptSection(c, Localization.GetOrDefault("options.section.fixed_keys", "Touches fixes"));
			var fixedControls = new (string action, string key)[]
			{
				(Localization.GetOrDefault("options.fixed.zoom", "Zoom"), "MAJ + Molette"),
			};
			foreach (var (action, key) in fixedControls)
			{
				Rectangle row = new Rectangle(c.X, c.Y, c.W, c.S(40) - c.S(6));
				Raylib.DrawRectangleRounded(row, 0.18f, 6, new Color(24, 23, 30, 150));
				int fs = c.S(17);
				FontManager.DrawText(action, (int)row.X + c.S(18), (int)(row.Y + (row.Height - fs) / 2f), fs, OptTextDim);
				int kw = FontManager.MeasureText(key, fs);
				FontManager.DrawText(key, (int)(row.X + row.Width - kw - c.S(18)), (int)(row.Y + (row.Height - fs) / 2f), fs, OptGoldDim);
				c.Y += c.S(40);
			}
		}

		// ───────────────────────── Onglet 2 : Interface ─────────────────────────

		private static void OptDrawInterfaceTab(OptCtx c)
		{
			var settings = SettingsManager.Settings;

			// ── Équipement ──
			OptSection(c, Localization.GetOrDefault("options.section.equipment", "Inventaire"), first: true);
			var equipmentStyles = new (string label, string value)[]
			{
				(Localization.GetOrDefault("options.ui.equip.character", "Personnage"), "character"),
				(Localization.GetOrDefault("options.ui.equip.character_slots", "Personnage + cases"), "character_with_slots"),
				(Localization.GetOrDefault("options.ui.equip.only", "Équipements seuls"), "equipment_only"),
			};
			OptRow(c, Localization.GetOrDefault("options.ui.equipment_style", "Affichage de l'équipement"), null, c.S(560), out var eqCtrl);
			int eqSel = Array.FindIndex(equipmentStyles, s => s.value == settings.InventoryEquipmentStyle);
			int eqClick = OptSegmented(c, eqCtrl, equipmentStyles.Select(s => s.label).ToArray(), eqSel);
			if (eqClick >= 0 && eqClick != eqSel)
			{
				settings.InventoryEquipmentStyle = equipmentStyles[eqClick].value;
				SettingsManager.Save();
			}

			// ── Hotbar ──
			OptSection(c, Localization.GetOrDefault("options.section.hotbar", "Barre d'objets"));
			var hotbarStyles = new (string label, string value)[]
			{
				(Localization.GetOrDefault("options.ui.hotbar.radial", "Circulaire"), "radial"),
				(Localization.GetOrDefault("options.ui.hotbar.classic", "Classique"), "classic"),
			};
			OptRow(c, Localization.GetOrDefault("options.ui.hotbar_style", "Style de la hotbar"), null, c.S(360), out var hbCtrl);
			int hbSel = Array.FindIndex(hotbarStyles, s => s.value == settings.HotbarStyle);
			int hbClick = OptSegmented(c, hbCtrl, hotbarStyles.Select(s => s.label).ToArray(), hbSel);
			if (hbClick >= 0 && hbClick != hbSel)
			{
				settings.HotbarStyle = hotbarStyles[hbClick].value;
				SettingsManager.Save();
			}

			// ── Boutons rapides ──
			OptSection(c, Localization.GetOrDefault("options.section.quick_buttons", "Boutons d'interface rapide"));
			var quickButtons = new (string label, bool current, Action<bool> setter)[]
			{
				(Localization.GetOrDefault("options.ui.btn.inventory", "Inventaire"),        settings.ShowInventoryButton, v => settings.ShowInventoryButton = v),
				(Localization.GetOrDefault("options.ui.btn.craft", "Artisanat"),             settings.ShowCraftButton,     v => settings.ShowCraftButton = v),
				(Localization.GetOrDefault("options.ui.btn.guild", "Guilde"),                settings.ShowGuildButton,     v => settings.ShowGuildButton = v),
				(Localization.GetOrDefault("options.ui.btn.quests", "Journal de quêtes"),    settings.ShowQuestButton,     v => settings.ShowQuestButton = v),
				(Localization.GetOrDefault("options.ui.btn.map", "Carte"),                   settings.ShowMapButton,       v => settings.ShowMapButton = v),
				(Localization.GetOrDefault("options.ui.btn.bestiary", "Bestiaire"),          settings.ShowBestiaryButton,  v => settings.ShowBestiaryButton = v),
				(Localization.GetOrDefault("options.ui.btn.items", "Items (godmode)"),       settings.ShowItemsButton,     v => settings.ShowItemsButton = v),
			};
			foreach (var q in quickButtons)
			{
				var row = OptRow(c, q.label, null, c.S(220), out var ctrl);
				if (OptSwitch(c, row, ctrl, q.current))
				{
					q.setter(!q.current);
					SettingsManager.Save();
				}
			}
		}

		// ───────────────────────── Onglet 3 : Voix ─────────────────────────

		private static void OptDrawVoiceTab(OptCtx c)
		{
			var settings = SettingsManager.Settings;

			// ── Mode du micro ──
			OptSection(c, Localization.GetOrDefault("options.section.microphone", "Microphone"), first: true);
			var voiceModes = new (string value, string label)[]
			{
				("Disabled",   Localization.GetOrDefault("options.voice.disabled", "Microphone off")),
				("PushToTalk", Localization.GetOrDefault("options.voice.push_to_talk", "Push to talk")),
				("AlwaysOn",   Localization.GetOrDefault("options.voice.always_on", "Always on")),
			};
			OptRow(c, Localization.GetOrDefault("options.voice_mode", "Microphone mode"), null, c.S(560), out var modeCtrl);
			int sel = Array.FindIndex(voiceModes, m => m.value == settings.VoiceChatMode);
			int click = OptSegmented(c, modeCtrl, voiceModes.Select(m => m.label).ToArray(), sel);
			if (click >= 0 && click != sel)
			{
				settings.VoiceChatMode = voiceModes[click].value;
				SettingsManager.Save();
			}

			// ── Volumes ──
			OptSection(c, Localization.GetOrDefault("options.section.voice_volume", "Volumes"));

			int input = Math.Clamp(settings.VoiceInputVolume, 0, 300);
			OptRow(c, string.Format(Localization.GetOrDefault("options.voice.input_volume", "Microphone input: {0}%"), input), null, c.S(460), out var inCtrl);
			int newIn = OptSlider(c, inCtrl, input, 300, out bool inChanged);
			if (inChanged) { settings.VoiceInputVolume = newIn; _voiceVolumeSliderChanged = true; }

			int output = Math.Clamp(settings.VoiceOutputVolume, 0, 300);
			OptRow(c, string.Format(Localization.GetOrDefault("options.voice.output_volume", "Voice output: {0}%"), output), null, c.S(460), out var outCtrl);
			int newOut = OptSlider(c, outCtrl, output, 300, out bool outChanged);
			if (outChanged) { settings.VoiceOutputVolume = newOut; _voiceVolumeSliderChanged = true; }
		}
	}
}
