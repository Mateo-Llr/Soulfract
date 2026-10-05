// StructureData.cs
using System.Text.Json.Serialization;
using System.Numerics;

namespace Soulfract
{
    public class StructureData
    {
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int MinX { get; set; }
        public int MinY { get; set; }
        public int MaxX { get; set; }
        public int MaxY { get; set; }
        public List<StructureTileData> Tiles { get; set; } = new();
    }

    public class StructureTileData
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int GroundTileId { get; set; }
        public int PlacedTileId { get; set; } // 0 si aucun objet placé
        public int OverlayId { get; set; }    // 0 si aucun overlay (minerai, toit)
        public int DecorationIndex { get; set; } = -1;
        public int VariationIndex { get; set; } = -1;
        public int Height { get; set; }
        public ContainerInventorySave? ContainerData { get; set; }
        public ArmorStandSaveData? ArmorStandData { get; set; }
        public CropSaveData? CropData { get; set; }
    }

    public class StructuresRoot
    {
        public List<StructureData> Structures { get; set; } = new();
    }
}