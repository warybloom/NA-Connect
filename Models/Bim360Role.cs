using System.Text.Json.Serialization;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Representa uma "Industry Role" da API legada do BIM 360 (HQ v2).
    /// </summary>
    public class Bim360Role
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}