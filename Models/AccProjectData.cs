using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace APS.WebApp.Models
{
    // Classe para o objeto aninhado 'template'
    public class ProjectTemplateInfo
    {
        [JsonPropertyName("projectId")]
        public string ProjectId { get; set; } = string.Empty;
    }

    public class AccProjectData
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("startDate")]
        public string? StartDate { get; set; }

        [JsonPropertyName("endDate")]
        public string? EndDate { get; set; }

        [JsonPropertyName("jobNumber")]
        public string? JobNumber { get; set; }

        // ✅ CORREÇÃO: Adicionado o objeto 'template' conforme a documentação.
        [JsonPropertyName("template")]
        public ProjectTemplateInfo? Template { get; set; }

        // A propriedade "products" foi removida daqui para ser usada condicionalmente no controller
    }

    // A classe AccProduct permanece a mesma
    public class AccProduct
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;
    }
}