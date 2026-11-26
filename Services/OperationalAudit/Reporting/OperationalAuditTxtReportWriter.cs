using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit.Reporting
{
    /// <summary>
    /// Relatório TXT legível por humanos (seção 22) — pensado para leitura direta por quem vai agir sobre
    /// os achados, não para processamento automático (isso é papel do CSV/JSON). Mesma origem de dados dos
    /// outros dois writers: nenhuma contagem ou classificação é recalculada aqui.
    /// </summary>
    public sealed class OperationalAuditTxtReportWriter : IOperationalAuditReportWriter
    {
        public async Task<string> EscreverAsync(OperationalAuditResult resultado, string outputDirectory, CancellationToken cancellationToken = default)
        {
            string caminho = ReportFileNaming.GarantirDiretorioECaminho(outputDirectory, resultado.ExecutionId, resultado.IniciadoEmUtc, "txt");

            var sb = new StringBuilder();
            EscreverCabecalho(sb, resultado);

            var projetos = resultado.ProjetosProcessados.Count > 0
                ? resultado.ProjetosProcessados
                : resultado.Findings.Select(f => f.ProjectIdAcc)
                    .Concat(resultado.Requirements.Select(r => r.ProjectIdAcc))
                    .Concat(resultado.StageResults.Select(s => s.ProjectIdAcc))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            foreach (var projectIdAcc in projetos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EscreverProjeto(sb, resultado, projectIdAcc);
            }

            if (resultado.ErrosDeExecucao.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("=== ERROS DE EXECUCAO DA AUDITORIA (nao sao dados operacionais) ===");
                foreach (var erro in resultado.ErrosDeExecucao)
                {
                    sb.AppendLine($"- {erro}");
                }
            }

            await File.WriteAllTextAsync(caminho, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
            return caminho;
        }

        private static void EscreverCabecalho(StringBuilder sb, OperationalAuditResult resultado)
        {
            sb.AppendLine("=====================================================================");
            sb.AppendLine(" AUDITORIA OPERACIONAL - N&A CONNECT");
            sb.AppendLine("=====================================================================");
            sb.AppendLine($"ExecutionId       : {resultado.ExecutionId:N}");
            sb.AppendLine($"Iniciado em (UTC) : {resultado.IniciadoEmUtc:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Finalizado em(UTC): {(resultado.FinalizadoEmUtc.HasValue ? resultado.FinalizadoEmUtc.Value.ToString("yyyy-MM-dd HH:mm:ss") : "(em andamento)")}");
            sb.AppendLine($"Status            : {resultado.Status}");
            sb.AppendLine($"Projetos          : {resultado.ProjetosProcessados.Count}");
            sb.AppendLine($"Total de achados  : {resultado.Findings.Count}");
            sb.AppendLine($"Total de requisitos avaliados: {resultado.Requirements.Count}");

            if (resultado.PersistenciaSqlFalhou)
            {
                sb.AppendLine();
                sb.AppendLine("*** ATENCAO: a persistencia no banco de dados (schema audit) FALHOU nesta execucao. ***");
                sb.AppendLine("*** Este relatorio reflete os achados desta execucao, mas eles NAO foram salvos no banco. ***");
                if (!string.IsNullOrWhiteSpace(resultado.PersistenciaSqlErro))
                {
                    sb.AppendLine($"Erro: {resultado.PersistenciaSqlErro}");
                }
            }

            sb.AppendLine("=====================================================================");
        }

        private static void EscreverProjeto(StringBuilder sb, OperationalAuditResult resultado, string projectIdAcc)
        {
            var stages = resultado.StageResults.Where(s => string.Equals(s.ProjectIdAcc, projectIdAcc, StringComparison.OrdinalIgnoreCase)).ToList();
            var requirements = resultado.Requirements.Where(r => string.Equals(r.ProjectIdAcc, projectIdAcc, StringComparison.OrdinalIgnoreCase)).ToList();
            var findings = resultado.Findings.Where(f => string.Equals(f.ProjectIdAcc, projectIdAcc, StringComparison.OrdinalIgnoreCase)).ToList();
            string? nomeProjeto = findings.Select(f => f.Projeto).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

            sb.AppendLine();
            sb.AppendLine($"--- PROJETO: {projectIdAcc}{(string.IsNullOrWhiteSpace(nomeProjeto) ? string.Empty : $" ({nomeProjeto})")} ---");

            if (stages.Count > 0)
            {
                sb.AppendLine("  Etapas:");
                foreach (var stage in stages.OrderBy(s => s.Etapa, StringComparer.OrdinalIgnoreCase))
                {
                    string conformidade = stage.PercentualConformidade.HasValue
                        ? stage.PercentualConformidade.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%"
                        : "N/A";
                    sb.AppendLine($"    Etapa {stage.Etapa} ({stage.NomeEtapa}): {stage.Resultado} | Conformidade: {conformidade} | " +
                                  $"Aprovados: {stage.Aprovados}/{stage.TotalRequisitos} | Falhos: {stage.Falhos} | Avisos: {stage.Avisos} | Bloqueadores: {stage.Bloqueadores}");
                }
            }

            int reprovados = requirements.Count(r => r.Status == OperationalRequirementStatus.Failed);
            sb.AppendLine($"  Requisitos avaliados: {requirements.Count} (reprovados: {reprovados})");

            if (findings.Count == 0)
            {
                sb.AppendLine("  Achados: nenhum.");
                return;
            }

            sb.AppendLine($"  Achados ({findings.Count}):");
            foreach (var f in findings.OrderByDescending(f => f.Severidade).ThenBy(f => f.Etapa, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"    [{f.Severidade}] [{f.Etapa}/{f.CodigoProblema}] {f.Titulo}");
                if (!string.IsNullOrWhiteSpace(f.Descricao))
                {
                    sb.AppendLine($"        Descricao: {f.Descricao}");
                }
                sb.AppendLine($"        Dominio: {f.ErrorDomain} | Classificacao: {f.Classificacao} | Status: {f.Status} | Ocorrencias: {f.QuantidadeOcorrencias}");
                if (!string.IsNullOrWhiteSpace(f.ValorEsperado) || !string.IsNullOrWhiteSpace(f.ValorEncontrado))
                {
                    sb.AppendLine($"        Esperado: {f.ValorEsperado ?? "(n/a)"} | Encontrado: {f.ValorEncontrado ?? "(n/a)"}");
                }
                if (!string.IsNullOrWhiteSpace(f.AcaoRecomendada))
                {
                    sb.AppendLine($"        Acao recomendada: {f.AcaoRecomendada}");
                }
            }
        }
    }
}
