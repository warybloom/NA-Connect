using System;
using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// UM documento lógico (DWG+PDF já consolidados) pronto para persistência em
    /// [dbo].[ACC_Review_Current_Stage] — construído inteiramente por AccReviewStageSnapshotService antes
    /// de qualquer gravação (seção 7/8 da funcionalidade "ACC_REVIEW_STAGE"). As listas (ArquivosFisicos/
    /// ItemUrns/VersionUrns/PastasEncontradas) são serializadas como texto separado por "; " no DataTable.
    /// </summary>
    public sealed class RegistroSnapshotEtapaReviewPreparado
    {
        public string ReviewId { get; set; }
        public string ReviewNumber { get; set; }
        public string ReviewName { get; set; }
        public string ReviewStatus { get; set; }

        public string WorkflowId { get; set; }
        public string CurrentStepId { get; set; }
        public string EtapaAtual { get; set; }

        public List<string> ResponsibleNames { get; set; } = new List<string>();
        public List<string> ResponsibleIds { get; set; } = new List<string>();
        public string OrigemResponsavel { get; set; }

        public DateOnly? DataVencimentoEtapa { get; set; }
        public string OrigemVencimento { get; set; }

        public string NumeroDocumento { get; set; }
        public string NumeroDocumentoNormalizado { get; set; }

        public List<string> ArquivosFisicos { get; set; } = new List<string>();
        public List<string> ItemUrns { get; set; } = new List<string>();
        public List<string> VersionUrns { get; set; } = new List<string>();
        public List<string> PastasEncontradas { get; set; } = new List<string>();

        public string StatusNoFluxo { get; set; }
        public DateTime? DataAtualizacaoReview { get; set; }
    }

    /// <summary>
    /// Resultado completo de UMA execução de AccReviewStageSnapshotService.SincronizarSnapshotAsync
    /// (seção 13/16 do pedido) — sempre com contadores discriminados, nunca "sucesso genérico".
    /// </summary>
    public sealed class ResultadoSnapshotEtapaReview
    {
        public bool Sucesso { get; set; }
        public string CodigoResultado { get; set; } = "OK";
        public string Mensagem { get; set; } = string.Empty;

        public int ReviewsAnalisados { get; set; }
        public int ReviewsAbertos { get; set; }
        public int DocumentosFisicos { get; set; }
        public int DocumentosLogicos { get; set; }
        public int DocumentosComResponsavel { get; set; }
        public int DocumentosSemResponsavel { get; set; }
        public int DocumentosComVencimento { get; set; }
        public int DocumentosSemVencimento { get; set; }
        public int Conflitos { get; set; }

        public int RegistrosAnteriores { get; set; }
        public int RegistrosInseridos { get; set; }
        public bool RollbackExecutado { get; set; }

        public override string ToString() => Mensagem;
    }
}
