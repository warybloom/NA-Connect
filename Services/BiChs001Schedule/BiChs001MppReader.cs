using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using APS.WebApp.Services.CronogramaTipo2;
using net.sf.mpxj;
using net.sf.mpxj.reader;
using MpxjTask = net.sf.mpxj.Task;

namespace APS.WebApp.Services.BiChs001Schedule;

/// <summary>
/// Leitor PURO (sem SQL, sem ACC, sem escrita) de uma CÓPIA do .mpp para o BI CHS001. Usa o mesmo MPXJ.Net do importador
/// Tipo 2, mas é independente dele: o importador continua como está. Extrai os campos nativos que o PBIX consome
/// (Task / WBX / AssignmentByDay): UID, pai, nível, Início/Término atuais e da LB, Trabalho, Trabalho Real, Trabalho da LB e a
/// série timephased das atribuições (real, LB 0 e planejado), distribuída por DIA com o calendário efetivo da atribuição.
/// Nada é estimado: campo ausente fica nulo e é contado no diagnóstico.
/// </summary>
public static class BiChs001MppReader
{
    private const decimal Tolerancia = 0.01m;

    public static BiLeituraCronograma Ler(string caminhoMpp, string? nomeArquivo = null)
    {
        if (!File.Exists(caminhoMpp)) throw new FileNotFoundException("Cópia do .mpp não encontrada.", caminhoMpp);
        var leitura = new BiLeituraCronograma
        {
            ArquivoNome = nomeArquivo ?? Path.GetFileName(caminhoMpp),
            ArquivoBytes = new FileInfo(caminhoMpp).Length,
            ArquivoSha256 = Sha256(caminhoMpp),
            LidoEmUtc = DateTime.UtcNow,
        };

        ProjectFile projeto = new UniversalProjectReader().read(caminhoMpp)
            ?? throw new InvalidDataException("MPXJ não reconheceu o arquivo como cronograma.");
        var props = projeto.getProjectProperties();
        leitura.DataStatus = Data(props.getStatusDate());
        leitura.DataAtualProjeto = Data(props.getCurrentDate());
        leitura.MinutosPorDia = props.getMinutesPerDay() is java.lang.Number mpd ? (decimal)mpd.doubleValue() : null;
        var calPadrao = projeto.getDefaultCalendar();
        leitura.CalendarioPadrao = calPadrao?.getName();

        var diag = leitura.Diagnostico;
        var calIt = projeto.getCalendars().iterator();
        while (calIt.hasNext()) if (calIt.next() is ProjectCalendar c && c.getName() is string n) diag.Calendarios.Add(n);

        decimal? Horas(Duration? d)
        {
            if (d == null) return null;
            double h = d.convertUnits(TimeUnit.HOURS, props).getDuration();
            return double.IsFinite(h) ? Math.Round((decimal)h, 6) : null;
        }

        var resolver = new CronogramaTipo2FieldResolver(projeto);
        var diario = new SortedDictionary<(int Uid, DateTime Dia), decimal[]>();
        var serieReal = new Dictionary<int, decimal>();
        var serieLb = new Dictionary<int, decimal>();
        int ordem = 0;

        var it = projeto.getTasks().iterator();
        while (it.hasNext())
        {
            if (it.next() is not MpxjTask t || t.getUniqueID() == null) continue;
            int uid = t.getUniqueID().intValue();
            bool resumo = t.getSummary() || !t.getChildTasks().isEmpty();
            var tarefa = new BiTarefa
            {
                Ordem = ++ordem,
                UidTarefa = uid,
                GuidTarefa = t.getGUID()?.toString()?.ToUpperInvariant(),
                IdTarefa = t.getID()?.intValue(),
                UidTarefaPai = t.getParentTask()?.getUniqueID()?.intValue(),
                NivelEstrutura = t.getOutlineLevel()?.intValue(),
                EhResumo = resumo,
                EhMarco = t.getMilestone(),
                Nome = t.getName() ?? "",
                Edt = t.getWBS(),
                Inicio = Data(t.getStart()),
                Termino = Data(t.getFinish()),
                InicioLinhaBase = Data(t.getBaselineStart()),
                TerminoLinhaBase = Data(t.getBaselineFinish()),
                TrabalhoHoras = Horas(t.getWork()),
                TrabalhoRealHoras = Horas(t.getActualWork()),
                TrabalhoLinhaBaseHoras = Horas(t.getBaselineWork()),
                TrabalhoRestanteHoras = Horas(t.getRemainingWork()),
                PercentualConcluido = t.getPercentageComplete() is java.lang.Number pc ? Math.Round((decimal)pc.doubleValue(), 6) : null,
                Calendario = t.getEffectiveCalendar()?.getName(),
                CamposCustomizadosJson = CamposCustomizados(resolver, t),
            };
            if (tarefa.UidTarefaPai == null && tarefa.NivelEstrutura is > 0) diag.TarefasSemPaiForaDaRaiz++;

            var ra = t.getResourceAssignments().iterator();
            while (ra.hasNext())
            {
                if (ra.next() is not ResourceAssignment a) continue;
                tarefa.QtdeAtribuicoes++;
                diag.TotalAtribuicoes++;
                var cal = a.getEffectiveCalendar() ?? t.getEffectiveCalendar() ?? calPadrao;
                Acumular(a.getTimephasedActualWork(), 0, uid, cal, diario, Horas, diag);
                Acumular(a.getTimephasedBaselineWork(0), 1, uid, cal, diario, Horas, diag);
                Acumular(a.getTimephasedWork(), 2, uid, cal, diario, Horas, diag);
            }
            leitura.Tarefas.Add(tarefa);
        }

        foreach (var ((uid, dia), v) in diario)
        {
            leitura.TrabalhoDiario.Add(new BiTrabalhoDia
            {
                UidTarefa = uid, Data = dia,
                TrabalhoRealHoras = Math.Round(v[0], 6), TrabalhoLinhaBaseHoras = Math.Round(v[1], 6), TrabalhoPlanejadoHoras = Math.Round(v[2], 6),
            });
            serieReal[uid] = serieReal.GetValueOrDefault(uid) + v[0];
            serieLb[uid] = serieLb.GetValueOrDefault(uid) + v[1];
        }

        // Diagnóstico (fatos, nunca correção): folhas sem campo, série × total da tarefa.
        diag.TotalTarefas = leitura.Tarefas.Count;
        var folhas = leitura.Tarefas.Where(x => !x.EhResumo).ToList();
        diag.TotalFolhas = folhas.Count;
        diag.TotalResumo = diag.TotalTarefas - diag.TotalFolhas;
        diag.FolhasSemInicio = folhas.Count(x => x.Inicio == null);
        diag.FolhasSemInicioLinhaBase = folhas.Count(x => x.InicioLinhaBase == null);
        diag.FolhasSemTrabalho = folhas.Count(x => x.TrabalhoHoras == null);
        diag.FolhasSemTrabalhoLinhaBase = folhas.Count(x => x.TrabalhoLinhaBaseHoras == null);
        foreach (var f in folhas)
        {
            decimal real = f.TrabalhoRealHoras ?? 0, lb = f.TrabalhoLinhaBaseHoras ?? 0;
            bool temSerieReal = serieReal.TryGetValue(f.UidTarefa, out var sr) && sr != 0;
            if (real > 0 && !temSerieReal) diag.FolhasComTrabalhoRealSemSerie++;
            if (temSerieReal && Math.Abs(sr - real) > Tolerancia) diag.FolhasComSerieDivergenteDoTotalReal++;
            if (serieLb.TryGetValue(f.UidTarefa, out var sl) && sl != 0 && Math.Abs(sl - lb) > Tolerancia) diag.FolhasComSerieDivergenteDoTotalLinhaBase++;
        }
        diag.SomaTrabalhoFolhas = folhas.Sum(x => x.TrabalhoHoras ?? 0);
        diag.SomaTrabalhoRealFolhas = folhas.Sum(x => x.TrabalhoRealHoras ?? 0);
        diag.SomaTrabalhoLinhaBaseFolhas = folhas.Sum(x => x.TrabalhoLinhaBaseHoras ?? 0);
        diag.SomaSerieReal = leitura.TrabalhoDiario.Sum(x => x.TrabalhoRealHoras ?? 0);
        diag.SomaSerieLinhaBase = leitura.TrabalhoDiario.Sum(x => x.TrabalhoLinhaBaseHoras ?? 0);
        diag.SomaSeriePlanejado = leitura.TrabalhoDiario.Sum(x => x.TrabalhoPlanejadoHoras ?? 0);
        diag.PrimeiraDataSerie = leitura.TrabalhoDiario.Count > 0 ? leitura.TrabalhoDiario.Min(x => x.Data) : null;
        diag.UltimaDataSerie = leitura.TrabalhoDiario.Count > 0 ? leitura.TrabalhoDiario.Max(x => x.Data) : null;
        if (diag.TotalAtribuicoes == 0) diag.Avisos.Add("O .mpp não tem atribuições de recurso: não há série timephased (AssignmentByDay) a extrair.");
        if (diag.FolhasComTrabalhoRealSemSerie > 0)
            diag.Avisos.Add($"{diag.FolhasComTrabalhoRealSemSerie} folha(s) com Trabalho Real > 0 sem série diária (trabalho real lançado sem atribuição/timephased).");
        if (diag.FolhasComSerieDivergenteDoTotalReal > 0)
            diag.Avisos.Add($"{diag.FolhasComSerieDivergenteDoTotalReal} folha(s) com série de trabalho real diferente do Trabalho Real da tarefa (> {Tolerancia} h); série {diag.SomaSerieReal:0.##} h × tarefas {diag.SomaTrabalhoRealFolhas:0.##} h.");
        if (diag.FolhasComSerieDivergenteDoTotalLinhaBase > 0)
            diag.Avisos.Add($"{diag.FolhasComSerieDivergenteDoTotalLinhaBase} folha(s) com série timephased de LINHA DE BASE diferente do Trabalho LB da tarefa; série {diag.SomaSerieLinhaBase:0.##} h × tarefas {diag.SomaTrabalhoLinhaBaseFolhas:0.##} h. "
                + "As medidas do PBIX não usam essa série (usam WBX[Trab Linha de Base] e só a série de Trabalho Real); gravada apenas para auditoria.");
        if (diag.BlocosSemHoraUtil > 0)
            diag.Avisos.Add($"{diag.BlocosSemHoraUtil} bloco(s) timephased sem hora útil no calendário efetivo: horas lançadas no dia de início do bloco.");
        return leitura;
    }

    /// <summary>
    /// Soma um bloco timephased (MPXJ agrupa dias consecutivos com a mesma taxa) nos DIAS que o compõem, na proporção das
    /// horas úteis do calendário efetivo em cada dia — é assim que o Project gera a visão por dia (AssignmentByDay).
    /// </summary>
    private static void Acumular(java.util.List? lista, int coluna, int uid, ProjectCalendar? cal,
        SortedDictionary<(int, DateTime), decimal[]> diario, Func<Duration?, decimal?> horas, BiDiagnosticoLeitura diag)
    {
        if (lista == null || lista.isEmpty()) return;
        var li = lista.iterator();
        while (li.hasNext())
        {
            if (li.next() is not TimephasedWork tw) continue;
            DateTime? ini = Data(tw.getStart()), fim = Data(tw.getFinish());
            decimal total = horas(tw.getTotalAmount() as Duration) ?? 0;
            if (ini == null || fim == null || total == 0) continue;
            var dias = new List<(DateTime Dia, decimal H)>();
            if (cal != null)
            {
                for (var d = ini.Value.Date; d <= fim.Value; d = d.AddDays(1))
                {
                    var s = d < ini.Value ? ini.Value : d;
                    var f = d.AddDays(1) > fim.Value ? fim.Value : d.AddDays(1);
                    if (f <= s) continue;
                    var dur = cal.getWork(Java(s), Java(f), TimeUnit.HOURS);
                    double h = dur?.getDuration() ?? 0;
                    if (h > 0 && double.IsFinite(h)) dias.Add((d, (decimal)h));
                }
            }
            decimal soma = dias.Sum(x => x.H);
            if (soma <= 0) { diag.BlocosSemHoraUtil++; dias = new() { (ini.Value.Date, 1m) }; soma = 1m; }
            foreach (var (dia, h) in dias)
            {
                if (!diario.TryGetValue((uid, dia), out var v)) diario[(uid, dia)] = v = new decimal[3];
                v[coluna] += total * h / soma;
            }
        }
    }

    private static java.time.LocalDateTime Java(DateTime d) =>
        java.time.LocalDateTime.of(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second);

    /// <summary>Data nativa do MPXJ (LocalDateTime nas versões atuais; java.util.Date em campos antigos), preservando a hora.</summary>
    public static DateTime? Data(object? v) => v switch
    {
        java.time.LocalDateTime l => new DateTime(l.getYear(), l.getMonthValue(), l.getDayOfMonth(), l.getHour(), l.getMinute(), l.getSecond()),
        java.util.Date d => DateTimeOffset.FromUnixTimeMilliseconds(d.getTime()).DateTime,
        _ => null,
    };

    /// <summary>Campos customizados com alias, texto de exibição — mesma convenção do JSON do Tipo 2 (Status1, Disciplina…).</summary>
    private static string CamposCustomizados(CronogramaTipo2FieldResolver resolver, MpxjTask task)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (alias, tipo) in resolver.AliasesOriginais)
        {
            object? bruto = task.get(tipo);
            d[alias] = bruto switch
            {
                null => "",
                java.lang.Number n => n.doubleValue().ToString("0.##", new CultureInfo("pt-BR")),
                java.util.Date dt => DateTimeOffset.FromUnixTimeMilliseconds(dt.getTime()).DateTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                _ => bruto.ToString()?.Trim() ?? "",
            };
        }
        return JsonSerializer.Serialize(d);
    }

    public static string Sha256(string caminho)
    {
        using var fs = File.OpenRead(caminho);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }
}
