using System.Text.Json;
using System.Text.RegularExpressions;
using net.sf.mpxj;
using MpxjTask = net.sf.mpxj.Task;

namespace APS.WebApp.Services.Planning;

public sealed record Tipo1AnalyticsMetadata(int Version, int? TaskId, string? ParentUid,
    bool IsLeaf, bool IsProjectSummary, string DisciplineResolution, bool NativeProjectSummary = false);
public sealed record Tipo1DisciplineResolution(FieldType? Field, string Diagnostic);

/// <summary>Extracts native MPP facts. Does not compute Work or infer disciplines from documents.</summary>
public static class CronogramaTipo1Analytics
{
    public const string MetadataKey = "__NA_CONNECT_ANALYTICS_V1";
    public static string Uid(MpxjTask task) => task.getGUID()?.ToString() ?? task.getUniqueID()?.ToString() ?? "";
    private static string Normalize(string? value) => Regex.Replace(value ?? "", @"\s+", " ").Trim().ToUpperInvariant();
    public static Tipo1DisciplineResolution ResolveDiscipline(ProjectFile project)
    {
        bool Matches(string? s) => Normalize(s) is "DISCIPLINA" or "DISCIPLINE";
        var aliases = new List<FieldType>();
        var fields = project.getCustomFields().iterator();
        while (fields.hasNext())
        {
            var field = (CustomField)fields.next();
            if (Matches(field.getAlias()) && field.getFieldType() is TaskField) aliases.Add(field.getFieldType());
        }
        var distinct = aliases.Distinct().ToArray();
        if (distinct.Length > 0) return distinct.Length == 1
            ? new(distinct[0], $"ALIAS:{distinct[0]}") : new(null, "DISCIPLINA_ALIAS_AMBIGUO");
        var titles = new List<FieldType>();
        var tables = project.getTables().iterator();
        while (tables.hasNext())
        {
            var table = (Table)tables.next();
            if (table.getResourceFlag()) continue;
            var columns = table.getColumns().iterator();
            while (columns.hasNext())
            {
                var column = (Column)columns.next();
                if (Matches(column.getTitle()) && column.getFieldType() is TaskField) titles.Add(column.getFieldType());
            }
        }
        distinct = titles.Distinct().ToArray();
        return distinct.Length == 1 ? new(distinct[0], $"TITULO_TABELA:{distinct[0]}")
            : new(null, distinct.Length > 1 ? "DISCIPLINA_TITULO_AMBIGUO" : "DISCIPLINA_NAO_ENCONTRADA");
    }

    public static decimal? ReadWorkHours(MpxjTask task)
    {
        var work = task.getWork();
        if (work == null) return null;
        var hours = work.convertUnits(TimeUnit.HOURS, task.getParentFile().getProjectProperties()).getDuration();
        if (!double.IsFinite(hours) || hours < 0 || hours >= (double)decimal.MaxValue) return null;
        return (decimal)hours;
    }

    public static string Metadata(MpxjTask task, bool root, string resolution) => JsonSerializer.Serialize(
        new Dictionary<string, Tipo1AnalyticsMetadata> { [MetadataKey] = new(1, task.getID()?.intValue(),
            task.getParentTask() is { } parent ? Uid(parent) : null, task.getChildTasks().isEmpty() && !task.getSummary(), root, resolution,
            IsNativeProjectSummary(task)) });

    // The special Project Summary can be represented outside the ordinary parent tree by MPXJ.
    // Four native signals distinguish it from a first row or SQL identity.
    private static bool IsNativeProjectSummary(MpxjTask task) => task.getSummary() && task.getParentTask() == null
        && task.getID()?.intValue() == 0 && task.getWBS() == "0";

    public static MpxjTask? FindRoot(ProjectFile project)
    {
        var tasks = new List<MpxjTask>();
        var it = project.getTasks().iterator();
        while (it.hasNext()) if (it.next() is MpxjTask t && t.getID() != null) tasks.Add(t);
        var native = tasks.Where(IsNativeProjectSummary).ToArray();
        if (native.Length > 0) return native.Length == 1 ? native[0] : null;
        bool Descends(MpxjTask child, MpxjTask root)
        {
            var seen = new HashSet<MpxjTask>();
            for (var p = child.getParentTask(); p != null && seen.Add(p); p = p.getParentTask())
                if (ReferenceEquals(p, root)) return true;
            return false;
        }
        var roots = tasks.Where(t => t.getSummary() && t.getParentTask() == null
            && tasks.All(other => ReferenceEquals(other, t) || Descends(other, t))).ToArray();
        return roots.Length == 1 ? roots[0] : null;
    }
}
