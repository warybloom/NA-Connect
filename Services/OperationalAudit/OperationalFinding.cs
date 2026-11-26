using System;

namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Uma ocorrência estruturada de problema/observação produzida por uma regra de auditoria (Checkpoint
    /// 2+). Campos alinhados 1:1 com a seção 5 da matriz da Fase 1, mais os campos novos da correção da
    /// Fase 2 (<see cref="ErrorDomain"/>, seção 7, e os campos de atributo personalizado, seção 8).
    ///
    /// TXT, CSV, JSON e a persistência em <c>audit.AuditIssue</c>/<c>audit.AuditOccurrence</c> derivam
    /// TODOS desta mesma coleção (seção 16) — nunca existe uma segunda lógica de classificação para uma
    /// saída específica. <see cref="DedupHash"/>/<see cref="OccurrenceHash"/> são preenchidos pelo
    /// orquestrador (via <c>DedupHashCalculator</c>) antes de qualquer saída ser gerada, nunca calculados
    /// de forma diferente por escritor.
    /// </summary>
    public sealed class OperationalFinding
    {
        public Guid ExecutionId { get; init; }
        public DateTime DataHoraUtc { get; init; }

        public string Projeto { get; init; } = string.Empty;
        public string ProjectIdAcc { get; init; } = string.Empty;
        public string? AccountIdAcc { get; init; }

        /// <summary>"0".."17", ou "5B".</summary>
        public string Etapa { get; init; } = string.Empty;
        public string? Subetapa { get; init; }

        /// <summary>Identificador da regra que produziu o achado (ex.: "CRN-005", "DNA-002").</summary>
        public string RegraId { get; init; } = string.Empty;
        public string CodigoProblema { get; init; } = string.Empty;

        public string Titulo { get; init; } = string.Empty;
        public string? Descricao { get; init; }

        public OperationalAuditErrorDomain ErrorDomain { get; init; }
        public OperationalAuditClassification Classificacao { get; init; }
        public OperationalAuditSeverity Severidade { get; init; }

        public string? OrigemProvavel { get; init; }
        public string? ResponsavelProvavel { get; init; }

        public string? DocumentoNa { get; init; }
        public string? DocumentoCliente { get; init; }
        public string? NomeArquivo { get; init; }
        public string? Extensao { get; init; }

        public string? PastaEsperada { get; init; }
        public string? PastaEncontrada { get; init; }
        public string? AbaEsperada { get; init; }
        public string? AbaEncontrada { get; init; }
        public int? Linha { get; init; }
        public string? Coluna { get; init; }
        public string? Campo { get; init; }
        public string? ValorEsperado { get; init; }
        public string? ValorEncontrado { get; init; }

        public string? FonteEvidencia { get; init; }
        public string? ArquivoLog { get; init; }
        public int? LinhaLogInicial { get; init; }
        public int? LinhaLogFinal { get; init; }
        public string? Evidencia { get; init; }

        public string? AcaoRecomendada { get; init; }
        public bool BloqueiaEtapa { get; init; }
        public bool AutomacaoPodeCorrigir { get; init; }

        // ---------------------------------------------------------------------------------------
        // Seção 8 da correção da Fase 2 — atributos personalizados do ACC (Stage07, Checkpoint 2).
        // Todos nulos para achados que não são sobre um atributo específico.
        // ---------------------------------------------------------------------------------------
        public string? CustomAttributeName { get; init; }
        public int? CustomAttributeIdAcc { get; init; }
        public CustomAttributeState? CustomAttributeState { get; init; }
        public bool? IsCustomAttributeHidden { get; init; }

        /// <summary>Ex.: ENGENHARIA_04 / PRODUCAO_DOCUMENTOS_09 / PRODUCAO_10 / PRODUCAO_MODELOS_3D.</summary>
        public string? ConfigurationKey { get; init; }

        // ---------------------------------------------------------------------------------------
        // Preenchidos pelo orquestrador (DedupHashCalculator + AuditIssueDedupEngine), nunca pela regra.
        // ---------------------------------------------------------------------------------------

        /// <summary>Versão normalizada e estável do motivo, usada como uma das entradas do DedupHash.</summary>
        public string? MotivoNormalizado { get; init; }

        public string? DedupHash { get; set; }
        public string? OccurrenceHash { get; set; }

        public OperationalIssueStatus Status { get; set; } = OperationalIssueStatus.Aberto;
        public DateTime? PrimeiraOcorrencia { get; set; }
        public DateTime? UltimaOcorrencia { get; set; }
        public int QuantidadeOcorrencias { get; set; } = 1;
    }
}
