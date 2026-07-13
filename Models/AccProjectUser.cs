using System.Text.Json.Serialization;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Representa os dados de um utilizador que já foi adicionado a um projeto.
    /// </summary>
    public class AccProjectUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        // ✅ CORREÇÃO: O erro de digitação "autodeskld" foi corrigido para "autodeskId".
        [JsonPropertyName("autodeskId")]
        public string AutodeskId { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;
    }
}