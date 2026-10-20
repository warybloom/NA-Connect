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

    /// <summary>
    /// Espelha uma linha de dbo.ACC_Attribute_Sync_State: o último payload de atributos personalizados
    /// enviado com sucesso (ou a última tentativa, se ela falhou) para uma VersionUrn + ChaveConfiguracao
    /// específica. Usado como cache persistente para evitar reenviar ao ACC um payload já sincronizado.
    /// </summary>
    public class AccAttributeSyncState
    {
        public long Id { get; set; }
        public string ProjectIdAcc { get; set; }
        public string VersionUrn { get; set; }
        public string ChaveConfiguracao { get; set; }
        public string PayloadHash { get; set; }
        public string FileName { get; set; }
        public string PastaEncontrada { get; set; }
        public bool UltimaTentativaSucesso { get; set; }
        public DateTime DataUltimaTentativa { get; set; }
        public DateTime? DataUltimoSucesso { get; set; }
        public string UltimoErro { get; set; }
    }

    /// <summary>
    /// Espelha uma linha de sys.columns/sys.types para UMA coluna de uma tabela (ver
    /// DatabaseService.ObterEsquemaColunas) — usado apenas para leitura/diagnóstico (nunca para gerar
    /// DDL): validação preventiva de valores DECIMAL/NUMERIC antes do envio ao SQL (Cronograma Tipo 1).
    /// </summary>
    public class ColunaEsquemaSql
    {
        public string Coluna { get; set; }
        public string Tipo { get; set; }
        public int Precision { get; set; }
        public int Scale { get; set; }
        public int MaxLength { get; set; }
        public bool IsNullable { get; set; }
    }
}