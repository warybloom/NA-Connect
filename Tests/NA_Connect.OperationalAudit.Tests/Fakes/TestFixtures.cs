using System;
using System.Collections.Generic;
using System.Data;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Configuration;
using APS.WebApp.Services.OperationalAudit.ReadOnly;

namespace NA_Connect.OperationalAudit.Tests.Fakes
{
    /// <summary>Construtores compartilhados por todos os testes de regra do Checkpoint 2 — só monta dados, nunca lógica de negócio.</summary>
    internal static class TestFixtures
    {
        public static OperationalAuditContext NovoContexto(string projectIdAcc = "PRJ-1", OperationalAuditOptions? options = null) =>
            new OperationalAuditContext
            {
                ExecutionId = Guid.NewGuid(),
                IniciadoEmUtc = DateTime.UtcNow,
                ProjectIdAcc = projectIdAcc,
                Options = options ?? new OperationalAuditOptions()
            };

        public static AccFolderRef Pasta(string folderId, string nome) =>
            new AccFolderRef { FolderId = folderId, Name = nome, ResolvedPath = nome };

        public static AccItemRef Item(string itemId, string nome, string parentFolderId, string parentFolderName, string? versionUrn = "urn:1", bool oculta = false, DateTime? modificadoEm = null, string caminhoRelativo = "") =>
            new AccItemRef
            {
                ItemId = itemId,
                Name = nome,
                Extension = System.IO.Path.GetExtension(nome).TrimStart('.').ToUpperInvariant(),
                CaminhoRelativoAPastaRaiz = caminhoRelativo,
                ParentFolderId = parentFolderId,
                ParentFolderName = parentFolderName,
                LatestVersionUrn = versionUrn,
                IsHiddenFolder = oculta,
                LastModifiedUtc = modificadoEm
            };

        public static AccFolderListingResult Listagem(params AccItemRef[] itens) =>
            new AccFolderListingResult { Itens = itens, QuantidadeDePaginas = 1, QuantidadePorPagina = new[] { itens.Length } };

        public static AuditWorksheetSnapshot Aba(string nome, string[] cabecalhos, Dictionary<int, Dictionary<string, string?>> linhas, bool oculta = false, HashSet<string>? formulas = null)
        {
            var linhasConvertidas = new Dictionary<int, IReadOnlyDictionary<string, string?>>();
            foreach (var (numero, valores) in linhas) linhasConvertidas[numero] = valores;

            return new AuditWorksheetSnapshot
            {
                Nome = nome,
                Oculta = oculta,
                LinhaDeCabecalho = 1,
                Cabecalhos = cabecalhos,
                TotalDeLinhasFisicas = linhas.Count + 1,
                Linhas = linhasConvertidas,
                CelulasComFormula = formulas ?? new HashSet<string>()
            };
        }

        public static AuditWorkbookSnapshot Workbook(params AuditWorksheetSnapshot[] abas)
        {
            var dict = new Dictionary<string, AuditWorksheetSnapshot>();
            var nomes = new List<string>();
            foreach (var aba in abas)
            {
                dict[aba.Nome] = aba;
                nomes.Add(aba.Nome);
            }
            return new AuditWorkbookSnapshot { NomesDasAbas = nomes, Abas = dict };
        }

        public static DataTable Tabela(string[] colunas, params object?[][] linhas)
        {
            var tabela = new DataTable();
            foreach (var coluna in colunas) tabela.Columns.Add(coluna, typeof(object));
            foreach (var linha in linhas) tabela.Rows.Add(linha);
            return tabela;
        }

        public static AuditReviewRef Review(string id, string status, DateTime iniciadaEmUtc, DateTime? finalizadaEmUtc = null, string? currentStepId = null, params AuditReviewVersionVerdictRef[] vereditos) =>
            new AuditReviewRef
            {
                ReviewId = id,
                Status = status,
                IniciadaEmUtc = iniciadaEmUtc,
                FinalizadaEmUtc = finalizadaEmUtc,
                CurrentStepId = currentStepId,
                Vereditos = vereditos
            };

        /// <summary>Checkpoint 3 (Stage05/05B) — mesma coisa que <see cref="Review"/>, mas com os campos aditivos WorkflowId/CurrentStepDueDateRaw.</summary>
        public static AuditReviewRef ReviewComEtapa(string id, string status, DateTime iniciadaEmUtc, DateTime? finalizadaEmUtc, string? currentStepId,
            string? workflowId, string? currentStepDueDateRaw, params AuditReviewVersionVerdictRef[] vereditos) =>
            new AuditReviewRef
            {
                ReviewId = id,
                Status = status,
                IniciadaEmUtc = iniciadaEmUtc,
                FinalizadaEmUtc = finalizadaEmUtc,
                CurrentStepId = currentStepId,
                WorkflowId = workflowId,
                CurrentStepDueDateRaw = currentStepDueDateRaw,
                Vereditos = vereditos
            };

        public static AuditReviewVersionVerdictRef Veredito(string versionUrn, string? approveStatus) =>
            new AuditReviewVersionVerdictRef { VersionUrn = versionUrn, ApproveStatus = approveStatus };
    }
}
