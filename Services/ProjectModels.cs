// Modulo: NA_Connect.Models/ProjectDiscoveryResult.cs
using System;

namespace NA_Connect.Models
{
    public class ProjectDiscoveryResult
    {
        public string NamingStandard { get; set; } // "NA" ou "CLIENT"
        public string Language { get; set; }       // "PT" ou "EN"
        public string LdpName { get; set; }        // 🎯 PROPRIEDADE ADICIONADA: Nome do ficheiro LDP
        public string HubId { get; set; }          // Para persistência futura
        public DateTime LastDiscovery { get; set; } = DateTime.Now;
    }
}