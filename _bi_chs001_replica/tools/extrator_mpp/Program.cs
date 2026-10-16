// Extrator de VALIDAÇÃO (somente leitura) — prova quais campos do .mpp oficial alimentam as medidas do BI CHS001:
// Início / Início da LB / Trabalho / Trabalho LB / Trabalho Real / pai + nível (GeraWBS do PBIX) e a série diária de
// trabalho real e de LB por tarefa (equivalente a AssignmentByDay[TimeByDay, Trabalho Real]).
// Uso: dotnet run -- <arquivo.mpp> <saida.json>. Não grava em banco, não altera o .mpp.
using System.Text.Json;
using net.sf.mpxj;
using net.sf.mpxj.reader;
using net.sf.mpxj.mpp;
using MpxjTask = net.sf.mpxj.Task;

static DateTime? Data(object v) => v switch
{
    java.time.LocalDateTime l => new DateTime(l.getYear(), l.getMonthValue(), l.getDayOfMonth(), l.getHour(), l.getMinute(), l.getSecond()),
    java.util.Date d => DateTimeOffset.FromUnixTimeMilliseconds(d.getTime()).DateTime,
    _ => null,
};

if (args.Length < 2) { Console.Error.WriteLine("uso: <arquivo.mpp> <saida.json>"); return 2; }
var projeto = new UniversalProjectReader().read(args[0]);
var props = projeto.getProjectProperties();
double? Horas(Duration d) => d == null ? null : d.convertUnits(TimeUnit.HOURS, props).getDuration();

var tarefas = new List<Dictionary<string, object>>();
var diario = new SortedDictionary<(int uid, DateTime dia), double[]>(); var guidPorUid = new Dictionary<int, string>(); // [real, LB, planejado]
int avisos = 0;

var it = projeto.getTasks().iterator();
while (it.hasNext())
{
    var t = (MpxjTask)it.next();
    if (t.getUniqueID() == null) continue;
    int uid = t.getUniqueID().intValue(); guidPorUid[uid] = t.getGUID()?.toString()?.ToUpperInvariant();
    tarefas.Add(new()
    {
        ["UID"] = uid, ["GUID"] = t.getGUID()?.toString()?.ToUpperInvariant(), ["ID"] = t.getID()?.intValue(), ["Pai"] = t.getParentTask()?.getUniqueID()?.intValue(),
        ["Nivel"] = t.getOutlineLevel()?.intValue(), ["Resumo"] = t.getSummary(), ["Nome"] = t.getName(), ["EDT"] = t.getWBS(),
        ["Inicio"] = Data(t.getStart()), ["Termino"] = Data(t.getFinish()),
        ["InicioLB"] = Data(t.getBaselineStart()), ["TerminoLB"] = Data(t.getBaselineFinish()),
        ["Trabalho"] = Horas(t.getWork()), ["TrabalhoLB"] = Horas(t.getBaselineWork()), ["TrabalhoReal"] = Horas(t.getActualWork()),
    });

    var ra = t.getResourceAssignments().iterator();
    while (ra.hasNext())
    {
        var a = (ResourceAssignment)ra.next();
        var cal = a.getEffectiveCalendar();
        void Acumular(java.util.List lista, int col)
        {
            if (lista == null || lista.isEmpty()) return;
            var li = lista.iterator();
            while (li.hasNext())
            {
                var tw = (TimephasedWork)li.next();
                DateTime ini = Data(tw.getStart()).Value, fim = Data(tw.getFinish()).Value;
                double total = Horas((Duration)tw.getTotalAmount()) ?? 0;
                if (total == 0) continue;
                // Distribui o bloco pelos dias de trabalho do calendário efetivo (como o Project grava a série diária).
                var dias = new List<(DateTime dia, double h)>();
                for (var d = ini.Date; d <= fim; d = d.AddDays(1))
                {
                    var s = d < ini ? ini : d; var f = d.AddDays(1) > fim ? fim : d.AddDays(1);
                    if (f <= s) continue;
                    var jS = java.time.LocalDateTime.of(s.Year, s.Month, s.Day, s.Hour, s.Minute, s.Second);
                    var jF = java.time.LocalDateTime.of(f.Year, f.Month, f.Day, f.Hour, f.Minute, f.Second);
                    double h = cal.getWork(jS, jF, TimeUnit.HOURS).getDuration();
                    if (h > 0) dias.Add((d, h));
                }
                double soma = dias.Sum(x => x.h);
                if (soma <= 0) { avisos++; dias = new() { (ini.Date, 1) }; soma = 1; }
                foreach (var (dia, h) in dias)
                {
                    if (!diario.TryGetValue((uid, dia), out var v)) diario[(uid, dia)] = v = new double[3];
                    v[col] += total * h / soma;
                }
            }
        }
        Acumular(a.getTimephasedActualWork(), 0);
        Acumular(a.getTimephasedBaselineWork(0), 1);
        Acumular(a.getTimephasedWork(), 2);
    }
}

var saida = new
{
    arquivo = Path.GetFileName(args[0]), lidoEm = DateTime.Now, dataStatus = Data(props.getStatusDate()), dataAtual = Data(props.getCurrentDate()),
    tarefas,
    diario = diario.Select(kv => new { UID = kv.Key.uid, GUID = guidPorUid.GetValueOrDefault(kv.Key.uid), Dia = kv.Key.dia.ToString("yyyy-MM-dd"), Real = Math.Round(kv.Value[0], 6), LB = Math.Round(kv.Value[1], 6), Planejado = Math.Round(kv.Value[2], 6) }),
    avisosBlocosSemDiaUtil = avisos,
};
File.WriteAllText(args[1], JsonSerializer.Serialize(saida, new JsonSerializerOptions { WriteIndented = false }));
Console.WriteLine($"tarefas={tarefas.Count} folhas={tarefas.Count(x => !(bool)x["Resumo"])} linhasDiarias={diario.Count} avisos={avisos}");
return 0;
