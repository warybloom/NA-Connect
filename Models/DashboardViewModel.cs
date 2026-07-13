using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APS.WebApp.Models // Verifique o namespace
{
    public class DashboardViewModel
    {
        [JsonPropertyName("userName")]
        public string UserName { get; set; } = "";

        [JsonPropertyName("projects")]
        public List<ProjectViewModel> Projects { get; set; } = new List<ProjectViewModel>();
    }
}