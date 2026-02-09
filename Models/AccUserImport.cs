using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace APS.WebApp.Models
{
    public class AccUserImport
    {
        [JsonPropertyName("users")]
        public List<AccUser> Users { get; set; } = new List<AccUser>();
    }

    public class AccUser
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("products")]
        public List<AccUserProductAccess> Products { get; set; } = new List<AccUserProductAccess>();
    }

    public class AccUserProductAccess
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("access")]
        public string Access { get; set; } = string.Empty;
    }
}