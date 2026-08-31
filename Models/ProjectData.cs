// Ficheiro: ProjectData.cs
// Pasta: Models/
#nullable disable
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Representa os dados necessários para criar um novo projeto no BIM 360 / ACC.
    /// </summary>
    public class ProjectData
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        // CORREÇÃO: O nome do parâmetro JSON deve ser "project_type" e não "type".
        [JsonPropertyName("project_type")]
        public string Type { get; set; }

        [JsonPropertyName("service_types")]
        public string ServiceTypes { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = "active";

        [JsonPropertyName("start_date")]
        public string StartDate { get; set; }

        [JsonPropertyName("end_date")]
        public string EndDate { get; set; }

        [JsonPropertyName("value")]
        public decimal? Value { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; }
    }
}