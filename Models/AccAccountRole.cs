using System.Text.Json.Serialization;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Representa uma função (role) a nível de conta no Autodesk Construction Cloud.
    /// </summary>
    public class AccAccountRole
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}