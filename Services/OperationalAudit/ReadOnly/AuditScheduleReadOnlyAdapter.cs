using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using net.sf.mpxj;
using net.sf.mpxj.reader;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    /// <summary>
    /// Implementação real de <see cref="IAuditScheduleReadOnlyAdapter"/> via MPXJ.Net — mesma biblioteca de
    /// leitura usada pelo operacional (<c>AccScheduleSyncService</c>), mas SEM depender daquela classe
    /// (subsistema paralelo, seção 1/12 do Checkpoint 2): abre o `.mpp` a partir de um arquivo temporário
    /// EXCLUSIVO da auditoria (nunca o caminho do MPP de produção), lê, e apaga o temporário.
    ///
    /// Simplificação deliberada e documentada: resolve REALIZADO/PREVISTO só pelo alias controlado exato
    /// (<see cref="AliasesRealizado"/>/<see cref="AliasesPrevisto"/>), sem replicar o fallback estrutural por
    /// título de Tabela/Coluna que o operacional tem (`ResolverRealizadoViaTituloColunaTabela`). Quando o
    /// alias não resolve, o campo fica <c>null</c> — a regra (Stage09ScheduleAuditRule) reporta isso como
    /// "não resolvido" (CRN-003/004), nunca como uma tentativa de adivinhar o campo certo.
    /// </summary>
    public sealed class AuditScheduleReadOnlyAdapter : IAuditScheduleReadOnlyAdapter
    {
        private static readonly string[] AliasesRealizado =
        {
            "% REALIZADO", "REALIZADO", "PERCENTUAL REALIZADO",
            "PHYSICAL COMPLETED", "% PHYSICAL COMPLETED", "PERCENT PHYSICAL COMPLETED"
        };

        private static readonly string[] AliasesPrevisto =
        {
            "% PREVISTO", "PREVISTO", "PERCENTUAL PREVISTO",
            "PLANNED PHYSICAL", "% PLANNED PHYSICAL", "PERCENT PREDICTED PHYSICAL"
        };

        public AuditScheduleSnapshot LerSomenteLeitura(Stream conteudoMpp)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"OperationalAudit_MPP_{Guid.NewGuid():N}.mpp");
            try
            {
                using (var arquivoTemp = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    conteudoMpp.CopyTo(arquivoTemp);
                }

                ProjectFile projeto = new UniversalProjectReader().read(tempPath);

                FieldType? tipoPrevisto = ResolverPorAlias(projeto, AliasesPrevisto);
                FieldType? tipoRealizado = ResolverPorAlias(projeto, AliasesRealizado);
                bool colidem = tipoPrevisto != null && tipoRealizado != null && tipoPrevisto.Equals(tipoRealizado);
                if (colidem) tipoRealizado = null; // mesma regra do operacional: colisão nunca é resolução válida.

                var tarefas = new List<AuditScheduleTaskRef>();
                var it = projeto.getTasks().iterator();
                while (it.hasNext())
                {
                    var tarefa = (net.sf.mpxj.Task)it.next();
                    if (tarefa.getSummary() == true) continue; // linhas de resumo/WBS não são tarefas reais.

                    double? valorRealizado = tipoRealizado != null ? ExtrairNumero(tarefa, tipoRealizado) : null;
                    double? valorPrevisto = tipoPrevisto != null ? ExtrairNumero(tarefa, tipoPrevisto) : null;

                    tarefas.Add(new AuditScheduleTaskRef
                    {
                        Uid = tarefa.getUniqueID()?.ToString() ?? string.Empty,
                        Nome = tarefa.getName() ?? string.Empty,
                        CampoRealizadoResolvido = tipoRealizado?.ToString(),
                        ValorRealizado = valorRealizado,
                        CampoPrevistoResolvido = tipoPrevisto?.ToString(),
                        ValorPrevisto = valorPrevisto
                    });
                }

                return new AuditScheduleSnapshot { Tarefas = tarefas, RealizadoEPrevistoColidem = colidem };
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* best-effort — nunca falha a leitura por causa da limpeza do temp. */ }
                }
            }
        }

        private static FieldType? ResolverPorAlias(ProjectFile projeto, string[] aliasesControlados)
        {
            var aliasesNormalizados = new HashSet<string>(aliasesControlados.Select(Normalizar));
            var candidatos = new List<FieldType>();

            try
            {
                var customFields = projeto.getCustomFields();
                if (customFields != null)
                {
                    var it = customFields.iterator();
                    while (it.hasNext())
                    {
                        var field = (CustomField)it.next();
                        string? aliasOriginal = field.getAlias()?.ToString();
                        if (string.IsNullOrWhiteSpace(aliasOriginal)) continue;
                        if (aliasesNormalizados.Contains(Normalizar(aliasOriginal)))
                        {
                            candidatos.Add(field.getFieldType());
                        }
                    }
                }
            }
            catch
            {
                return null;
            }

            var distintos = candidatos.Distinct().ToList();
            return distintos.Count == 1 ? distintos[0] : null; // zero ou ambíguo -> não resolvido, nunca adivinhado.
        }

        private static double? ExtrairNumero(net.sf.mpxj.Task tarefa, FieldType tipo)
        {
            try
            {
                object? valor = tarefa.getCachedValue(tipo);
                if (valor == null) return null;
                if (valor is double d) return d;
                if (valor is float f) return f;
                if (valor is int i) return i;

                // Tipos numéricos Java (via IKVM) não têm um cast direto e uniforme aqui — Convert cobre os
                // casos comuns (o próprio MPXJ implementa ToString() numérico nesses wrappers).
                return double.TryParse(System.Convert.ToString(valor, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static string Normalizar(string valor) =>
            valor.Trim().ToUpperInvariant();
    }
}
