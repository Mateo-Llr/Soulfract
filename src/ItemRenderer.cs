using Raylib_cs;
using System.Numerics;

namespace Soulfract
{
    public static class ItemRenderer
    {
        // Cache pour les textures chargées dynamiquement
        private static Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();
        private static Dictionary<string, (float Width, float Bottom)> _textureAlphaBoundsCache = new Dictionary<string, (float Width, float Bottom)>();
        private static Dictionary<string, (float Width, float Bottom)> _itemAlphaBoundsCache = new Dictionary<string, (float Width, float Bottom)>();

        /// <summary>
        /// Dessine un item dans un rectangle cible avec toutes ses couches de couleur.
        ///
        ///  CONTRAT IMPORTANT : cette fonction gère TOUT le repli en interne (couches
        /// teintées via TextureName/_1.png/_2.png/..., puis item.Icon, puis un rectangle
        /// de couleur uni). Le code appelant ne doit JAMAIS vérifier item.Icon.Id (ou tout
        /// autre pré-requis) avant d'appeler DrawItem/DrawItemCompact/DrawItemPadded pour
        /// décider s'il dessine "à la main" en repli - ça a justement causé un bug où des
        /// items sans texture de base (chapka, fedora...) s'affichaient en carré uni dans
        /// certains écrans (ItemMenuUI) alors qu'ils s'affichaient bien ailleurs. Toujours
        /// appeler cette fonction inconditionnellement et la laisser décider du rendu.
        /// </summary>
        /// <param name="item">L'item à dessiner (peut être null)</param>
        /// <param name="destRect">Rectangle de destination (position et taille)</param>
        /// <param name="tint">Teinte supplémentaire (optionnel, blanc par défaut)</param>
        /// <param name="forceSingleLayer">Limite le rendu aux couches structurelles définies par l'item (dyeLayers), sans les couches de teinte virtuelles ajoutées par une instance (pour tooltips/icônes compactes)</param>
        public static void DrawItem(Item? item, Rectangle destRect, Color? tint = null, bool forceSingleLayer = false)
        {
            if (item == null) return;

            DrawItemCore(item, destRect, tint, forceSingleLayer);

            //  Barre de contenu liquide (dessinée par-dessus l'icône, une fois le rendu
            // principal terminé, quel que soit le chemin de rendu emprunté ci-dessus : icône
            // par couches, icône unique de repli, etc.). Système unique et commun à la
            // gourde, l'arrosoir et le seau — tous des "contenants de liquide à charges"
            // au sens de Program.TryGetLiquidContainer.
            if (Program.TryGetLiquidContainer(item, out _, out _, out _))
            {
                DrawLiquidContainerOverlay(item, destRect, Vector2.Zero, 0f);
                DrawLiquidContainerBar(item, destRect);
            }
            // Barre générique pour métadonnées d'item (durabilité, pourrissement, etc.)
            DrawItemMetaBar(item, destRect);
        }

        private static void DrawItemCore(Item? item, Rectangle destRect, Color? tint, bool forceSingleLayer)
        {
            int itemId = Program.GetItemId(item.Name);
            bool hasPotionMetadata = IsPotionMetadata(item.Metadata);
            bool hasItemData = GameData.ItemDatabase.TryGetValue(itemId, out var itemData);
            if (hasPotionMetadata && !Program.IsLiquidContainer(item)
                && GameData.ItemDatabaseByKey.TryGetValue("glassbottle_small", out var bottleData))
            {
                itemData = bottleData;
                hasItemData = true;
            }

            if (!hasItemData || string.IsNullOrEmpty(itemData.TextureName))
            {
                // Pas de données connues, ou pas de texture par couches définie pour cet item :
                // seul recours possible, le rendu simple (icône unique / couleur de fallback).
                DrawSingleLayer(item, destRect, tint);
                return;
            }

            string textureBaseName = itemData.TextureName;

            if (string.Equals(textureBaseName, "coat", StringComparison.OrdinalIgnoreCase))
            {
                if (DrawCoatVariant(item, destRect, tint ?? Color.White))
                    return;
            }

            //  Les contenants de liquide à charges (gourde, arrosoir, seau) n'utilisent JAMAIS
            // le rendu "bouteille" (overlay de teinte plein cadre selon un contentType
            // heal/speed/etc.) même si leur texture ressemble à une bouteille (ex: la gourde
            // utilise "water_bottle") : ils suivent tous le même rendu simple + barre de
            // liquide, dessinée après coup par DrawItem via DrawLiquidContainerBar.
            bool isLiquidChargeContainer = Program.TryGetLiquidContainer(item, out _, out _, out _)
                && !IsBottleLikeTexture(textureBaseName);
            bool isBottleLike = !isLiquidChargeContainer && IsBottleLikeTexture(textureBaseName);
            if (isBottleLike)
            {
                if (DrawBottle(item, itemData, destRect, Vector2.Zero, 0f, tint ?? Color.White))
                    return;
            }

            bool isDyeable = itemData.IsDyeable;
            // Nombre de couches "structurelles" définies dans les données de l'item (dyeLayers).
            // Ce sont de véritables morceaux de sprite (ex: base + bord d'un chapeau), pas de
            // simples variantes de teinte : elles doivent toujours être dessinées ensemble pour
            // que l'item ait un rendu complet, y compris dans une icône compacte.
            int definedLayers = itemData.DyeLayers > 0 ? itemData.DyeLayers : 1;

            int layers;
            if (forceSingleLayer)
            {
                //  Mode compact (grille, tooltip) : on dessine toutes les couches structurelles
                // de l'item (definedLayers), mais on ignore les couches "virtuelles" qu'une
                // instance en jeu pourrait ajouter au-delà via des couleurs personnalisées
                // supplémentaires (CustomColors). Cela garantit un rendu complet et cohérent
                // même à partir d'un ItemData statique (sans instance Item réelle en jeu).
                layers = definedLayers;
            }
            else
            {
                // Nombre de couleurs personnalisées + 1 (pour la couche de base)
                int customColorLayers = itemData.IsDyeable ? Math.Max(1, itemData.DyeLayers) : 1;
                // Le nombre total de couches à dessiner est le maximum des deux (mais au moins 1)
                layers = Math.Max(definedLayers, customColorLayers);
            }

            //  COMPTER LE NOMBRE DE TEXTURES EFFECTIVEMENT CHARGÉES
            Texture2D baseTexture = LoadItemBaseTexture(textureBaseName);
            int loadedLayers = 0;
            if (baseTexture.Id != 0)
                loadedLayers++;
            for (int layer = 1; layer <= layers; layer++)
            {
                if (IsBucket(itemData))
                    break;
                Texture2D layerTex = LoadItemLayerTexture(textureBaseName, layer, isDyeable, definedLayers);
                if (layerTex.Id != 0)
                    loadedLayers++;
            }

            //  SI AUCUNE TEXTURE PAR COUCHE N'EXISTE (ni fichier de base, ni _1/_2/...),
            // utiliser le mode simple (icône unique / couleur) comme dernier recours.
            if (loadedLayers == 0)
            {
                DrawSingleLayer(item, destRect, tint);
                return;
            }

            if (baseTexture.Id != 0)
            {
                Raylib.DrawTexturePro(baseTexture,
                    new Rectangle(0, 0, baseTexture.Width, baseTexture.Height),
                    destRect, Vector2.Zero, 0, tint ?? Color.White);
            }

            //  DESSINER CHAQUE COUCHE
            for (int layer = 1; layer <= layers; layer++)
            {
                if (IsBucket(itemData))
                    break;
                Texture2D layerTex = LoadItemLayerTexture(textureBaseName, layer, isDyeable, definedLayers);

                // Si la texture de la couche n'existe pas, on ignore cette couche
                if (layerTex.Id == 0)
                    continue;

                // Déterminer la couleur à appliquer à cette couche
                Color layerColor = Color.White;

                int colorIndex = isDyeable ? layer - 1 : 0;
                layerColor = item.GetLayerColor(colorIndex);

                // Appliquer le tint externe s'il est fourni
                Color finalTint = tint ?? Color.White;
                if (tint.HasValue)
                {
                    // Mélanger le tint avec la couleur de la couche
                    finalTint = new Color(
                        (byte)(finalTint.R * layerColor.R / 255),
                        (byte)(finalTint.G * layerColor.G / 255),
                        (byte)(finalTint.B * layerColor.B / 255),
                        (byte)(finalTint.A * layerColor.A / 255)
                    );
                }
                else
                {
                    finalTint = layerColor;
                }

                // Dessiner la couche
                Raylib.DrawTexturePro(layerTex,
                    new Rectangle(0, 0, layerTex.Width, layerTex.Height),
                    destRect, Vector2.Zero, 0, finalTint);
            }
        }

        /// <summary>
        /// Dessine un item en une seule couche (cas standard, fallback, ou mode compact)
        /// </summary>
        private static void DrawSingleLayer(Item? item, Rectangle destRect, Color? tint = null)
        {
            if (item == null) return;

            Color displayColor;
            bool hasCustomColor = false;

            // Déterminer la couleur à afficher
            displayColor = item.GetLayerColor(0);
            hasCustomColor = item.HasMeta("color");

            Color finalTint = tint ?? Color.White;

            // Mélanger le tint avec la couleur de l'item si nécessaire
            if (hasCustomColor && tint.HasValue)
            {
                finalTint = new Color(
                    (byte)(finalTint.R * displayColor.R / 255),
                    (byte)(finalTint.G * displayColor.G / 255),
                    (byte)(finalTint.B * displayColor.B / 255),
                    (byte)(finalTint.A * displayColor.A / 255)
                );
            }
            else if (hasCustomColor)
            {
                finalTint = displayColor;
            }

            // Dessiner l'icône ou un rectangle de fallback
            if (item.Icon.Id != 0)
            {
                Raylib.DrawTexturePro(item.Icon,
                    new Rectangle(0, 0, item.Icon.Width, item.Icon.Height),
                    destRect, Vector2.Zero, 0, finalTint);
            }
            else
            {
                Raylib.DrawRectangleRounded(destRect, 0.1f, 6, displayColor);
            }
        }

        private static bool DrawCoatVariant(Item item, Rectangle destRect, Color tint)
        {
            string bodyStyle = item.GetMetadataValue("opening", item.GetMeta("opening", "closed")).ToLowerInvariant();
            if (bodyStyle != "open" && bodyStyle != "lace_up" && bodyStyle != "closed")
                bodyStyle = "closed";

            string sleeveStyle = item.GetMetadataValue("sleeves", item.GetMeta("sleeves", "short")).ToLowerInvariant();
            if (sleeveStyle != "short" && sleeveStyle != "long" && sleeveStyle != "none")
                sleeveStyle = "short";

            Texture2D body = LoadTexture($"assets/items/coat_1_body_{bodyStyle}.png");
            Texture2D sleeves = sleeveStyle == "none"
                ? new Texture2D()
                : LoadTexture($"assets/items/coat_1_sleeves_{sleeveStyle}.png");
            if (body.Id == 0 && sleeves.Id == 0)
                return false;

            Color coatColor = item.GetLayerColor(0);

            Color finalTint = new Color(
                (byte)(tint.R * coatColor.R / 255),
                (byte)(tint.G * coatColor.G / 255),
                (byte)(tint.B * coatColor.B / 255),
                (byte)(tint.A * coatColor.A / 255));

            if (body.Id != 0)
                DrawTextureLayer(body, destRect, finalTint);
            if (sleeves.Id != 0)
                DrawTextureLayer(sleeves, destRect, finalTint);

            return true;
        }

        private static void DrawTextureLayer(Texture2D texture, Rectangle destRect, Color tint)
        {
            Raylib.DrawTexturePro(texture,
                new Rectangle(0, 0, texture.Width, texture.Height),
                destRect, Vector2.Zero, 0, tint);
        }

        private static bool IsBottleLikeTexture(string? textureBaseName)
        {
            if (string.IsNullOrWhiteSpace(textureBaseName))
                return false;

            string normalized = textureBaseName.ToLowerInvariant();
            return normalized.Contains("glassbottle") || normalized.Contains("glass_bottle") || normalized.Contains("water_bottle");
        }

        private static bool IsBucket(ItemData itemData)
        {
            return string.Equals(itemData.Key, "bucket", StringComparison.OrdinalIgnoreCase);
        }

        public static void DrawLiquidContainerOverlay(Item item, Rectangle dest, Vector2 origin, float rotation, float facing = 1f, bool held = false)
        {
            if (!IsBucketItem(item))
                return;

            string liquidType = string.Empty;
            bool hasContent = Program.TryGetLiquidContainer(item, out liquidType, out int charges, out _)
                && charges > 0;
            if (!hasContent && IsPotionMetadata(item.Metadata))
            {
                var bucketData = new ItemData { TextureName = "bucket" };
                hasContent = TryGetBottleContentInfo(item, bucketData, out liquidType, out _)
                    && !liquidType.Equals("empty", StringComparison.OrdinalIgnoreCase)
                    && !liquidType.Equals("none", StringComparison.OrdinalIgnoreCase);
            }
            if (!hasContent)
                return;

            string textureName = "bucket";
            string path = held
                ? $"assets/equipements/{textureName}_rarmbottom_1.png"
                : $"assets/items/{textureName}_1.png";
            Texture2D overlay = LoadTexture(path);
            if (overlay.Id == 0)
                return;

            Color liquidColor = GetLiquidColor(liquidType);
            float sourceWidth = overlay.Width * MathF.Sign(facing == 0f ? 1f : facing);
            Rectangle source = new(facing < 0f ? overlay.Width : 0f, 0f, sourceWidth, overlay.Height);
            Raylib.DrawTexturePro(overlay, source, dest, origin, rotation, liquidColor);
        }

        private static bool IsBucketItem(Item item)
        {
            return GameData.TryGetItemByName(item.Name, out var itemData) && IsBucket(itemData);
        }

        private static Color GetLiquidColor(string liquidType)
        {
            return liquidType.Equals("milk", StringComparison.OrdinalIgnoreCase)
                ? new Color(240, 240, 230, 255)
                : liquidType.Equals("water", StringComparison.OrdinalIgnoreCase)
                    ? new Color(60, 140, 230, 255)
                    : GetBottleContentColor(liquidType, Color.White);
        }

        //  Les contenants de liquide à charges (gourde, arrosoir, seau — cf.
        // Program.TryGetLiquidContainer) ne sont PAS traités comme des bouteilles : pas
        // d'overlay de teinte ("_1.png"), mais une barre de liquide dessinée par DrawItem
        // (cf. DrawLiquidContainerBar) proportionnelle à leurs charges, et un nom affiché
        // unifié (cf. GetDisplayName, ex. "Gourde [4/6]", "Seau d'eau [7/10]"), indépendamment
        // de IsBottleLikeTexture/GetBottleContentColor qui ne concernent que les vraies
        // bouteilles/potions.

        /// <summary>
        /// Dessine une petite barre de contenu liquide en bas de l'icône, pour visualiser
        /// rapidement le niveau de liquide d'un contenant à charges (gourde, arrosoir, seau)
        /// sans avoir besoin de survoler l'item pour lire son nom.
        /// </summary>
        private static void DrawLiquidContainerBar(Item item, Rectangle destRect)
        {
            if (!Program.TryGetLiquidContainer(item, out string liquidType, out int charges, out int maxCharges))
                return;
            if (liquidType == "empty" || charges <= 0 || maxCharges <= 0)
                return; // Contenant vide : pas de barre à afficher

            Color liquidColor = GetLiquidColor(liquidType);

            // Barre fine ancrée en bas de l'icône, sur toute sa largeur.
            float barHeight = MathF.Max(2f, destRect.Height * 0.12f);
            float barMargin = MathF.Max(1f, destRect.Width * 0.08f);
            Rectangle barBackground = new Rectangle(
                destRect.X + barMargin,
                destRect.Y + destRect.Height - barHeight - barMargin * 0.5f,
                destRect.Width - barMargin * 2f,
                barHeight);

            // Fond semi-transparent pour que la barre reste lisible sur toutes les textures.
            Raylib.DrawRectangleRec(barBackground, new Color(20, 20, 20, 140));

            // Remplissage proportionnel au nombre de charges.
            float fillRatio = Math.Clamp((float)charges / maxCharges, 0f, 1f);
            Rectangle barFill = new Rectangle(
                barBackground.X + 1,
                barBackground.Y + 1,
                MathF.Max(0f, (barBackground.Width - 2) * fillRatio),
                MathF.Max(0f, barBackground.Height - 2));
            Raylib.DrawRectangleRec(barFill, liquidColor);
        }

        // Dessine une petite barre en bas de l'icône pour indiquer durabilité / pourrissement
        private static void DrawItemMetaBar(Item item, Rectangle destRect)
        {
            if (item == null) return;

            // Priorité aux clés connues : "durability" (armes/outils), "spoil" (nourriture)
            string durMeta = item.GetMeta("durability", "");
            string spoilMeta = item.GetMeta("spoil", "");

            float current = -1f, max = -1f;
            Color fillColor = Color.White;

            if (!string.IsNullOrWhiteSpace(durMeta))
            {
                if (!TryParseMetaPair(durMeta, out current, out max)) return;
                fillColor = new Color(200, 200, 200, 255);
            }
            else if (!string.IsNullOrWhiteSpace(spoilMeta))
            {
                if (!TryParseMetaPair(spoilMeta, out current, out max)) return;
                // spoil = remaining / total -> green -> red as it approaches 0
                fillColor = new Color(120, 200, 80, 255);
            }
            else
            {
                // Non : tenter d'afficher durabilité implicite dans Metadata (par ex. "42/100")
                string meta = item.Metadata ?? "";
                if (!string.IsNullOrWhiteSpace(meta) && meta.Contains("/"))
                {
                    if (!TryParseMetaPair(meta, out current, out max)) return;
                    fillColor = new Color(180, 160, 200, 255);
                }
                else
                {
                    return; // rien à afficher
                }
            }

            if (max <= 0f) return;
            float ratio = Math.Clamp(current / max, 0f, 1f);

            // Choisir une teinte selon le ratio (vert->jaune->rouge)
            if (ratio > 0.66f) fillColor = new Color(100, 200, 100, 255);
            else if (ratio > 0.33f) fillColor = new Color(240, 200, 80, 255);
            else fillColor = new Color(220, 90, 70, 255);

            float barHeight = MathF.Max(2f, destRect.Height * 0.12f);
            float barMargin = MathF.Max(1f, destRect.Width * 0.08f);
            Rectangle barBackground = new Rectangle(
                destRect.X + barMargin,
                destRect.Y + destRect.Height - barHeight - barMargin * 0.5f,
                destRect.Width - barMargin * 2f,
                barHeight);

            Raylib.DrawRectangleRec(barBackground, new Color(20, 20, 20, 140));

            Rectangle barFill = new Rectangle(
                barBackground.X + 1,
                barBackground.Y + 1,
                MathF.Max(0f, (barBackground.Width - 2) * ratio),
                MathF.Max(0f, barBackground.Height - 2));
            Raylib.DrawRectangleRec(barFill, fillColor);
        }

        // Parse une paire de type "cur/max" ou "cur|max" ou "cur:max" ou une valeur unique (0-100)
        private static bool TryParseMetaPair(string meta, out float cur, out float max)
        {
            cur = 0f; max = 0f;
            if (string.IsNullOrWhiteSpace(meta)) return false;
            char[] seps = new char[] { '/', '|', ':', '=' };
            var parts = meta.Split(seps, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                {
                    cur = v; max = 100f; return true;
                }
                return false;
            }
            if (parts.Length >= 2)
            {
                bool ok1 = float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v1);
                bool ok2 = float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v2);
                if (ok1 && ok2)
                {
                    cur = v1; max = v2; return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Une couche de couleur doit être dessinée dès qu'on a un contenu identifiable et
        /// non vide : soit un type de potion connu (table PotionContentInfo), soit "custom"
        /// (couleur personnalisée fournie par ailleurs). Seul "empty" n'a rien à dessiner
        /// (bouteille vide = juste la texture de base).
        ///  Ne PAS whitelister les types un par un ici : toute potion ajoutée à
        /// PotionContentInfo doit se dessiner automatiquement, sans y toucher.
        /// </summary>
        private static bool ShouldRenderBottleContent(Item? item, ItemData itemData)
        {
            if (!TryGetBottleContentInfo(item, itemData, out string contentType, out _))
                return false;

            if (contentType.Equals("empty", StringComparison.OrdinalIgnoreCase))
                return false;

            return contentType.Equals("custom", StringComparison.OrdinalIgnoreCase)
                || PotionContentInfo.ContainsKey(contentType);
        }

        /// <summary>
        /// Table UNIQUE de vérité pour tout ce qui dépend du type de contenu d'une bouteille
        /// (effet de potion, soin, raté, etc.) : nom affiché ET couleur de rendu.
        ///
        ///  Toute nouvelle potion/effet ne doit être ajoutée QU'ICI. Avant ce refactor,
        /// le nom (GetDisplayName), la couleur (GetBottleContentColor) et l'autorisation même
        /// de dessiner une couche de couleur (ShouldRenderBottleContent) étaient 3 listes
        /// tenues à la main séparément, qui avaient fini par diverger : plusieurs potions
        /// (force, résistance, lumière, résist. chaleur/froid, invisibilité) avaient bien un
        /// nom et une info de contenu, mais n'étaient dans AUCUNE des listes de couleur/overlay,
        /// donc s'affichaient comme des bouteilles neutres sans teinte, partout où l'item est
        /// dessiné (hotbar, tooltip, inventaire, sol...) puisque tous ces rendus passent déjà
        /// par le même point d'entrée commun (DrawItem/DrawItemCompact/DrawItemPadded).
        /// </summary>
        private static readonly Dictionary<string, (string DisplayName, Color Color)> PotionContentInfo = new(StringComparer.OrdinalIgnoreCase)
        {
            ["heal"]         = ("Petite bouteille de soin",                 new Color(235,  20,  20, 255)),
            ["speed"]        = ("Petite potion de vitesse",                 new Color( 21,  62, 217, 255)),
            ["strength"]     = ("Petite potion de force",                   new Color(200,  90,  20, 255)),
            ["power"]        = ("Petite bouteille de pouvoir",              new Color(200,  90,  20, 255)),
            ["pouvoir"]      = ("Petite bouteille de pouvoir",              new Color(200,  90,  20, 255)),
            ["resistance"]   = ("Petite potion de résistance",              new Color(120, 120, 140, 255)),
            ["light"]        = ("Petite potion de lumière",                 new Color(250, 220,  80, 255)),
            ["heatresist"]   = ("Petite potion de résistance à la chaleur", new Color(230, 100,  30, 255)),
            ["coldresist"]   = ("Petite potion de résistance au froid",     new Color(120, 200, 255, 255)),
            ["invisibility"] = ("Petite potion d'invisibilité",             new Color(180, 180, 220, 160)),
            ["empty"]        = ("Petite bouteille vide",                   new Color(170, 170, 170, 255)),
            ["none"]         = ("Petite bouteille de breuvage raté",       new Color(200, 215, 140, 255)),
            ["failed"]       = ("Petite bouteille de breuvage raté",       new Color(200, 215, 140, 255)),
        };

        public static string GetDisplayName(Item? item, int itemId, string fallbackName = "")
        {
            string baseName = string.IsNullOrWhiteSpace(fallbackName) ? (item?.Name ?? string.Empty) : fallbackName;
            if (item == null)
                return baseName;

            string rawMetadata = NormalizePotionMetadata(item.Metadata);

            if (itemId <= 0)
                itemId = Program.GetItemId(item.Name);

            if (!GameData.ItemDatabase.TryGetValue(itemId, out var itemData))
                return baseName;

            if (TryGetBottleContentInfo(item, itemData, out string potionType, out _)
                && PotionContentInfo.TryGetValue(potionType, out var potionInfo))
            {
                string potionDisplayName = potionInfo.DisplayName;
                if (Program.IsLiquidContainer(item))
                {
                    string containerName = string.IsNullOrWhiteSpace(itemData.Name) ? baseName : itemData.Name;
                    potionDisplayName = potionInfo.DisplayName
                        .Replace("Petite bouteille", containerName, StringComparison.OrdinalIgnoreCase)
                        .Replace("Petite potion", containerName, StringComparison.OrdinalIgnoreCase);
                }

                if (Program.TryGetLiquidContainer(item, out _, out int potionCharges, out int potionMaxCharges)
                    && potionCharges > 0 && potionMaxCharges > 0)
                    return $"{potionDisplayName} [{potionCharges}/{potionMaxCharges}]";

                return potionDisplayName;
            }

            //  Contenants de liquide à charges (gourde, arrosoir, seau) : nom unifié avec
            // l'indication de charges entre crochets, ex. "Gourde [4/6]", "Seau d'eau [7/10]".
            // Le seau, seul contenant pouvant recevoir eau OU lait, précise en plus le type.
            if (!IsPotionMetadata(rawMetadata)
                && Program.TryGetLiquidContainer(item, out string liquidType, out int liquidCharges, out int liquidMaxCharges))
            {
                string containerBaseName = string.IsNullOrWhiteSpace(itemData.Name) ? baseName : itemData.Name;

                if (liquidType == "empty")
                    return containerBaseName;

                string label = liquidType switch
                {
                    "water" => $"{containerBaseName} d'eau",
                    "milk" => $"{containerBaseName} de lait",
                    _ => containerBaseName
                };

                return $"{label} [{liquidCharges}/{liquidMaxCharges}]";
            }

            if (!TryGetBottleContentInfo(item, itemData, out string contentType, out _))
                return baseName;

            // "custom" n'a pas de nom générique : on garde le nom de base de l'item.
            return PotionContentInfo.TryGetValue(contentType, out var contentInfo)
                ? contentInfo.DisplayName
                : baseName;
        }


        public static bool TryGetBottleContentInfo(Item? item, ItemData itemData, out string contentType, out int amount)
        {
            contentType = string.Empty;
            amount = 0;

            string metadata = item?.Metadata ?? string.Empty;
            if (string.IsNullOrWhiteSpace(metadata))
                return false;

            string normalized = NormalizePotionMetadata(metadata);
            string potionMetadata = normalized.Split(';', 2)[0].Trim();
            if (potionMetadata.Equals("empty", StringComparison.OrdinalIgnoreCase))
            {
                contentType = "empty";
                return true;
            }

            if (potionMetadata.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                potionMetadata.Equals("effect:none", StringComparison.OrdinalIgnoreCase) ||
                potionMetadata.Equals("failed", StringComparison.OrdinalIgnoreCase))
            {
                contentType = "none";
                return true;
            }

            if (potionMetadata.StartsWith("heal", StringComparison.OrdinalIgnoreCase))
            {
                contentType = "heal";
                amount = ExtractNumericValueFromMetadata(potionMetadata);
                if (amount <= 0) amount = 10;
                return true;
            }

            if (potionMetadata.StartsWith("effect", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = potionMetadata.Split(':', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string effectType = parts[1].Trim().ToLowerInvariant();
                    if (effectType == "none")
                    {
                        contentType = "none";
                        return true;
                    }

                    if (effectType == "empty")
                    {
                        contentType = "empty";
                        return true;
                    }

                    contentType = effectType;
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int parsedAmount))
                    {
                        amount = parsedAmount;
                    }
                    return true;
                }

                contentType = "none";
                return true;
            }

            contentType = "custom";
            return true;
        }

        public static string NormalizePotionMetadata(string? metadata)
        {
            string normalized = metadata?.Trim() ?? string.Empty;
            if (normalized.StartsWith("raw=", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[4..].Trim();
            return normalized;
        }

        public static bool IsPotionMetadata(string? metadata)
        {
            string normalized = NormalizePotionMetadata(metadata);
            return normalized.StartsWith("effect:", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("heal:", StringComparison.OrdinalIgnoreCase);
        }

        public static int GetPotionDoses(Item item)
        {
            if (!IsPotionMetadata(item.Metadata)) return 0;
            string doses = item.GetMeta("doses", "");
            return int.TryParse(doses, out int parsed) ? Math.Max(0, parsed) : 1;
        }

        public static void SetPotionDoses(Item item, int doses)
        {
            string normalized = NormalizePotionMetadata(item.Metadata);
            var parts = normalized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => !part.StartsWith("doses=", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (doses > 0)
                parts.Add($"doses={doses}");
            item.Metadata = string.Join(";", parts);
        }

        private static int ExtractNumericValueFromMetadata(string metadata)
        {
            char[] separators = [':', '=', '|', ' '];
            string[] parts = metadata.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (int.TryParse(part, out int value))
                    return value;
            }

            return 0;
        }

        /// <summary>
        /// Couleur de l'overlay de contenu d'une bouteille. Lit PotionContentInfo, la même
        /// table utilisée par GetDisplayName : impossible qu'un type de potion ait un nom
        /// mais pas de couleur (c'est exactement ce qui causait le bug des potions "invisibles"
        /// en force/résistance/lumière/résist. chaleur-froid/invisibilité). Pour "custom", pas
        /// de couleur prédéfinie : on retombe sur la teinte de base fournie par l'appelant.
        /// </summary>
        private static Color GetBottleContentColor(string contentType, Color fallbackTint)
        {
            return PotionContentInfo.TryGetValue(contentType, out var info) ? info.Color : fallbackTint;
        }

        private static Color ResolveDisplayTint(Item? item, Color? tint)
        {
            Color finalTint = tint ?? Color.White;
            if (item == null) return finalTint;

            Color displayColor;
            bool hasCustomColor = false;

            if (item.CustomColors != null && item.CustomColors.Count > 0 && item.CustomColors[0].HasValue)
            {
                displayColor = item.CustomColors[0].Value;
                hasCustomColor = true;
            }
            else if (item.CustomColor.HasValue)
            {
                displayColor = item.CustomColor.Value;
                hasCustomColor = true;
            }
            else
            {
                displayColor = item.BaseColor;
                hasCustomColor = false;
            }

            if (hasCustomColor && tint.HasValue)
            {
                return new Color(
                    (byte)(finalTint.R * displayColor.R / 255),
                    (byte)(finalTint.G * displayColor.G / 255),
                    (byte)(finalTint.B * displayColor.B / 255),
                    (byte)(finalTint.A * displayColor.A / 255)
                );
            }

            return hasCustomColor ? displayColor : finalTint;
        }

        /// <summary>
        /// Résout la texture à utiliser pour représenter un item de façon simple (couche 1
        /// en priorité, repli sur ItemData.Icon si aucune texture par couche n'existe).
        ///
        /// À utiliser uniquement pour les rendus personnalisés qui ne peuvent pas passer par
        /// DrawItem/DrawItemCompact/DrawItemPadded (ex: items au sol avec rotation/origine
        /// personnalisées). Partout ailleurs, préférer directement DrawItem*.
        /// </summary>
        public static Texture2D ResolveDisplayTexture(ItemData itemData)
        {
            if (!string.IsNullOrEmpty(itemData.TextureName))
            {
                int definedLayers = itemData.DyeLayers > 0 ? itemData.DyeLayers : 1;
                Texture2D baseTexture = LoadItemBaseTexture(itemData.TextureName);
                if (baseTexture.Id != 0)
                    return baseTexture;

                Texture2D tex = LoadItemLayerTexture(itemData.TextureName, 1, itemData.IsDyeable, definedLayers);
                if (tex.Id != 0)
                    return tex;
            }

            return itemData.Icon;
        }

        public static float GetItemAlphaWidth(ItemData itemData)
        {
            return GetItemAlphaBounds(itemData).Width;
        }

        public static float GetItemAlphaBottom(ItemData itemData)
        {
            return GetItemAlphaBounds(itemData).Bottom;
        }

        private static (float Width, float Bottom) GetItemAlphaBounds(ItemData itemData)
        {
            string cacheKey = $"{itemData.TextureName}|{itemData.DyeLayers}|{itemData.IsDyeable}";
            if (_itemAlphaBoundsCache.TryGetValue(cacheKey, out var cachedBounds))
                return cachedBounds;

            float width = 0f;
            float bottom = 0f;
            if (!string.IsNullOrEmpty(itemData.TextureName))
            {
                int definedLayers = itemData.DyeLayers > 0 ? itemData.DyeLayers : 1;
                Texture2D baseTexture = LoadItemBaseTexture(itemData.TextureName);
                if (baseTexture.Id != 0)
                {
                    var baseBounds = GetTextureAlphaBounds(baseTexture, $"item:{itemData.TextureName}:base");
                    width = MathF.Max(width, baseBounds.Width);
                    bottom = MathF.Max(bottom, baseBounds.Bottom);
                }

                for (int layer = 1; layer <= definedLayers; layer++)
                {
                    Texture2D layerTexture = LoadItemLayerTexture(itemData.TextureName, layer, itemData.IsDyeable, definedLayers);
                    if (layerTexture.Id == 0) continue;

                    var layerBounds = GetTextureAlphaBounds(layerTexture, $"item:{itemData.TextureName}:{layer}");
                    width = MathF.Max(width, layerBounds.Width);
                    bottom = MathF.Max(bottom, layerBounds.Bottom);
                }
            }

            if (width <= 0f && itemData.Icon.Id != 0)
            {
                var iconBounds = GetTextureAlphaBounds(itemData.Icon, $"icon:{itemData.Name}");
                width = iconBounds.Width;
                bottom = iconBounds.Bottom;
            }

            cachedBounds = (width > 0f ? width : 1f, bottom > 0f ? bottom : 1f);
            _itemAlphaBoundsCache[cacheKey] = cachedBounds;
            return cachedBounds;
        }

        public static float GetTextureAlphaWidth(Texture2D texture, string cacheKey)
        {
            return GetTextureAlphaBounds(texture, cacheKey).Width;
        }

        public static float GetTextureAlphaBottom(Texture2D texture, string cacheKey)
        {
            return GetTextureAlphaBounds(texture, cacheKey).Bottom;
        }

        private static (float Width, float Bottom) GetTextureAlphaBounds(Texture2D texture, string cacheKey)
        {
            if (_textureAlphaBoundsCache.TryGetValue(cacheKey, out var cachedBounds))
                return cachedBounds;

            if (texture.Id == 0 || texture.Width <= 0 || texture.Height <= 0)
                return (1f, 1f);

            Image image = Raylib.LoadImageFromTexture(texture);
            int minX = image.Width;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (Raylib.GetImageColor(image, x, y).A == 0) continue;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            Raylib.UnloadImage(image);
            cachedBounds = maxX >= minX
                ? ((maxX - minX + 1f) / texture.Width, (maxY + 1f) / texture.Height)
                : (1f, 1f);
            _textureAlphaBoundsCache[cacheKey] = cachedBounds;
            return cachedBounds;
        }

        private static bool DrawBottle(Item item, ItemData itemData, Rectangle dest, Vector2 origin, float rotation, Color baseTint)
        {
            string textureBaseName = itemData.TextureName;
            Texture2D bottleBaseTex = LoadTexture($"assets/items/{textureBaseName}.png");
            if (bottleBaseTex.Id == 0)
                return false;

            Raylib.DrawTexturePro(bottleBaseTex,
                new Rectangle(0, 0, bottleBaseTex.Width, bottleBaseTex.Height),
                dest, origin, rotation, baseTint);

            Texture2D overlayTex = LoadTexture($"assets/items/{textureBaseName}_1.png");
            if (overlayTex.Id != 0)
            {
                bool hasContent = Program.TryGetLiquidContainer(item, out string contentType, out int charges, out _)
                    && charges > 0
                    && !contentType.Equals("empty", StringComparison.OrdinalIgnoreCase);
                if (hasContent)
                {
                    Color overlayTint = GetLiquidColor(contentType);
                    Raylib.DrawTexturePro(overlayTex,
                        new Rectangle(0, 0, overlayTex.Width, overlayTex.Height),
                        dest, origin, rotation, overlayTint);
                }
            }

            return true;
        }

        /// <summary>
        /// Dessine un item avec rotation/origine personnalisées, en gardant TOUTES les couches
        /// structurelles de l'item (dyeLayers), pas seulement la première. À utiliser pour les
        /// rendus qui ne peuvent pas passer par DrawItem (ex: items au sol qui tournent) mais
        /// qui doivent quand même afficher un item multi-couches complet (ex: fedora_1 + fedora_2).
        /// </summary>
        /// <param name="itemData">Données de l'item</param>
        /// <param name="center">Position (centre) du rendu dans le monde/à l'écran</param>
        /// <param name="size">Taille du carré de destination</param>
        /// <param name="rotation">Rotation en degrés, autour de "center"</param>
        /// <param name="customTint">
        /// Vraie couleur de teinture appliquée à la couche 1 (ex: item réellement teint par le
        /// joueur). Laisser à null si l'item n'a pas été teint : la texture s'affiche alors avec
        /// ses couleurs d'origine (blanc), au lieu d'être recolorée par itemData.Color (qui n'est
        /// qu'une couleur de repli pour le cas où aucune texture n'existe du tout).
        /// </param>
        public static void DrawItemRotated(ItemData itemData, Vector2 center, float size, float rotation, Color? customTint = null, string? metadata = null)
        {
            Rectangle dest = new Rectangle(center.X, center.Y, size, size);
            Vector2 origin = new Vector2(size / 2f, size / 2f);
            // Couleur de dernier recours (rectangle de repli si aucune texture n'existe) :
            // la vraie teinte de l'item si elle existe, sinon la couleur générique de l'item.
            Color fallbackColor = customTint ?? itemData.Color;

            if (string.IsNullOrEmpty(itemData.TextureName))
            {
                DrawSingleTextureRotated(itemData.Icon, dest, origin, rotation, fallbackColor);
                return;
            }

            string textureBaseName = itemData.TextureName;
            bool isBottleLike = IsBottleLikeTexture(textureBaseName);
            if (isBottleLike)
            {
                var groundItem = new Item(itemData.Name, 1, itemData.Color, itemData.Icon, customTint);
                groundItem.Metadata = metadata ?? "";
                if (DrawBottle(groundItem, itemData, dest, origin, rotation, customTint ?? Color.White))
                {
                    if (rotation == 0f)
                        DrawLiquidContainerBar(groundItem, new Rectangle(
                            dest.X - origin.X, dest.Y - origin.Y, dest.Width, dest.Height));
                    return;
                }
            }

            bool isBucket = IsBucket(itemData);

            int definedLayers = itemData.DyeLayers > 0 ? itemData.DyeLayers : 1;
            bool isDyeable = itemData.IsDyeable;

            int loadedLayers = 0;
            if (LoadItemBaseTexture(itemData.TextureName).Id != 0)
                loadedLayers++;
            for (int layer = 1; layer <= definedLayers; layer++)
            {
                if (LoadItemLayerTexture(itemData.TextureName, layer, isDyeable, definedLayers).Id != 0)
                    loadedLayers++;
            }

            if (loadedLayers == 0)
            {
                DrawSingleTextureRotated(itemData.Icon, dest, origin, rotation, fallbackColor);
                return;
            }

            Texture2D baseTexture = LoadItemBaseTexture(textureBaseName);
            if (baseTexture.Id != 0)
            {
                Raylib.DrawTexturePro(baseTexture,
                    new Rectangle(0, 0, baseTexture.Width, baseTexture.Height),
                    dest, origin, rotation, Color.White);
            }

            for (int layer = 1; layer <= definedLayers; layer++)
            {
                if (isBucket)
                    break;
                Texture2D layerTex = LoadItemLayerTexture(itemData.TextureName, layer, isDyeable, definedLayers);
                if (layerTex.Id == 0) continue;

                Color layerColor;
                if (isDyeable)
                {
                    var metadataItem = new Item(itemData.Name, 1, Color.White, itemData.Icon);
                    metadataItem.Metadata = metadata ?? "";
                    layerColor = metadataItem.GetLayerColor(layer - 1);
                }
                else
                    layerColor = layer == 1 && customTint.HasValue ? customTint.Value : Color.White;

                Raylib.DrawTexturePro(layerTex,
                    new Rectangle(0, 0, layerTex.Width, layerTex.Height),
                    dest, origin, rotation, layerColor);
            }

            if (isBucket)
            {
                var groundItem = new Item(itemData.Name, 1, itemData.Color, itemData.Icon, customTint);
                groundItem.Metadata = metadata ?? "";
                DrawLiquidContainerOverlay(groundItem, dest, origin, rotation);
            }
        }

        private static void DrawSingleTextureRotated(Texture2D tex, Rectangle dest, Vector2 origin, float rotation, Color tint)
        {
            if (tex.Id != 0)
            {
                Raylib.DrawTexturePro(tex,
                    new Rectangle(0, 0, tex.Width, tex.Height),
                    dest, origin, rotation, tint);
            }
            else
            {
                Raylib.DrawRectangleRounded(dest, 0.1f, 6, tint);
            }
        }

        /// <summary>
        /// Charge une texture depuis le cache ou depuis le disque
        /// </summary>
        private static Texture2D LoadTexture(string path)
        {
            if (_textureCache.TryGetValue(path, out var cached) && cached.Id != 0)
                return cached;

            if (File.Exists(path))
            {
                var tex = Raylib.LoadTexture(path);
                if (tex.Id != 0)
                {
                    _textureCache[path] = tex;
                    return tex;
                }
            }

            _textureCache[path] = new Texture2D();
            return new Texture2D();
        }

        private static Texture2D LoadItemLayerTexture(string baseName, int layer, bool isDyeable, int definedLayers = 1)
        {
            string path;
            if (layer == 1)
            {
                // La texture sans suffixe est une couche structurelle indépendante.
                path = $"assets/items/{baseName}_1.png";
            }
            else if (layer <= definedLayers)
            {
                //  Couche déclarée dans les données de l'item (dyeLayers) : fichier dédié
                // "_2.png" pour la couche 2, "_3.png" pour la couche 3, etc.
                path = $"assets/items/{baseName}_{layer}.png";
            }
            else
            {
                // Couche "de teinte" au-delà des couches déclarées (cas des items à teinte
                // simple : DyeLayers=1 mais un CustomColor ajoute une couche 2 virtuelle) :
                // on réutilise le fichier recolorable de la couche 1, simplement teinté.
                path = $"assets/items/{baseName}_1.png";
            }

            var tex = LoadTexture(path);
            if (tex.Id != 0)
                return tex;

            return new Texture2D();
        }

        private static Texture2D LoadItemBaseTexture(string baseName)
        {
            return LoadTexture($"assets/items/{baseName}.png");
        }

        /// <summary>
        /// Dessine un item avec des coordonnées et une taille simple
        /// </summary>
        public static void DrawItem(Item? item, int x, int y, int size, Color? tint = null, bool forceSingleLayer = false)
        {
            DrawItem(item, new Rectangle(x, y, size, size), tint, forceSingleLayer);
        }

        /// <summary>
        /// Dessine un item avec padding automatique (pour les slots avec bordure)
        /// </summary>
        public static void DrawItemPadded(Item? item, int x, int y, int slotSize, int padding = 6, Color? tint = null, bool forceSingleLayer = false)
        {
            DrawItem(item, new Rectangle(x + padding, y + padding, slotSize - padding * 2, slotSize - padding * 2), tint, forceSingleLayer);
        }

        /// <summary>
        /// Dessine un item en version compacte (pour les tooltips ou les petites icônes).
        /// Dessine uniquement les couches structurelles définies par l'item (dyeLayers),
        /// sans les couches de teinte "virtuelles" supplémentaires qu'une instance en jeu
        /// pourrait avoir via CustomColors — utile pour un rendu à partir d'un ItemData
        /// statique (ex: grille du menu d'items) qui n'a pas d'instance Item réelle.
        /// </summary>
        public static void DrawItemCompact(Item? item, int x, int y, int size, Color? tint = null)
        {
            DrawItem(item, new Rectangle(x, y, size, size), tint, forceSingleLayer: true);
        }

        /// <summary>
        /// Vide le cache des textures (à appeler lors du rechargement des assets)
        /// </summary>
        public static void ClearTextureCache()
        {
            foreach (var tex in _textureCache.Values)
            {
                if (tex.Id != 0)
                    Raylib.UnloadTexture(tex);
            }
            _textureCache.Clear();
        }
    }
}