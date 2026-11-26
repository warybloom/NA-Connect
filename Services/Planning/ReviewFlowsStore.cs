using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Planning;

public sealed class ReviewStage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Order { get; set; }
    public List<string> StatusInFlowValues { get; set; } = [];
}
public sealed class ReviewFlow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public int Version { get; set; }
    public List<ReviewStage> Stages { get; set; } = [];
}
public sealed class ReviewFlowBinding
{
    public string Id { get; set; } = "";
    public string FlowId { get; set; } = "";
    public string Scope { get; set; } = "";
    public string? ProjectId { get; set; }
    public string? GroupName { get; set; }
    public List<string> ProjectIds { get; set; } = [];
    public int Version { get; set; }
}
public sealed class ReviewFlowConflictException(string message) : Exception(message);

public static class ReviewFlowRules
{
    public static string Key(string? value)
    {
        var decomp = (value ?? "").Normalize(NormalizationForm.FormD);
        var b = new StringBuilder();
        var space = false;
        foreach (var c in decomp)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c)) { if (b.Length > 0) space = true; continue; }
            if (space) { b.Append(' '); space = false; }
            b.Append(char.ToUpperInvariant(c));
        }
        return b.ToString().Trim();
    }

    public static string? Validate(ReviewFlow flow)
    {
        if (string.IsNullOrWhiteSpace(flow.Name)) return "Informe o nome do fluxo.";
        if (flow.Stages is not { Count: > 0 }) return "O fluxo precisa de ao menos uma etapa.";
        var names = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < flow.Stages.Count; i++)
        {
            var stage = flow.Stages[i];
            if (string.IsNullOrWhiteSpace(stage.Name)) return "Toda etapa precisa de nome oficial em português.";
            if (stage.Order != i + 1) return "A ordem das etapas deve ser sequencial, de 1 até n.";
            if (!names.Add(Key(stage.Name))) return "Há etapas com o mesmo nome oficial.";
            if (!string.IsNullOrWhiteSpace(stage.Id) && !ids.Add(stage.Id)) return "Identificador de etapa duplicado.";
            if (stage.StatusInFlowValues is null) return "A lista de aliases é obrigatória.";
            foreach (var alias in stage.StatusInFlowValues)
            {
                var key = Key(alias);
                if (key.Length == 0) return "Alias vazio não é permitido.";
                if (!aliases.Add(key)) return "Um alias de Status_In_Flow aparece mais de uma vez no fluxo.";
            }
        }
        return null;
    }

    public static ReviewStage? Resolve(ReviewFlow? flow, string? status) => flow?.Active == true && Key(status).Length > 0
        ? flow.Stages.FirstOrDefault(s => s.StatusInFlowValues.Any(a => Key(a) == Key(status))) : null;
}

public sealed class ReviewFlowsStore(IConfiguration config)
{
    private readonly string cs = config.GetConnectionString("LocalConnection") ?? throw new InvalidOperationException("LocalConnection ausente.");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private SqlConnection Open() { var c = new SqlConnection(cs); c.Open(); return c; }

    public (List<ReviewFlow> Flows, List<ReviewFlowBinding> Bindings) Read()
    {
        using var c = Open();
        var flows = new List<ReviewFlow>();
        using (var cmd = new SqlCommand("SELECT Id,Name,Active,Version,StagesJson FROM planning.ReviewFlow", c))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) flows.Add(new ReviewFlow { Id = r.GetGuid(0).ToString(), Name = r.GetString(1), Active = r.GetBoolean(2), Version = r.GetInt32(3),
                Stages = JsonSerializer.Deserialize<List<ReviewStage>>(r.GetString(4), Json) ?? [] });
        var bindings = new List<ReviewFlowBinding>();
        using (var cmd = new SqlCommand("SELECT Id,FlowId,Scope,ProjectId,GroupName,ProjectIdsJson,Version FROM planning.ReviewFlowBinding", c))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) bindings.Add(new ReviewFlowBinding { Id = r.GetGuid(0).ToString(), FlowId = r.GetGuid(1).ToString(), Scope = r.GetString(2),
                ProjectId = r.IsDBNull(3) ? null : r.GetString(3), GroupName = r.IsDBNull(4) ? null : r.GetString(4),
                ProjectIds = JsonSerializer.Deserialize<List<string>>(r.GetString(5), Json) ?? [], Version = r.GetInt32(6) });
        return (flows, bindings);
    }

    public ReviewFlow Save(ReviewFlow flow, string? id)
    {
        var error = ReviewFlowRules.Validate(flow);
        if (error is not null) throw new ArgumentException(error);
        flow.Name = flow.Name.Trim();
        foreach (var stage in flow.Stages)
        {
            stage.Name = stage.Name.Trim();
            stage.StatusInFlowValues = stage.StatusInFlowValues.Select(a => a.Trim()).ToList();
        }
        using var c = Open();
        using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        if (id is null) { flow.Id = Guid.NewGuid().ToString(); flow.Version = 1; }
        else
        {
            if (!Guid.TryParse(id, out _)) throw new ArgumentException("ID inválido.");
            using var check = new SqlCommand("SELECT Version,StagesJson FROM planning.ReviewFlow WITH (UPDLOCK) WHERE Id=@id", c, tx);
            check.Parameters.AddWithValue("@id", Guid.Parse(id));
            using var r = check.ExecuteReader();
            if (!r.Read()) throw new KeyNotFoundException("Fluxo não encontrado.");
            var oldVersion = r.GetInt32(0);
            var oldStages = JsonSerializer.Deserialize<List<ReviewStage>>(r.GetString(1), Json) ?? [];
            r.Close();
            if (oldVersion != flow.Version) throw new ReviewFlowConflictException("Fluxo alterado por outro usuário. Atualize a tela.");
            var oldIds = oldStages.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            if (flow.Stages.Any(s => s.Id.Length > 0 && !oldIds.Contains(s.Id))) throw new ArgumentException("Etapa não pertence a este fluxo.");
            flow.Id = id; flow.Version++;
        }
        foreach (var stage in flow.Stages) if (string.IsNullOrWhiteSpace(stage.Id)) stage.Id = Guid.NewGuid().ToString();
        using var cmd = new SqlCommand(id is null
            ? "INSERT INTO planning.ReviewFlow(Id,Name,Active,Version,StagesJson) VALUES(@id,@name,@active,@version,@stages)"
            : "UPDATE planning.ReviewFlow SET Name=@name,Active=@active,Version=@version,StagesJson=@stages WHERE Id=@id", c, tx);
        cmd.Parameters.AddWithValue("@id", Guid.Parse(flow.Id));
        cmd.Parameters.AddWithValue("@name", flow.Name.Trim());
        cmd.Parameters.AddWithValue("@active", flow.Active);
        cmd.Parameters.AddWithValue("@version", flow.Version);
        cmd.Parameters.AddWithValue("@stages", JsonSerializer.Serialize(flow.Stages, Json));
        cmd.ExecuteNonQuery(); tx.Commit(); return flow;
    }

    public ReviewFlowBinding Save(ReviewFlowBinding binding, string? id)
    {
        if (!Guid.TryParse(binding.FlowId, out var flowId)) throw new ArgumentException("Fluxo inválido.");
        if (binding.Scope is not ("project" or "group")) throw new ArgumentException("Escopo inválido.");
        if (binding.Scope == "project" && string.IsNullOrWhiteSpace(binding.ProjectId)) throw new ArgumentException("Projeto obrigatório.");
        if (binding.Scope == "group" && (string.IsNullOrWhiteSpace(binding.GroupName) || binding.ProjectIds.Count == 0))
            throw new ArgumentException("Grupo precisa de nome e projetos.");
        binding.ProjectIds = binding.Scope == "group" ? binding.ProjectIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : [];
        binding.ProjectId = binding.Scope == "project" ? binding.ProjectId?.Trim() : null;
        binding.GroupName = binding.Scope == "group" ? binding.GroupName?.Trim() : null;
        using var c = Open(); using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        using (var exists = new SqlCommand("SELECT COUNT(*) FROM planning.ReviewFlow WHERE Id=@id", c, tx))
        { exists.Parameters.AddWithValue("@id", flowId); if ((int)exists.ExecuteScalar()! == 0) throw new ArgumentException("Fluxo não encontrado."); }
        var others = new List<ReviewFlowBinding>();
        using (var cmd = new SqlCommand("SELECT Id,FlowId,Scope,ProjectId,GroupName,ProjectIdsJson,Version FROM planning.ReviewFlowBinding WITH (UPDLOCK,HOLDLOCK)", c, tx))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) others.Add(new ReviewFlowBinding { Id = r.GetGuid(0).ToString(), FlowId = r.GetGuid(1).ToString(), Scope = r.GetString(2),
                ProjectId = r.IsDBNull(3) ? null : r.GetString(3), GroupName = r.IsDBNull(4) ? null : r.GetString(4),
                ProjectIds = JsonSerializer.Deserialize<List<string>>(r.GetString(5), Json) ?? [], Version = r.GetInt32(6) });
        var current = id is null ? null : others.FirstOrDefault(b => b.Id == id);
        if (id is not null && current is null) throw new KeyNotFoundException("Vínculo não encontrado.");
        if (current is not null && current.Version != binding.Version) throw new ReviewFlowConflictException("Vínculo alterado por outro usuário. Atualize a tela.");
        if (current is not null && current.Scope != binding.Scope) throw new ArgumentException("O escopo do vínculo não pode mudar.");
        foreach (var other in others.Where(b => b.Id != id))
        {
            if (binding.Scope == "project" && other.Scope == "project" && other.ProjectId == binding.ProjectId)
                throw new ArgumentException("Projeto já tem vínculo direto.");
            if (binding.Scope == "group" && other.Scope == "group")
            {
                if (ReviewFlowRules.Key(other.GroupName) == ReviewFlowRules.Key(binding.GroupName)) throw new ArgumentException("Nome de grupo duplicado.");
                if (binding.ProjectIds.Intersect(other.ProjectIds, StringComparer.OrdinalIgnoreCase).Any()) throw new ArgumentException("Projeto já pertence a outro grupo.");
            }
        }
        binding.Id = id ?? Guid.NewGuid().ToString(); binding.Version = (current?.Version ?? 0) + 1;
        using var save = new SqlCommand(id is null
            ? "INSERT INTO planning.ReviewFlowBinding(Id,FlowId,Scope,ProjectId,GroupName,ProjectIdsJson,Version) VALUES(@id,@flow,@scope,@project,@group,@projects,@version)"
            : "UPDATE planning.ReviewFlowBinding SET FlowId=@flow,ProjectId=@project,GroupName=@group,ProjectIdsJson=@projects,Version=@version WHERE Id=@id", c, tx);
        save.Parameters.AddWithValue("@id", Guid.Parse(binding.Id)); save.Parameters.AddWithValue("@flow", flowId);
        save.Parameters.AddWithValue("@scope", binding.Scope);
        save.Parameters.AddWithValue("@project", (object?)binding.ProjectId ?? DBNull.Value);
        save.Parameters.AddWithValue("@group", (object?)binding.GroupName ?? DBNull.Value);
        save.Parameters.AddWithValue("@projects", JsonSerializer.Serialize(binding.ProjectIds, Json));
        save.Parameters.AddWithValue("@version", binding.Version);
        save.ExecuteNonQuery(); tx.Commit(); return binding;
    }

    public List<(string Status, long Count)> Statuses(string projectId)
    {
        using var c = Open();
        var table = PmoLdpSnapshot.Tabela;
        var documents = LdpItemRules.SqlIsDocument("LdpAtual");
        using var cmd = new SqlCommand($"SELECT Status_In_Flow, COUNT_BIG(DISTINCT COALESCE(NULLIF(LTRIM(RTRIM(NA_Document_Number)), ''), NULLIF(LTRIM(RTRIM(Client_Document_Number)), ''), CONVERT(nvarchar(100), ID_Registro))) FROM {table} WHERE {documents} AND NULLIF(LTRIM(RTRIM(Status_In_Flow)), '') IS NOT NULL GROUP BY Status_In_Flow {PmoLdpSnapshot.OpcaoPlano}", c);
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        var result = new List<(string, long)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add((r.GetString(0), r.GetInt64(1)));
        return result;
    }
}
