using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using net.sf.mpxj;
using MpxjTask = net.sf.mpxj.Task;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>Work planejado, em horas MPXJ. Não lê WBS nem percentuais históricos.</summary>
    public static class CronogramaTipo2Work
    {
        public const string ChaveSnapshot = "__NA_CONNECT_WORK_V1";
        public sealed class Snapshot
        {
            public decimal? ItemHoras { get; set; }
            public decimal? TotalHoras { get; set; }
            public string FonteTotal { get; set; }
        }

        public static decimal? CalcularPercentual(decimal? item, decimal? total)
            => item.HasValue && total > 0 && item >= 0 && item <= total
                ? item.Value / total.Value * 100m : null;

        public static decimal? LerHoras(MpxjTask task)
        {
            var work = task?.getWork();
            if (work == null) return null;
            double horas = work.convertUnits(TimeUnit.HOURS, task.getParentFile().getProjectProperties()).getDuration();
            return double.IsFinite(horas) && horas >= 0 && horas <= (double)decimal.MaxValue
                ? (decimal)horas : null;
        }

        public static decimal? ObterTotal(ProjectFile project, out string fonte)
        {
            var tasks = new List<MpxjTask>();
            var it = project.getTasks().iterator();
            while (it.hasNext()) if (it.next() is MpxjTask task) tasks.Add(task);
            var folhas = tasks.Where(t => !t.getSummary() && t.getChildTasks().isEmpty()).ToList();
            decimal soma = folhas.Sum(t => LerHoras(t) ?? 0m);
            // Não basta ID=0: deve ser resumo raiz, conter TODAS as outras tarefas e
            // seu Work deve concordar com todas as folhas (sem Work ausente) em horas.
            var resumos = tasks.Where(t => t.getID()?.intValue() == 0 && t.getSummary() && t.getParentTask() == null).ToList();
            var resumo = resumos.Count == 1 ? resumos[0] : null;
            bool DescendeDeResumo(MpxjTask task)
            {
                var visitados = new HashSet<MpxjTask>();
                for (var pai = task.getParentTask(); pai != null && visitados.Add(pai); pai = pai.getParentTask())
                    if (ReferenceEquals(pai, resumo)) return true;
                return false;
            }
            var workResumo = LerHoras(resumo);
            if (resumo != null && workResumo > 0 && folhas.Count > 0 && folhas.All(t => LerHoras(t).HasValue)
                && tasks.Where(t => !ReferenceEquals(t, resumo)).All(DescendeDeResumo)
                && Math.Abs(workResumo.Value - soma) <= 0.0001m)
            {
                fonte = "Project Summary Task ID=0; hierarquia completa e Work reconciliado com folhas";
                return workResumo;
            }
            fonte = "Fallback: soma de Work válido das folhas, excluindo summary tasks";
            return soma > 0 ? soma : null;
        }

        public static decimal? LerPercentualSnapshot(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty(ChaveSnapshot, out var value) || value.ValueKind != JsonValueKind.String) return null;
                var work = JsonSerializer.Deserialize<Snapshot>(value.GetString());
                return work == null ? null : CalcularPercentual(work.ItemHoras, work.TotalHoras);
            }
            catch (JsonException) { return null; }
        }
    }
}
