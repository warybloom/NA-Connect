// Ficheiro: Models/VersionViewModel.cs
using System.Text.Json.Serialization;

namespace APS.WebApp.Models // Garante que o namespace está correto para o seu projeto
{
    public class VersionViewModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int VersionNumber { get; set; }
        public string LastModifiedTime { get; set; }
        public string LastModifiedUserName { get; set; }
    }
}