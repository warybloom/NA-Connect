using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

// Mesma biblioteca MPXJ.Net já usada por CronogramaTipo2Parser.cs/AccScheduleSyncService.cs — nenhum
// segundo parser de MPP foi adicionado. net.sf.mpxj.Task colide em nome com System.Threading.Tasks.Task,
// mesmo alias já usado no resto do código.
using net.sf.mpxj;
using net.sf.mpxj.reader;
using MpxjTask = net.sf.mpxj.Task;

namespace NA_Connect.Services.Ldp
{
    public sealed class LdpMppPreviewResultado
    {
        public List<string> CamposDisponiveis { get; set; } = new();
        public List<Dictionary<string, string>> Linhas { get; set; } = new();
        public List<string> Avisos { get; set; } = new();
    }

    /// <summary>
    /// Leitura GENÉRICA de .mpp para o Novo Motor de LDP — item 9 do pedido: "NÃO assumir nomes. Ler o
    /// arquivo real e mostrar os campos efetivamente presentes." Diferente de
    /// CronogramaTipo2FieldResolver (que resolve aliases de negócio específicos do Cronograma Tipo 2),
    /// este leitor só enumera o que existe e tem valor real em pelo menos uma tarefa — sem lógica de
    /// negócio, sem prioridade entre candidatos. Cada getter nativo é tentado individualmente com
    /// try/catch: uma falha num campo nunca derruba a leitura dos demais.
    /// </summary>
    public class LdpMppParserService
    {
        private readonly ILogger<LdpMppParserService> _logger;

        public LdpMppParserService(ILogger<LdpMppParserService> logger)
        {
            _logger = logger;
        }

        private static readonly (string Label, Func<MpxjTask, object> Getter)[] CamposNativos =
        {
            ("ID", t => t.getID()),
            ("Unique ID", t => t.getUniqueID()),
            ("WBS", t => t.getWBS()),
            ("Task Name", t => t.getName()),
            ("Outline Level", t => t.getOutlineLevel()),
            ("Start", t => t.getStart()),
            ("Finish", t => t.getFinish()),
            ("Baseline Start", t => t.getBaselineStart()),
            ("Baseline Finish", t => t.getBaselineFinish()),
            ("Duration", t => t.getDuration()),
            ("% Complete", t => t.getPercentageComplete()),
            ("Resource Names", t => t.getResourceNames()),
            ("Predecessors", t => t.getPredecessors()),
        };

        public LdpMppPreviewResultado Ler(byte[] conteudoMpp, string nomeArquivoOriginal, int maxLinhasPreview = 50)
        {
            var resultado = new LdpMppPreviewResultado();
            string tempPath = Path.Combine(Path.GetTempPath(), $"ldp_mpp_{Guid.NewGuid():N}{Path.GetExtension(nomeArquivoOriginal)}");
            File.WriteAllBytes(tempPath, conteudoMpp);

            try
            {
                ProjectFile projectFile;
                try
                {
                    try { java.lang.System.setProperty("log4j2.loggerContextFactory", "org.apache.logging.log4j.simple.SimpleLoggerContextFactory"); } catch { }
                    projectFile = new UniversalProjectReader().read(tempPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[LDP][MPP] Falha ao abrir '{Arquivo}'.", nomeArquivoOriginal);
                    throw new InvalidOperationException($"Não foi possível ler o arquivo MPP: {ex.Message}", ex);
                }

                var tasks = projectFile.getTasks().Cast<MpxjTask>()
                    .Where(t => t != null && t.getID() != null)
                    .ToList();

                if (tasks.Count == 0)
                {
                    resultado.Avisos.Add("O arquivo foi lido, mas nenhuma tarefa foi encontrada.");
                    return resultado;
                }

                // Campos nativos — só entram na lista se ao menos 1 tarefa tiver valor real.
                var nativosComValor = new List<(string Label, Func<MpxjTask, object> Getter)>();
                foreach (var campo in CamposNativos)
                {
                    bool temValor = false;
                    foreach (var t in tasks)
                    {
                        object valor = null;
                        try { valor = campo.Getter(t); } catch { /* getter indisponível nesta versão/arquivo — ignora só este campo */ }
                        if (valor != null && !string.IsNullOrWhiteSpace(valor.ToString()))
                        {
                            temValor = true;
                            break;
                        }
                    }
                    if (temValor) nativosComValor.Add(campo);
                }

                // Campos customizados (aliases reais definidos no MPP) — mesma técnica de enumeração de
                // CronogramaTipo2FieldResolver, sem resolver nenhum alias específico de negócio aqui.
                var customComAlias = new List<(string Alias, FieldType Tipo)>();
                try
                {
                    var customFields = projectFile.getCustomFields();
                    var it = customFields?.iterator();
                    while (it != null && it.hasNext())
                    {
                        if (it.next() is CustomField field)
                        {
                            var alias = field.getAlias()?.ToString();
                            if (!string.IsNullOrWhiteSpace(alias)) customComAlias.Add((alias, field.getFieldType()));
                        }
                    }
                }
                catch (Exception ex)
                {
                    resultado.Avisos.Add($"Não foi possível enumerar campos customizados: {ex.Message}");
                }

                var customComValor = new List<(string Alias, FieldType Tipo)>();
                foreach (var cf in customComAlias)
                {
                    bool temValor = tasks.Any(t =>
                    {
                        try { var v = t.get(cf.Tipo); return v != null && !string.IsNullOrWhiteSpace(v.ToString()); }
                        catch { return false; }
                    });
                    if (temValor) customComValor.Add(cf);
                }

                resultado.CamposDisponiveis = nativosComValor.Select(c => c.Label)
                    .Concat(customComValor.Select(c => c.Alias))
                    .Distinct()
                    .ToList();

                foreach (var t in tasks.Take(maxLinhasPreview))
                {
                    var linha = new Dictionary<string, string>();
                    foreach (var campo in nativosComValor)
                    {
                        string valor = "";
                        try { valor = campo.Getter(t)?.ToString() ?? ""; } catch { }
                        linha[campo.Label] = valor;
                    }
                    foreach (var cf in customComValor)
                    {
                        string valor = "";
                        try { valor = t.get(cf.Tipo)?.ToString() ?? ""; } catch { }
                        linha[cf.Alias] = valor;
                    }
                    resultado.Linhas.Add(linha);
                }

                if (tasks.Count > maxLinhasPreview)
                    resultado.Avisos.Add($"Preview limitado às primeiras {maxLinhasPreview} de {tasks.Count} tarefas encontradas.");

                return resultado;
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* best-effort — arquivo temporário, não crítico */ }
            }
        }
    }
}
