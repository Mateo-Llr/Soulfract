// RadialMenu.cs
using Raylib_cs;
using System.Numerics;
using System.Linq;

namespace Soulfract
{
    public class RadialMenu
    {
        private bool _isOpen = false;
        private int _hoverSlot = -1;
        private List<InventorySlot> _slots = new List<InventorySlot>();
        private int _currentSlot = -1;
        private float _radius = 225f;
        private const int SLOT_COUNT = 10;
        private const float SECTOR_ANGLE = 360f / SLOT_COUNT;

        public bool IsOpen => _isOpen;
        public int SelectedSlot => _hoverSlot;

        public void Open(List<InventorySlot> slots, int currentSlot)
        {
            if (slots.Count < SLOT_COUNT) return;
            _slots = slots.Take(SLOT_COUNT).ToList();
            _currentSlot = currentSlot;
            _hoverSlot = -1;
            _isOpen = true;
        }

        public void Close()
        {
            _isOpen = false;
            _hoverSlot = -1;
            _slots.Clear();
        }

        public void Update(Vector2 mousePos)
        {
            if (!_isOpen) return;

            Vector2 center = new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f);
            Vector2 dir = mousePos - center;
            float distance = dir.Length();

            if (distance < 25f || distance > _radius * 1.3f)
            {
                _hoverSlot = -1;
                return;
            }

            float angle = MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI;
            angle = (angle + 360) % 360;
            angle = (angle + 90) % 360;

            int slotIndex = (int)(angle / SECTOR_ANGLE);
            if (slotIndex >= 0 && slotIndex < SLOT_COUNT)
                _hoverSlot = slotIndex;
            else
                _hoverSlot = -1;
        }

        public void Draw()
        {
            if (!_isOpen) return;

            Vector2 center = new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f);
            float radius = _radius;

            for (int i = 0; i < SLOT_COUNT; i++)
            {
                float startAngle = i * SECTOR_ANGLE - 90f;
                float endAngle = startAngle + SECTOR_ANGLE;
                bool isHover = (i == _hoverSlot);
                float midAngle = (startAngle + endAngle) / 2f;
                float radMid = midAngle * MathF.PI / 180f;
                Vector2 outward = new Vector2(MathF.Cos(radMid), MathF.Sin(radMid));

                Texture2D slotTexture = isHover && Program.RadialSlotOnTexture.Id != 0
                    ? Program.RadialSlotOnTexture
                    : Program.RadialSlotTexture;
                if (slotTexture.Id != 0)
                {
                    float slotSize = radius * (isHover ? 0.60f : 0.54f);
                    Vector2 slotCenter = center + outward * (radius * 0.64f);
                    Rectangle source = new Rectangle(0, 0, slotTexture.Width, slotTexture.Height);
                    Rectangle destination = new Rectangle(slotCenter.X, slotCenter.Y, slotSize, slotSize);
                    Raylib.DrawTexturePro(slotTexture, source, destination,
                        new Vector2(slotSize / 2f, slotSize / 2f), midAngle + 180f, Color.White);
                }

                // Icône de l'item
                if (i < _slots.Count && !_slots[i].IsEmpty && _slots[i].Item != null)
                {
                    float midAngleIcon = (startAngle + endAngle) / 2f;
                    float radIcon = midAngleIcon * MathF.PI / 180f;
                    float iconDist = radius * 0.64f;
                    Vector2 iconPos = center + new Vector2(MathF.Cos(radIcon), MathF.Sin(radIcon)) * iconDist;
                    float iconSize = radius * 0.30f;

                    Rectangle dest = new Rectangle(iconPos.X - iconSize / 2, iconPos.Y - iconSize / 2, iconSize, iconSize);
                    //  Toujours passer par ItemRenderer (gère lui-même le repli couches
                    // teintées / icône / couleur) plutôt que de dessiner _slots[i].Item.Icon
                    // à la main : sinon les items sans texture de base (chapka, fedora...)
                    // restent invisibles dans le menu radial.
                    ItemRenderer.DrawItemCompact(_slots[i].Item, (int)dest.X, (int)dest.Y, (int)iconSize);

                    // Quantité
                    if (_slots[i].Count > 1)
                    {
                        string text = _slots[i].Count.ToString();
                        int fs = 16;
                        int tw = FontManager.MeasureText(text, fs);
                        FontManager.DrawText(text, (int)(iconPos.X - tw / 2), (int)(iconPos.Y + iconSize / 2 + 4), fs, Color.White);
                    }
                }
                else
                {
                }

                // Numéro de la touche (1-9,0) à l'extérieur du secteur
                float labelAngle = (startAngle + endAngle) / 2f;
                float labelRad = labelAngle * MathF.PI / 180f;
                float labelDist = radius * 1.06f;
                Vector2 labelPos = center + new Vector2(MathF.Cos(labelRad), MathF.Sin(labelRad)) * labelDist;
                string num = (i == 9 ? "0" : (i + 1).ToString());
                int fsNum = 14;
                int twNum = FontManager.MeasureText(num, fsNum);
                FontManager.DrawText(num, (int)(labelPos.X - twNum / 2), (int)(labelPos.Y - fsNum / 2), fsNum, new Color(200, 200, 200, 180));
            }

            // Le sélecteur central pointe vers la case active, comme les slots qui pointent
            // vers le centre. Sans sélection, il reste orienté vers le haut.
            Texture2D selectorTexture = _hoverSlot >= 0 && Program.RadialSelectorOnTexture.Id != 0
                ? Program.RadialSelectorOnTexture
                : Program.RadialSelectorTexture;
            if (selectorTexture.Id != 0)
            {
                float selectedSlotPixelSize = radius * 0.60f /
                    (Program.RadialSlotOnTexture.Width > 0 ? Program.RadialSlotOnTexture.Width : 32f);
                float selectorScale = selectedSlotPixelSize;
                float selectorWidth = selectorTexture.Width * selectorScale;
                float selectorHeight = selectorTexture.Height * selectorScale;
                float selectorAngle = _hoverSlot >= 0
                    ? ((_hoverSlot + 0.5f) * SECTOR_ANGLE - 90f) + 180f
                    : 0f;
                Rectangle source = new Rectangle(0, 0, selectorTexture.Width, selectorTexture.Height);
                Rectangle destination = new Rectangle(center.X, center.Y, selectorWidth, selectorHeight);
                Raylib.DrawTexturePro(selectorTexture, source, destination,
                    new Vector2(selectorWidth / 2f, selectorHeight / 2f), selectorAngle, Color.White);
            }
            else
            {
                Raylib.DrawCircle((int)center.X, (int)center.Y, 32, new Color(30, 30, 40, 220));
                Raylib.DrawCircleLines((int)center.X, (int)center.Y, 32, new Color(210, 180, 100, 200));
            }

            // Affichage du nom de l'item survolé (ou "Sélectionner")
            if (_hoverSlot >= 0 && _hoverSlot < _slots.Count && !_slots[_hoverSlot].IsEmpty && _slots[_hoverSlot].Item != null)
            {
                int itemId = Program.GetItemId(_slots[_hoverSlot].Item.Name);
                string itemName = ItemRenderer.GetDisplayName(_slots[_hoverSlot].Item, itemId, Localization.GetLocalizedItemName(itemId));
                int fontSizeName = 18;
                int nameWidth = FontManager.MeasureText(itemName, fontSizeName);
                FontManager.DrawText(itemName, (int)(center.X - nameWidth / 2), (int)(center.Y - 10), fontSizeName, Color.White);
            }
            else
            {
                string hint = Localization.Get("radial.select");
                int fsHint = 16;
                int hintWidth = FontManager.MeasureText(hint, fsHint);
                FontManager.DrawText(hint, (int)(center.X - hintWidth / 2), (int)(center.Y - 8), fsHint, new Color(200, 200, 200, 180));
            }

            // Petit indicateur de slot actuel (en bas à droite ou en bas au centre) ?
            // On peut afficher le numéro du slot actuel en bas du cercle central.
            string currentNum = (_currentSlot == 9 ? "0" : (_currentSlot + 1).ToString());
            int fsCurrent = 14;
            int twCurrent = FontManager.MeasureText(currentNum, fsCurrent);
            FontManager.DrawText(currentNum, (int)(center.X - twCurrent / 2), (int)(center.Y + 32 + 6), fsCurrent, new Color(150, 150, 150, 180));
        }
    }
}