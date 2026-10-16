using System.Data;
using APS.WebApp.Services;
using APS.WebApp.Services.BiChs001Schedule;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

/// <summary>
/// BI CHS001 (Original) — cronograma ISOLADO (schema bi_chs001), lido do .mpp oficial por Services/BiChs001Schedule.
/// Exclusivo do BI: não altera nem substitui /api/bi-chs001/cronograma (legado) nem o importador Tipo 2.
///   GET  api/bi-chs001/schedule/status            → versões gravadas, disponibilidade de cada fonte, alias ativo, conferência com o legado
///   GET  api/bi-chs001/schedule/tarefas           → WBX/Task da versão (mesmas colunas do /cronograma + Início, LB, trabalhos, pai, nível)
///   GET  api/bi-chs001/schedule/trabalho-diario   → AssignmentByDay por dia (soma das atribuições da tarefa)
///   GET  api/bi-chs001/schedule/trabalho-semanal  → mesma série agrupada por semana iniciada no domingo (grão do cache do PBIX)
///   POST api/bi-chs001/schedule/sincronizar       → baixa cópia do .mpp mais recente do ACC e grava nova versão (robot.run)
/// Leituras de dbo só por SELECT. Projeto restrito ao CHS001.
/// </summary>
[ApiController]
[Route("api/bi-chs001/schedule")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BiChs001ScheduleController(IProjectAccessService projectAccess, IPermissionService permissions, IConfiguration configuration,
    AutodeskApiService autodeskApi, ILogger<BiChs001ScheduleController> logger) : ControllerBase
{
    public static readonly string[] KeysLeitura = BiChs001Controller.KeysCronograma;
    public static readonly string[] KeysSincronizar = ["robot.run"];

    private string ConnectionString => configuration.GetConnectionString("LocalConnection")
        ?? throw new InvalidOperationException("Conexão de relatórios indisponível.");

    [HttpGet("status")]
    [RequirePermission("reports.gestao.view", "reports.cronograma.view")]
    public async Task<IActionResult> Status([FromQuery] string projectId, CancellationToken ct)
    {
        var bloqueio = await Validar(projectId, KeysLeitura, ct);
        if (bloqueio is not null) return bloqueio;
        try
        {
            var repo = new BiChs001ScheduleRepository(ConnectionString);
            if (!await repo.SchemaExisteAsync(ct))
                return Ok(new { schemaInstalado = false, versaoAtual = (object?)null, versoes = Array.Empty<object>(), alias = Array.Empty<object>(),
                    indisponivel = "Schema bi_chs001 não instalado." });
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            var versoes = await Linhas(cn, BiChs001ScheduleRepository.VersoesSql, projectId, null, ct);
            var alias = await Linhas(cn, BiChs001ScheduleRepository.AliasSql, projectId, null, ct);
            int? atual = await repo.ResolverVersaoAsync(cn, projectId, null, ct);
            var legado = atual is null ? null : (await Linhas(cn, BiChs001ScheduleRepository.ComparacaoLegadoSql, projectId, atual, ct)).FirstOrDefault();
            return Ok(new
            {
                schemaInstalado = true,
                versaoAtual = versoes.FirstOrDefault(),
                versoes,
                alias,
                comparacaoLegado = legado,
                indisponivel = atual is null ? "Nenhuma versão do cronograma gravada no BI ainda (POST /api/bi-chs001/schedule/sincronizar)." : null,
            });
        }
        catch (SqlException ex) { return Falha("status", ex); }
    }

    [HttpGet("tarefas")]
    [RequirePermission("reports.gestao.view", "reports.cronograma.view")]
    public Task<IActionResult> Tarefas([FromQuery] string projectId, [FromQuery] int? versao, CancellationToken ct) =>
        LerVersao(projectId, versao, BiChs001ScheduleRepository.TarefasSql, "schedule-tarefas", ct);

    [HttpGet("trabalho-diario")]
    [RequirePermission("reports.gestao.view", "reports.cronograma.view")]
    public Task<IActionResult> TrabalhoDiario([FromQuery] string projectId, [FromQuery] int? versao, CancellationToken ct) =>
        LerVersao(projectId, versao, BiChs001ScheduleRepository.TrabalhoDiarioSql, "schedule-trabalho-diario", ct);

    [HttpGet("trabalho-semanal")]
    [RequirePermission("reports.gestao.view", "reports.cronograma.view")]
    public Task<IActionResult> TrabalhoSemanal([FromQuery] string projectId, [FromQuery] int? versao, CancellationToken ct) =>
        LerVersao(projectId, versao, BiChs001ScheduleRepository.TrabalhoSemanalSql, "schedule-trabalho-semanal", ct);

    [HttpPost("sincronizar")]
    [RequirePermission("robot.run")]
    public async Task<IActionResult> Sincronizar([FromQuery] string projectId, CancellationToken ct)
    {
        var bloqueio = await Validar(projectId, KeysSincronizar, ct);
        if (bloqueio is not null) return bloqueio;
        var servico = new BiChs001ScheduleSyncService(autodeskApi, new BiChs001ScheduleRepository(ConnectionString), logger);
        var r = await servico.SincronizarDoAccAsync(projectId, User?.Identity?.Name ?? "api", gravar: true, ct);
        return r.Sucesso ? Ok(r) : StatusCode(502, r);
    }

    private async Task<IActionResult> LerVersao(string projectId, int? versao, string sql, string fonte, CancellationToken ct)
    {
        var bloqueio = await Validar(projectId, KeysLeitura, ct);
        if (bloqueio is not null) return bloqueio;
        try
        {
            var repo = new BiChs001ScheduleRepository(ConnectionString);
            if (!await repo.SchemaExisteAsync(ct))
                return Ok(new { fonte, totalLinhas = 0, linhas = Array.Empty<object>(), indisponivel = "Schema bi_chs001 não instalado." });
            await using var cn = new SqlConnection(ConnectionString);
            await cn.OpenAsync(ct);
            int? v = await repo.ResolverVersaoAsync(cn, projectId, versao, ct);
            if (v is null)
                return versao is null
                    ? Ok(new { fonte, totalLinhas = 0, linhas = Array.Empty<object>(), indisponivel = "Nenhuma versão do cronograma gravada no BI ainda." })
                    : NotFound(new { message = "Versão inexistente para o projeto." });
            var linhas = await Linhas(cn, sql, projectId, v, ct);
            return Ok(new { fonte, versaoId = v, totalLinhas = linhas.Count, linhas });
        }
        catch (SqlException ex) { return Falha(fonte, ex); }
    }

    private async Task<IActionResult?> Validar(string projectId, string[] keys, CancellationToken ct)
    {
        if (!IsAccId(projectId)) return BadRequest(new { message = "Filtros inválidos." });
        var auth = await SqlReportAuthorization.CheckAsync(this, projectAccess, permissions, projectId, keys, ct);
        if (auth is not null) return auth;
        return BiChs001Escopo.ProjetoPermitido(projectId) ? null : NotFound(new { message = "BI CHS001 disponível somente para o projeto CHS001." });
    }

    private static async Task<List<Dictionary<string, object?>>> Linhas(SqlConnection cn, string sql, string projectId, int? versao, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        if (sql.Contains("@versao", StringComparison.Ordinal)) cmd.Parameters.Add("@versao", SqlDbType.Int).Value = (object?)versao ?? DBNull.Value;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var colunas = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
        var linhas = new List<Dictionary<string, object?>>();
        while (await r.ReadAsync(ct))
        {
            var l = new Dictionary<string, object?>(colunas.Length, StringComparer.Ordinal);
            for (int i = 0; i < colunas.Length; i++) l[colunas[i]] = r.IsDBNull(i) ? null : r.GetValue(i);
            linhas.Add(l);
        }
        return linhas;
    }

    private IActionResult Falha(string fonte, SqlException ex)
    {
        logger.LogWarning("Falha na consulta do BI CHS001 isolado ({Fonte}). Number={SqlErrorNumber} Message={SqlErrorMessage}", fonte, ex.Number, ex.Message);
        return StatusCode(503, new { message = "Relatório temporariamente indisponível." });
    }

    private static bool IsAccId(string? value) => value is { Length: 38 } && value.StartsWith("b.", StringComparison.Ordinal)
        && Guid.TryParseExact(value[2..], "D", out _);
}
