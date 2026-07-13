using System.Text.Json.Serialization;

namespace APS.WebApp.Models // Verifique o seu namespace
{
    public class ProjectViewModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("hubId")]
        public string HubId { get; set; } = "";

        [JsonPropertyName("rootFolderId")]
        public string RootFolderId { get; set; } = "";
    }
}