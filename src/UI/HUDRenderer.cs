// HudRenderer.cs - Interface HUD stylisée
using Raylib_cs;
using System.Numerics;
using System.Collections.Generic;

namespace Soulfract
{
    public static class HudRenderer
    {
        private static float _hpPulseTimer = 0f;
        private static float _hpBarFlash = 0f;
        
        // Couleurs du thème
        private static readonly Color COLOR_HP_BG = new Color(40, 20, 20, 200);
        private static readonly Color COLOR_HP_FILL = new Color(220, 60, 50, 255);
        private static readonly Color COLOR_HP_FILL_CRITICAL = new Color(255, 80, 70, 255);
        private static readonly Color COLOR_HP_BORDER = new Color(180, 100, 80, 200);
        private static readonly Color COLOR_TEXT_GOLD = new Color(230, 200, 100, 255);
        private static readonly Color COLOR_TEXT_WHITE = new Color(255, 255, 255, 220);
        private static readonly Color COLOR_PANEL_BG = new Color(15, 18, 22, 200);
        
        // Endurance (vert)
        private static readonly Color COLOR_STAMINA_FILL = new Color(80, 210, 90, 255);
        private static readonly Color COLOR_STAMINA_FILL_LOCKED = new Color(90, 100, 90, 255); // grisé quand vidée (en attente de recharge complète)
        
        // Faim (orange/brun) et soif (bleu)
        private static readonly Color COLOR_HUNGER_FILL = new Color(210, 140, 60, 255);
        private static readonly Color COLOR_THIRST_FILL = new Color(70, 160, 230, 255);
        
        // Textures pour le HUD
        private static Texture2D _heartIcon;
        private static Texture2D _panelCorner;
		private static Texture2D _hotIcon;
		private static Texture2D _coldIcon;
        private static bool _texturesLoaded = false;

        //  BOOSTS TEMPORAIRES : icônes "info_{effet}_boost.png" chargées à la demande et mises
        // en cache par nom d'effet (ex: "speed" -> assets/gui/info_speed_boost.png).
        private static readonly Dictionary<string, Texture2D> _boostIcons = new(StringComparer.OrdinalIgnoreCase);
        private const int BOOST_ICON_SIZE = 28;
        private const int BOOST_ICON_GAP = 6;
        
        // ===== Health bar en nine slice + shader de remplissage =====
        private static NineSliceTexture _hpBarNineSlice;
        private static Shader _hpBarShader;
        private static bool _hpBarShaderLoaded = false;
        private static int _locBarX;
        private static int _locBarWidth;
        private static int _locHealthPercent;
        private static int _locFillColor;
        private static int _locPreviousPercent;
        
        // Ajuste la taille des bords du nine slice (voir DrawHealthBarNineSlice)
        private const float HP_BAR_SCALE = 4f;
        
        //  Barre d'endurance : réutilise EXACTEMENT le même nine-slice + shader
        // que la barre de vie (même assets assets/gui/healthbar_*), seule la
        // couleur de remplissage (uniform fillColor) et la taille changent.
        private const float STAMINA_BAR_SCALE_RATIO = 0.8f; // légèrement plus petite que la vie
        
        //  Bulles de faim / soif (barres verticales) — dessinées SANS étirement,
        // à taille réduite, respectant le ratio natif de la texture bubble_bar.
        private static VerticalBubbleBar _hungerBubble;
        private static VerticalBubbleBar _thirstBubble;
        private const float SMALL_BUBBLE_HEIGHT = 46f; // hauteur cible, largeur déduite du ratio natif
        
        //  Grosse bulle décorative centrale (même texture bubble_bar, en plus grand),
        // sur laquelle viennent se "brancher" vie/endurance/faim/soif. Ne représente
        // aucune jauge pour l'instant.
        private static VerticalBubbleBar _mainBubble;
        private const float MAIN_BUBBLE_HEIGHT = 150f;
        
        //  Effet "dégât différé" sur la barre de vie : la zone perdue reste
        // affichée en BLANC un court instant avant de disparaître progressivement,
        // au lieu de sauter instantanément à la nouvelle valeur.
        private static float _hpWhiteBoundary = 1f;   // bord blanc (0..1), toujours >= hpPercent
        private static float _hpHoldTimer = 0f;        // temps restant avant que le blanc commence à rétrécir
        private static float _hpLastPercent = 1f;      // valeur réelle vue à la frame précédente (détecte un nouveau coup)
        private const float HP_FLASH_HOLD_DURATION = 0.4f; // durée d'affichage en blanc avant rétrécissement
        private const float HP_DRAIN_SPEED = 0.5f;         // vitesse de rétrécissement (fraction de barre / seconde)
        
        public static void LoadTextures()
        {
            _heartIcon = LoadIcon("assets/gui/heart_icon.png");
            _panelCorner = LoadIcon("assets/gui/panel_corner.png");
            _texturesLoaded = _heartIcon.Id != 0;
            
            // Nine slice de la health bar : assets/gui/healthbar_top1.png, _top2.png, ..., _bottom3.png
            _hpBarNineSlice = new NineSliceTexture("assets/gui/healthbar");
            
            if (File.Exists("assets/shaders/healthbar.fs"))
            {
                _hpBarShader = Raylib.LoadShader(null, "assets/shaders/healthbar.fs");
                _hpBarShaderLoaded = _hpBarShader.Id != 0;
                if (_hpBarShaderLoaded)
                {
                    _locBarX = Raylib.GetShaderLocation(_hpBarShader, "barX");
                    _locBarWidth = Raylib.GetShaderLocation(_hpBarShader, "barWidth");
                    _locHealthPercent = Raylib.GetShaderLocation(_hpBarShader, "healthPercent");
                    _locFillColor = Raylib.GetShaderLocation(_hpBarShader, "fillColor");
                    _locPreviousPercent = Raylib.GetShaderLocation(_hpBarShader, "previousPercent");
                }
            }
            
            // Bulles de faim / soif : même texture "bubble_bar", même shader de
            // remplissage vertical, seule la couleur de remplissage diffère.
            _hungerBubble = new VerticalBubbleBar("assets/gui/bubble_bar.png", "assets/shaders/bubblebar.fs");
            _thirstBubble = new VerticalBubbleBar("assets/gui/bubble_bar.png", "assets/shaders/bubblebar.fs");
            
			//  Grosse bulle décorative centrale : même asset, en plus grand, pas de shader.
			_mainBubble = new VerticalBubbleBar("assets/gui/bubble_bar.png", "assets/shaders/bubblebar.fs");

			// Icônes températures (hot/cold)
			_hotIcon = LoadIcon("assets/gui/hot.png");
			_coldIcon = LoadIcon("assets/gui/cold.png");
        }
        
        private static Texture2D LoadIcon(string path)
        {
            if (File.Exists(path))
                return Raylib.LoadTexture(path);
            return new Texture2D();
        }
        
        public static void Draw(int currentHp, int maxHp, float currentStamina, float maxStamina, bool staminaLocked, float currentHunger, float maxHunger, float currentThirst, float maxThirst, Item? heldItem)
		{
			float dt = Raylib.GetFrameTime();
			
			_hpPulseTimer += dt;
			float hpPercent = (float)currentHp / maxHp;
			
			if (_hpBarFlash > 0)
				_hpBarFlash -= dt * 3f;
			
			//    NOUVELLE DISPOSITION "CONNECTÉE"   
			// Tout part d'une grosse bulle décorative (mêmes assets bubble_bar) sur
			// laquelle les autres jauges viennent se "brancher" en se superposant
			// légèrement, façon interface organique.
			
			int originX = 24;
			int originY = 24;
			
			bool mainBubbleOk = _mainBubble != null && _mainBubble.IsValid;
			
			// --- 1) Grosse bulle centrale : on calcule sa géométrie tout de suite
			//        (les autres éléments s'alignent dessus), mais on la DESSINE
			//        en tout dernier, par-dessus vie/endurance, pour donner
			//        l'impression qu'elles rentrent dedans plutôt que l'inverse. ---
			Rectangle mainRect;
			if (mainBubbleOk)
			{
				mainRect = _mainBubble.GetFitRect(originX, originY, MAIN_BUBBLE_HEIGHT);
			}
			else
			{
				float r0 = MAIN_BUBBLE_HEIGHT / 2f;
				mainRect = new Rectangle(originX, originY, r0 * 2f, r0 * 2f);
			}
			
			// --- 2) Barre de vie : "branchée" sur le bord droit de la bulle, en
			//        chevauchement léger pour donner l'impression qu'elle en sort ---
			int barW = 260;
			int barH = 40;
			const int HEALTH_OVERLAP_X = 22; // chevauchement horizontal avec la bulle
			int barX = (int)(mainRect.X + mainRect.Width) - HEALTH_OVERLAP_X;
			int barY = (int)(mainRect.Y + mainRect.Height * 0.18f);
			
			// --- 3) Barre d'endurance : juste en dessous, légèrement chevauchée
			//        verticalement avec la vie, et plus petite ---
			int staminaBarW = (int)(barW * STAMINA_BAR_SCALE_RATIO);
			int staminaBarH = (int)(barH * STAMINA_BAR_SCALE_RATIO);
			const int STAMINA_OVERLAP_Y = 8; // chevauchement vertical avec la barre de vie
			int staminaBarX = barX;
			int staminaBarY = barY + barH - STAMINA_OVERLAP_Y;
			
			// --- 4) Bulles de faim / soif : "branchées" sous la grosse bulle, l'une
			//        légèrement à gauche, l'autre légèrement à droite, chevauchant
			//        son bord inférieur. Dessinées SANS étirement (ratio natif). ---
			const float BUBBLE_OVERLAP_RATIO = 0.35f; // proportion de la petite bulle qui remonte dans la grosse
			float hungerAspect = (_hungerBubble != null && _hungerBubble.IsValid) ? _hungerBubble.AspectRatio : 0.45f;
			float thirstAspect = (_thirstBubble != null && _thirstBubble.IsValid) ? _thirstBubble.AspectRatio : 0.45f;
			float hungerW = SMALL_BUBBLE_HEIGHT * hungerAspect;
			float thirstW = SMALL_BUBBLE_HEIGHT * thirstAspect;
			
			float bottomY = mainRect.Y + mainRect.Height;
			float hungerX = mainRect.X + mainRect.Width * 0.30f - hungerW / 2f;
			float thirstX = mainRect.X + mainRect.Width * 0.70f - thirstW / 2f;
			float smallBubbleY = bottomY - SMALL_BUBBLE_HEIGHT * BUBBLE_OVERLAP_RATIO;
			
			// Ordre de dessin : bulles faim/soif, puis endurance, puis vie, et enfin
			// la grosse bulle décorative PAR-DESSUS tout (elle "avale" les bords des
			// barres de vie/endurance/faim/soif qui viennent s'y connecter).
			float hungerPercent = maxHunger > 0 ? currentHunger / maxHunger : 0f;
			float thirstPercent = maxThirst > 0 ? currentThirst / maxThirst : 0f;
			DrawBubbleNoStretch(_hungerBubble, hungerX, smallBubbleY, hungerW, SMALL_BUBBLE_HEIGHT, hungerPercent, COLOR_HUNGER_FILL);
			DrawBubbleNoStretch(_thirstBubble, thirstX, smallBubbleY, thirstW, SMALL_BUBBLE_HEIGHT, thirstPercent, COLOR_THIRST_FILL);
			
			float staminaPercent = maxStamina > 0 ? currentStamina / maxStamina : 0f;
			DrawStaminaBarNineSlice(staminaBarX, staminaBarY, staminaBarW, staminaBarH, staminaPercent, staminaLocked);
			
			DrawHealthBarNineSlice(barX, barY, barW, barH, hpPercent, dt);
			
			// --- Grosse bulle décorative, dessinée en dernier -> passe AU-DESSUS
			//     des barres de vie/endurance et des bulles faim/soif. ---
			if (mainBubbleOk)
			{
				Color bubbleFillColor = GetRarityBubbleColor(heldItem);
				_mainBubble.Draw(mainRect, 1f, bubbleFillColor, Color.White);
			}
			else
			{
				float r = MAIN_BUBBLE_HEIGHT / 2f;
				Raylib.DrawCircle((int)(originX + r), (int)(originY + r), r, COLOR_HP_BG);
				Raylib.DrawCircleLines((int)(originX + r), (int)(originY + r), r, COLOR_HP_BORDER);
			}

			// Item tenu, centré dans la grosse bulle.
			if (heldItem != null)
			{
				float itemSize = MathF.Min(mainRect.Width, mainRect.Height) * 0.45f;
				float itemX = mainRect.X + (mainRect.Width - itemSize) / 2f;
				float itemY = mainRect.Y + (mainRect.Height - itemSize) / 2f;

				ItemRenderer.DrawItemPadded(heldItem, (int)itemX, (int)itemY, (int)itemSize, 4);
				if (Program.TryGetHeldRangedAmmoInfo(heldItem, out int currentAmmo, out int maxAmmo))
				{
					string ammoText = $"{currentAmmo}/{maxAmmo}";
					int ammoFontSize = 10;
					int ammoTextWidth = FontManager.MeasureText(ammoText, ammoFontSize);
					float ammoTextX = itemX + (itemSize - ammoTextWidth) / 2f;
					float ammoTextY = itemY + itemSize + 4f;
					FontManager.DrawText(ammoText, (int)ammoTextX, (int)ammoTextY, ammoFontSize, new Color(240, 230, 180, 255));
				}
			}

			//  BOOSTS TEMPORAIRES + TEMPÉRATURE : rangée d'icônes juste sous la barre
			// d'endurance (potions, poison, rassasiement, chaud/froid...). Toutes les
			// icônes actives sont désormais fusionnées dans UNE SEULE file qui
			// s'accumule selon leur nombre réel (avec retour à la ligne automatique
			// avant de sortir de l'écran), au lieu d'avoir chacune une position figée
			// qui provoquait des chevauchements dès que plusieurs effets étaient
			// actifs en même temps (ex: température qui recouvrait les boosts).
			// Dessinée en tout dernier pour ne jamais être masquée par la grosse bulle.
			int boostsStartY = (int)(staminaBarY + staminaBarH) + 6;
			int boostsMaxWidth = Raylib.GetScreenWidth() - staminaBarX - 24;
			DrawActiveInfoIcons(staminaBarX, boostsStartY, boostsMaxWidth);
        }

		public static void DrawClassicHotbar(int selectedSlot)
		{
			const int slotCount = 10;
			const int slotSize = 64;
			const int gap = -12;
			GetClassicHotbarLayout(slotCount, slotSize, gap, out int startX, out int startY, out int totalWidth);
			var slots = Program.GetHotbarSlotsCached();

			Raylib.DrawRectangleRounded(new Rectangle(startX - 10, startY - 10, totalWidth + 20, slotSize + 20), 0.12f, 6, new Color(12, 14, 18, 225));
			Raylib.DrawRectangleRoundedLines(new Rectangle(startX - 10, startY - 10, totalWidth + 20, slotSize + 20), 0.12f, 6, 2, new Color(150, 125, 75, 210));

			for (int drawPass = 0; drawPass < 2; drawPass++)
			{
				for (int index = 0; index < slotCount; index++)
				{
					bool selected = index == selectedSlot;
					if (selected != (drawPass == 1))
						continue;

					int x = startX + index * (slotSize + gap);
					Rectangle slotRect = new Rectangle(x, startY, slotSize, slotSize);
					Color fallbackColor = selected ? new Color(100, 110, 80, 255) : new Color(50, 53, 60, 255);
					UIManager.DrawItemSlotBackground(
						slotRect,
						selected,
						false,
						fallbackColor,
						borderUp: true,
						borderDown: true,
						borderLeft: index == 0,
						borderRight: index == slotCount - 1);
				}
			}

			for (int index = 0; index < slotCount; index++)
			{
				int x = startX + index * (slotSize + gap);
				if (index < slots.Count && !slots[index].IsEmpty && slots[index].Item != null)
					{
						ItemRenderer.DrawItemCompact(slots[index].Item, x + 7, startY + 7, slotSize - 14);
						if (slots[index].Count > 1)
						{
							string quantity = slots[index].Count.ToString();
							int quantityWidth = FontManager.MeasureText(quantity, 14);
							FontManager.DrawText(quantity, x + slotSize - quantityWidth - 4, startY + slotSize - 18, 14, Color.White);
						}
					}

					string key = index == 9 ? "0" : (index + 1).ToString();
					FontManager.DrawText(key, x + 4, startY + 3, 12, new Color(220, 215, 195, 220));
			}
		}

		public static Rectangle GetClassicHotbarBounds()
		{
			const int slotCount = 10;
			const int slotSize = 64;
			const int gap = -12;
			GetClassicHotbarLayout(slotCount, slotSize, gap, out int startX, out int startY, out int totalWidth);
			return new Rectangle(startX - 10, startY - 10, totalWidth + 20, slotSize + 20);
		}

		public static bool TryGetClassicHotbarSlot(Vector2 mousePosition, out int slotIndex)
		{
			const int slotCount = 10;
			const int slotSize = 64;
			const int gap = -12;
			GetClassicHotbarLayout(slotCount, slotSize, gap, out int startX, out int startY, out _);

			for (int index = 0; index < slotCount; index++)
			{
				int x = startX + index * (slotSize + gap);
				if (Raylib.CheckCollisionPointRec(mousePosition, new Rectangle(x, startY, slotSize, slotSize)))
				{
					slotIndex = index;
					return true;
				}
			}

			slotIndex = -1;
			return false;
		}

		private static void GetClassicHotbarLayout(int slotCount, int slotSize, int gap, out int startX, out int startY, out int totalWidth)
		{
			totalWidth = slotCount * slotSize + (slotCount - 1) * gap;
			startX = (Raylib.GetScreenWidth() - totalWidth) / 2;
			startY = Raylib.GetScreenHeight() - slotSize - 18;
		}

        /// <summary>Icône d'info générique pour la grille sous la barre d'endurance
        /// (boost temporaire ou indicateur de température) : texture, couleur de
        /// repli si la texture manque, et texte du tooltip associé.</summary>
        private struct HudInfoIcon
        {
            public Texture2D Texture;
            public Color FallbackColor;
            public string TooltipTitle;
            public string TooltipDetail;
        }

        /// <summary>
        /// Construit la liste de toutes les icônes d'info actuellement actives
        /// (boosts temporaires + température) et les affiche en grille sous la barre
        /// d'endurance : elles s'accumulent à la suite les unes des autres selon leur
        /// nombre réel, avec retour à la ligne automatique dès que la largeur
        /// disponible est atteinte, au lieu d'occuper chacune une position fixe
        /// (ce qui provoquait des chevauchements, notamment avec l'indicateur de
        /// température qui recouvrait les dernières icônes de boost).
        /// Charge à la demande la texture "assets/gui/info_{effet}_boost.png" pour
        /// chaque effet, et affiche un tooltip (nom + détail) au survol.
        /// </summary>
        private static void DrawActiveInfoIcons(int startX, int startY, int maxWidth)
        {
            var icons = new List<HudInfoIcon>();

            var boosts = Program.GetActiveBoosts();
            if (boosts != null)
            {
                foreach (var boost in boosts)
                {
                    icons.Add(new HudInfoIcon
                    {
                        Texture = GetBoostIcon(boost.Effect),
                        FallbackColor = new Color(120, 200, 255, 230),
                        TooltipTitle = GetBoostDisplayName(boost.Effect),
                        TooltipDetail = boost.Effect.ToLowerInvariant() switch
                        {
                            "crab_companion" => "Compagnon actif grâce à l'armure crabe.",
                            "invisibility" => $"Invisible encore {FormatBoostTime(boost.Remaining)}",
                            _ => $"Temps restant : {FormatBoostTime(boost.Remaining)}"
                        }
                    });
                }
            }

            bool isTempActive = TemperatureSystem.IsHot || TemperatureSystem.IsCold;
            if (isTempActive)
            {
                bool isHot = TemperatureSystem.IsHot;
                icons.Add(new HudInfoIcon
                {
                    Texture = isHot ? _hotIcon : _coldIcon,
                    FallbackColor = isHot ? new Color(255, 140, 60, 220) : new Color(140, 180, 255, 220),
                    TooltipTitle = Localization.Get(isHot ? "tooltip.hot" : "tooltip.cold"),
                    TooltipDetail = string.Format(Localization.Get("tooltip.temperature.detail"), TemperatureSystem.RawAmbientTemperature, TemperatureSystem.CurrentBiome.ToString())
                });
            }

            if (icons.Count == 0) return;

            Vector2 mousePos = Raylib.GetMousePosition();
            HudInfoIcon? hovered = null;
            Rectangle hoveredRect = default;

            int x = startX;
            int y = startY;
            int rowRightLimit = startX + Math.Max(maxWidth, BOOST_ICON_SIZE);

            foreach (var icon in icons)
            {
                // Retour à la ligne dès que l'icône suivante dépasserait la largeur
                // disponible (mais jamais sur la toute première icône d'une ligne,
                // pour ne pas créer de ligne vide si une seule icône est déjà trop large).
                if (x != startX && x + BOOST_ICON_SIZE > rowRightLimit)
                {
                    x = startX;
                    y += BOOST_ICON_SIZE + BOOST_ICON_GAP;
                }

                var rect = new Rectangle(x, y, BOOST_ICON_SIZE, BOOST_ICON_SIZE);

                if (icon.Texture.Id != 0)
                {
                    Raylib.DrawTexturePro(
                        icon.Texture,
                        new Rectangle(0, 0, icon.Texture.Width, icon.Texture.Height),
                        rect,
                        Vector2.Zero,
                        0f,
                        Color.White
                    );
                }
                else
                {
                    // Fallback si la texture est absente/manquante
                    Raylib.DrawCircle((int)(x + BOOST_ICON_SIZE / 2f), (int)(y + BOOST_ICON_SIZE / 2f), BOOST_ICON_SIZE / 2.2f, icon.FallbackColor);
                }

				if (Raylib.CheckCollisionPointRec(mousePos, rect))
				{
					hovered = icon;
					hoveredRect = rect;
				}

				x += BOOST_ICON_SIZE + BOOST_ICON_GAP;
			}

			if (hovered.HasValue)
				DrawInfoIconTooltip(hovered.Value, hoveredRect);
		}

		/// <summary>Charge (et met en cache) la texture d'icône d'un boost donné.</summary>
		private static Texture2D GetBoostIcon(string effectName)
		{
			if (_boostIcons.TryGetValue(effectName, out var cached))
				return cached;

            string iconPath = effectName.ToLowerInvariant() switch
            {
                "crab_companion" => "assets/gui/info_crab_companion.png",
                _ => $"assets/gui/info_{effectName}_boost.png"
            };

            var tex = LoadIcon(iconPath);
            _boostIcons[effectName] = tex;
            return tex;
        }

        /// <summary>Tooltip générique affiché au survol d'une icône d'info (boost
        /// temporaire ou température) : titre + détail (description ou temps restant).
        /// Remplace les deux anciennes fonctions dédiées (boost / température), qui
        /// faisaient exactement le même rendu, pour que chaque icône de la grille
        /// puisse afficher son tooltip quelle que soit sa position accumulée.</summary>
        private static void DrawInfoIconTooltip(HudInfoIcon icon, Rectangle iconRect)
        {
            int titleSize = 14;
            int detailSize = 12;
            int w1 = FontManager.MeasureText(icon.TooltipTitle, titleSize);
            int w2 = FontManager.MeasureText(icon.TooltipDetail, detailSize);
            int tooltipW = Math.Max(w1, w2) + 24;
            int tooltipH = 46;

            int tx = (int)(iconRect.X + iconRect.Width / 2f - tooltipW / 2f);
            int ty = (int)(iconRect.Y + iconRect.Height + 8);

            // Reste dans l'écran si on est trop près du bord gauche
            if (tx < 4) tx = 4;

            DrawPanel(tx, ty, tooltipW, tooltipH);
            FontManager.DrawText(icon.TooltipTitle, tx + 12, ty + 8, titleSize, COLOR_TEXT_GOLD);
            FontManager.DrawText(icon.TooltipDetail, tx + 12, ty + 26, detailSize, COLOR_TEXT_WHITE);
        }

        private static string GetBoostDisplayName(string effectName) => effectName.ToLowerInvariant() switch
        {
            "speed" => "Vitesse augmentée",
            "strength" => "Force augmentée",
            "resistance" => "Résistance aux dégâts",
            "light" => "Lumière",
            "heatresist" => "Résistance à la chaleur",
            "coldresist" => "Résistance au froid",
            "invisibility" => "Invisibilité",
            "satiety" => "Rassasié",
            "poison" => "Empoisonné",
            "crab_companion" => "Compagnon actif",
            _ => effectName
        };

        /// <summary>Formate un temps restant en "m:ss" (>= 1 min) ou "Xs" / "X.Ys" (< 1 min).</summary>
        private static string FormatBoostTime(float seconds)
        {
            float remaining = MathF.Max(0f, seconds);
            if (remaining >= 60f)
            {
                int total = (int)remaining;
                int minutes = total / 60;
                int secs = total % 60;
                return minutes > 0 ? $"{minutes}:{secs:00}" : $"{secs}s";
            }

            if (remaining >= 10f)
            {
                return $"{(int)remaining}s";
            }

            return $"{remaining:0.#}s";
        }

		private static Color GetRarityBubbleColor(Item? item)
		{
			if (item == null)
				return GameData.GetRarityColor("common");

			if (GameData.TryGetItemByName(item.Name, out var itemData))
			{
				return GameData.GetRarityColor(itemData.Rarity ?? "common");
			}

			return GameData.GetRarityColor("common");
		}

		private static void DrawBubbleNoStretch(VerticalBubbleBar bubble, float x, float y, float w, float h, float percent, Color fillColor)
		{
			if (bubble == null || !bubble.IsValid)
			{
				// Fallback simple si l'asset "bubble_bar.png" n'existe pas encore
				var rect = new Rectangle(x, y, w, h);
				Raylib.DrawRectangleRounded(rect, 0.5f, 8, COLOR_HP_BG);
				float fillH = h * Math.Clamp(percent, 0f, 1f);
				Raylib.DrawRectangleRounded(new Rectangle(x, y + h - fillH, w, fillH), 0.5f, 8, fillColor);
				Raylib.DrawRectangleRoundedLines(rect, 0.5f, 8, 2, COLOR_HP_BORDER);
				return;
			}
			
			var destRect = new Rectangle(x, y, w, h);
			bubble.Draw(destRect, percent, fillColor, Color.White);
		}
        
        private static void DrawHealthBarNineSlice(int barX, int barY, int barW, int barH, float hpPercent, float dt)
		{
			//  Met à jour le bord blanc "dégât en attente" : quand hpPercent chute
			// (dégât), le bord blanc reste figé sur l'ancienne valeur pendant
			// HP_FLASH_HOLD_DURATION, puis rétrécit progressivement jusqu'à
			// rejoindre hpPercent. En cas de soin (hpPercent augmente), il n'y a
			// pas de zone à faire disparaître : le bord blanc suit instantanément.
			if (hpPercent < _hpLastPercent - 0.0005f)
			{
				// Nouveau coup encaissé cette frame -> on relance le délai avant
				// que le blanc ne commence à rétrécir (sans faire de saut : le
				// bord blanc reste où il était).
				_hpHoldTimer = HP_FLASH_HOLD_DURATION;
			}
			
			if (hpPercent >= _hpWhiteBoundary)
			{
				// Pas (ou plus) de dégât en attente à afficher
				_hpWhiteBoundary = hpPercent;
				_hpHoldTimer = 0f;
			}
			else if (_hpHoldTimer > 0f)
			{
				_hpHoldTimer -= dt;
			}
			else
			{
				// Rétrécissement fluide du blanc vers la nouvelle valeur réelle
				float maxDelta = HP_DRAIN_SPEED * dt;
				if (_hpWhiteBoundary - hpPercent <= maxDelta)
					_hpWhiteBoundary = hpPercent;
				else
					_hpWhiteBoundary -= maxDelta;
			}
			_hpLastPercent = hpPercent;
			
			bool nineSliceOk = _hpBarNineSlice != null && _hpBarNineSlice.IsValid;
			
			if (!nineSliceOk || !_hpBarShaderLoaded)
			{
				// Fallback : ancien rendu simple si les assets sont absents
				Color fillColor = COLOR_HP_FILL;
				if (_hpBarFlash > 0)
					fillColor = new Color(255, 180, 100, 255);
				else if (hpPercent < 0.2f)
					fillColor = COLOR_HP_FILL_CRITICAL;
				
				Raylib.DrawRectangleRounded(new Rectangle(barX, barY, barW, barH), 0.3f, 8, COLOR_HP_BG);
				// Zone blanche "en attente de disparition" (si présente)
				if (_hpWhiteBoundary > hpPercent)
				{
					int whiteW = (int)(barW * _hpWhiteBoundary);
					if (whiteW > 0)
						Raylib.DrawRectangle(barX, barY, whiteW, barH, Color.White);
				}
				int fillW = (int)(barW * hpPercent);
				if (fillW > 0)
					Raylib.DrawRectangle(barX, barY, fillW, barH, fillColor);
				Raylib.DrawRectangleRoundedLines(new Rectangle(barX, barY, barW, barH), 0.3f, 8, 2, COLOR_HP_BORDER);
				return;
			}
			
			//  CALCUL D'UNE SCALE ADAPTATIVE
			// On veut que la hauteur totale = barH, mais sans écraser les textures
			// On calcule la scale nécessaire pour que la hauteur naturelle ≈ barH
			float naturalHeight = _hpBarNineSlice.GetNaturalHeight(HP_BAR_SCALE);
			
			// Si la hauteur demandée est plus petite que la hauteur naturelle,
			// on réduit proportionnellement la scale
			float effectiveScale = HP_BAR_SCALE;
			if (naturalHeight > barH && naturalHeight > 0)
			{
				effectiveScale = HP_BAR_SCALE * (barH / naturalHeight);
				// On applique un facteur de sécurité pour éviter que les bords
				// ne soient trop écrasés
				effectiveScale = Math.Max(effectiveScale, 1.0f);
			}
			
			// On applique la scale calculée
			_hpBarNineSlice.Scale = effectiveScale;
			
			// La hauteur réelle après scale
			float actualHeight = _hpBarNineSlice.GetNaturalHeight(effectiveScale);
			
			// On centre verticalement si la hauteur réelle est plus petite que barH
			float offsetY = (barH - actualHeight) / 2f;
			
			var destRect = new Rectangle(
				barX, 
				barY + offsetY, 
				barW, 
				actualHeight
			);
			
			// Le shader travaille en coordonnées écran
			float dpiRatio = Raylib.GetRenderWidth() / (float)Raylib.GetScreenWidth();
			float shaderBarX = barX * dpiRatio;
			float shaderBarWidth = barW * dpiRatio;
			
			Raylib.SetShaderValue(_hpBarShader, _locBarX, shaderBarX, ShaderUniformDataType.Float);
			Raylib.SetShaderValue(_hpBarShader, _locBarWidth, shaderBarWidth, ShaderUniformDataType.Float);
			Raylib.SetShaderValue(_hpBarShader, _locHealthPercent, hpPercent, ShaderUniformDataType.Float);
			Raylib.SetShaderValue(_hpBarShader, _locPreviousPercent, _hpWhiteBoundary, ShaderUniformDataType.Float);
			float[] redCol = { COLOR_HP_FILL.R / 255f, COLOR_HP_FILL.G / 255f, COLOR_HP_FILL.B / 255f };
			Raylib.SetShaderValue(_hpBarShader, _locFillColor, redCol, ShaderUniformDataType.Vec3);
			
			Raylib.BeginShaderMode(_hpBarShader);
			_hpBarNineSlice.Draw(destRect, Color.White);
			Raylib.EndShaderMode();
		}
		
		/// <summary>
		/// Barre d'endurance : réutilise le même nine-slice + shader que la barre de
		/// vie (mêmes assets assets/gui/healthbar_*), seule la couleur de remplissage
		/// (verte, ou grisée quand l'endurance est vidée et doit se recharger
		/// entièrement) et la taille changent.
		/// </summary>
		private static void DrawStaminaBarNineSlice(int barX, int barY, int barW, int barH, float staminaPercent, bool staminaLocked)
		{
			bool nineSliceOk = _hpBarNineSlice != null && _hpBarNineSlice.IsValid;
			
			if (!nineSliceOk || !_hpBarShaderLoaded)
			{
				Color fillColor = staminaLocked ? COLOR_STAMINA_FILL_LOCKED : COLOR_STAMINA_FILL;
				Raylib.DrawRectangleRounded(new Rectangle(barX, barY, barW, barH), 0.3f, 8, COLOR_HP_BG);
				int fillW = (int)(barW * staminaPercent);
				if (fillW > 0)
					Raylib.DrawRectangle(barX, barY, fillW, barH, fillColor);
				Raylib.DrawRectangleRoundedLines(new Rectangle(barX, barY, barW, barH), 0.3f, 8, 2, COLOR_HP_BORDER);
				return;
			}
			
			float naturalHeight = _hpBarNineSlice.GetNaturalHeight(HP_BAR_SCALE);
			float effectiveScale = HP_BAR_SCALE;
			if (naturalHeight > barH && naturalHeight > 0)
			{
				effectiveScale = HP_BAR_SCALE * (barH / naturalHeight);
				effectiveScale = Math.Max(effectiveScale, 1.0f);
			}
			
			_hpBarNineSlice.Scale = effectiveScale;
			float actualHeight = _hpBarNineSlice.GetNaturalHeight(effectiveScale);
			float offsetY = (barH - actualHeight) / 2f;
			
			var destRect = new Rectangle(barX, barY + offsetY, barW, actualHeight);
			
			float dpiRatio2 = Raylib.GetRenderWidth() / (float)Raylib.GetScreenWidth();
			float shaderBarX2 = barX * dpiRatio2;
			float shaderBarWidth2 = barW * dpiRatio2;
			
			Raylib.SetShaderValue(_hpBarShader, _locBarX, shaderBarX2, ShaderUniformDataType.Float);
			Raylib.SetShaderValue(_hpBarShader, _locBarWidth, shaderBarWidth2, ShaderUniformDataType.Float);
			Raylib.SetShaderValue(_hpBarShader, _locHealthPercent, staminaPercent, ShaderUniformDataType.Float);
			Raylib.SetShaderValue(_hpBarShader, _locPreviousPercent, staminaPercent, ShaderUniformDataType.Float);
			Color stColor = staminaLocked ? COLOR_STAMINA_FILL_LOCKED : COLOR_STAMINA_FILL;
			float[] stCol = { stColor.R / 255f, stColor.G / 255f, stColor.B / 255f };
			Raylib.SetShaderValue(_hpBarShader, _locFillColor, stCol, ShaderUniformDataType.Vec3);
			
			Raylib.BeginShaderMode(_hpBarShader);
			_hpBarNineSlice.Draw(destRect, Color.White);
			Raylib.EndShaderMode();
			
			// Remet l'échelle par défaut pour ne pas affecter le prochain appel
			// (barre de vie) sur la même frame ou la suivante.
			_hpBarNineSlice.Scale = HP_BAR_SCALE;
		}
        
        private static void DrawPanel(int x, int y, int w, int h)
        {
            // Fond principal
            Raylib.DrawRectangleRounded(new Rectangle(x, y, w, h), 0.15f, 8, COLOR_PANEL_BG);
            
            // Bordure dorée
            Raylib.DrawRectangleRoundedLines(new Rectangle(x, y, w, h), 0.15f, 8, 2, COLOR_TEXT_GOLD);
            
            // Décoration : petite ligne en haut
            Raylib.DrawRectangle(x + 15, y + 5, w - 30, 2, new Color(230, 200, 100, 100));
        }
        
        public static void OnDamageTaken()
        {
            _hpBarFlash = 0.5f;
        }
    }
}