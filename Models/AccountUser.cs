// Pasta: C:/.../AccIntegration.WebApp/Models/
// Ficheiro: AccountUser.cs
using System.Text.Json.Serialization;

namespace APS.WebApp.Models // <-- Namespace para os modelos
{
    public class AccountUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; }
    }
}