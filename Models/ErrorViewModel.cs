// Ficheiro: Models/ErrorViewModel.cs
using System.Diagnostics;

namespace APS.WebApp.Models // Garante que o namespace está correto
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}