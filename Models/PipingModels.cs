using System.Text.Json.Serialization;

namespace ForgeSync.Modules.Piping.Models
{
    // 1. Modelos Base dos JSONs
    public class EngineeringItem
    {
        [JsonPropertyName("PnPID")]
        public int PnPID { get; set; }
        
        [JsonPropertyName("Class")]
        public string Class { get; set; }
        
        [JsonPropertyName("Tag")]
        public string Tag { get; set; }
        
        [JsonPropertyName("ShortDescription")]
        public string ShortDescription { get; set; }
        
        [JsonPropertyName("Manufacturer")]
        public string Manufacturer { get; set; }
        
        [JsonPropertyName("Material")]
        public string Material { get; set; }
    }

    public class P3dLineGroup
    {
        [JsonPropertyName("PnPID")]
        public int PnPID { get; set; }
        
        [JsonPropertyName("Tag")]
        public string Tag { get; set; }
        
        [JsonPropertyName("Number")]
        public string Number { get; set; } // Número da Linha
        
        [JsonPropertyName("NominalSpec")]
        public string NominalSpec { get; set; }
        
        [JsonPropertyName("NominalSize")]
        public string NominalSize { get; set; }
    }

    public class P3dLineGroupPartRelationship
    {
        [JsonPropertyName("LineGroupId")]
        public int LineGroupId { get; set; }
        
        [JsonPropertyName("PartId")]
        public int PartId { get; set; }
    }

    public class PipeRunComponent
    {
        [JsonPropertyName("PnPID")]
        public int PnPID { get; set; }
        
        [JsonPropertyName("ItemCode")]
        public string ItemCode { get; set; }
        
        [JsonPropertyName("Length")]
        public double? Length { get; set; }
        
        [JsonPropertyName("Weight")]
        public double? Weight { get; set; }
        
        [JsonPropertyName("SpoolNumber")]
        public string SpoolNumber { get; set; }
    }

    public class Valve
    {
        [JsonPropertyName("PnPID")]
        public int PnPID { get; set; }
    }

    public class Equipment
    {
        [JsonPropertyName("PnPID")]
        public int PnPID { get; set; }
    }

    public class Support
    {
        [JsonPropertyName("PnPID")]
        public int PnPID { get; set; }
    }

    public class Instrument
    {
        [JsonPropertyName("PnPID")]
        public int PnPID { get; set; }
    }

    // 2. Modelo Unificado de Saída (Para o SQL Server)
    public class UnifiedPipingItem
    {
        public int PnPID { get; set; }
        public string Class { get; set; }
        public string LineNumber { get; set; }
        public string LineSpec { get; set; }
        public string Tag { get; set; }
        public string ShortDescription { get; set; }
        public string ItemCode { get; set; }
        public string SpoolNumber { get; set; }
        public double? Length { get; set; }
        public double? Weight { get; set; }
        public string Material { get; set; }
        public string Manufacturer { get; set; }
        public bool IsValve { get; set; }
        public bool IsEquipment { get; set; }
        public bool IsSupport { get; set; }
        public bool IsInstrument { get; set; }
    }
}