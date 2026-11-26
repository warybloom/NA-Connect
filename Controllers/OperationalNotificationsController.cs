using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using NA_Connect.Services;

namespace APS.WebApp.Controllers;

/// <summary>
/// Central interna de notificacoes da engenharia, versao 1.
/// Le somente as ocorrencias reais dos arquivos Erros_YYYYMMDD.txt do robo.
/// Nenhuma acao neste controller inicia robos ou altera o ACC.
/// </summary>
[ApiController]
[Route("api/notifications")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OperationalNotificationsController(
    IPermissionService permissions,
    IPermissionRepository repository,
    DatabaseService database,
    IConfiguration configuration,
    ILogger<OperationalNotificationsController> logger) : ControllerBase
{
    private const string LogDirectory = @"C:\Robo_NA_Connect\Logs_Historico";
    private static readonly Regex Registro = new(
        @"^(?<date>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) \| Projeto: (?<project>.*?) \| Etapa: (?<stage>.*?) \| Status: ERRO(?: \| (?<message>.*))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SeparadorCodigo = new(@"^\s*(?<code>[A-Za-z]{2,}[0-9][A-Za-z0-9.]*)", RegexOptions.Compiled);

    private sealed record ProjetoPermitido(string Nome, string AccId);
    private sealed record Ocorrencia(DateTime At, string Projeto, string Etapa, string Mensagem);
    public sealed record Notificacao(string Id, string ProjectIdAcc, string Projeto, string Etapa,
        string Tipo, string Titulo, string Mensagem, DateTime PrimeiraOcorrencia, DateTime UltimaOcorrencia,
        int Repeticoes, string Situacao, bool Lida);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int days = 7, CancellationToken ct = default)
    {
        var usuario = await permissions.GetCurrentUserAsync(ct);
        if (usuario is null) return Unauthorized();
        days = Math.Clamp(days, 1, 14);
        try
        {
            var projetos = await ProjetosPermitidos(usuario, ct);
            if (projetos.Count == 0)
                return Ok(new { items = Array.Empty<Notificacao>(), unreadCount = 0, windowDays = days });
            var items = Montar(projetos, days);
            var lidas = usuario.UserId is int id ? await LerLidasAsync(id, ct) : new HashSet<string>();
            var resposta = items.Select(n => n with { Lida = lidas.Contains(n.Id) })
                .OrderByDescending(n => n.UltimaOcorrencia).Take(120).ToList();
            return Ok(new { items = resposta, unreadCount = resposta.Count(n => !n.Lida), windowDays = days });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Central de notificacoes indisponivel.");
            return StatusCode(503, new { message = "Nao foi possivel consultar as notificacoes." });
        }
    }

    [HttpPost("{key}/read")]
    public async Task<IActionResult> MarkRead(string key, CancellationToken ct)
    {
        var usuario = await permissions.GetCurrentUserAsync(ct);
        if (usuario is null) return Unauthorized();
        if (usuario.UserId is not int userId) return StatusCode(403);
        if (!Regex.IsMatch(key ?? "", "^[A-F0-9]{64}$", RegexOptions.CultureInvariant))
            return BadRequest(new { message = "Identificador invalido." });
        var visible = Montar(await ProjetosPermitidos(usuario, ct), 14);
        if (!visible.Any(n => n.Id == key)) return NotFound();

        try
        {
            await using var connection = await AbrirSql(ct);
            await using var cmd = new SqlCommand(@"
IF NOT EXISTS (SELECT 1 FROM dbo.NA_NotificationReads WITH (UPDLOCK, HOLDLOCK)
               WHERE UserId = @userId AND NotificationKey = @key)
    INSERT INTO dbo.NA_NotificationReads(UserId, NotificationKey, ReadAt)
    VALUES(@userId, @key, SYSUTCDATETIME());", connection);
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@key", key);
            await cmd.ExecuteNonQueryAsync(ct);
            return NoContent();
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Falha ao registrar leitura de notificacao.");
            return StatusCode(503, new { message = "Nao foi possivel marcar como lida." });
        }
    }

    private async Task<List<ProjetoPermitido>> ProjetosPermitidos(CurrentPermissionUser usuario, CancellationToken ct)
    {
        if (!usuario.IsMasterAdmin)
        {
            if (usuario.UserId is not int id) return new List<ProjetoPermitido>();
            return (await repository.ListarProjetosPermitidosAsync(id, ct))
                .Where(p => p.IsAtivo && !string.IsNullOrWhiteSpace(p.ProjectIdAcc))
                .Select(p => new ProjetoPermitido(p.NomeProjeto, p.ProjectIdAcc)).ToList();
        }

        return database.GetTodosProjetosParaAdmin().AsEnumerable()
            .Where(r => r["Is_Ativo"] != DBNull.Value && Convert.ToBoolean(r["Is_Ativo"]))
            .Select(r => new ProjetoPermitido(Convert.ToString(r["Nome_Projeto"]) ?? "",
                                             Convert.ToString(r["Project_ID_ACC"]) ?? ""))
            .Where(p => !string.IsNullOrWhiteSpace(p.Nome) && !string.IsNullOrWhiteSpace(p.AccId)).ToList();
    }

    private static List<Notificacao> Montar(IReadOnlyList<ProjetoPermitido> permitidos, int days)
    {
        var eventos = LerErros(days);
        var dados = new List<(Ocorrencia Evento, ProjetoPermitido Projeto)>();
        foreach (var e in eventos)
        {
            var nome = Normalizar(e.Projeto);
            var exatos = permitidos.Where(p => Normalizar(p.Nome) == nome).ToList();
            var candidatos = exatos.Count > 0 ? exatos : permitidos.Where(p =>
                Codigo(p.Nome).Length > 0 && Codigo(p.Nome) == Codigo(e.Projeto)).ToList();
            // Sem vinculo inequívoco com o projeto permitido, nao mostrar: falhar fechado.
            if (candidatos.Count == 1) dados.Add((e, candidatos[0]));
        }

        return dados.GroupBy(x => new {
            x.Projeto.AccId,
            x.Evento.Etapa,
            Mensagem = Normalizar(x.Evento.Mensagem[..Math.Min(x.Evento.Mensagem.Length, 160)])
        }).Select(g =>
        {
            var ultimo = g.OrderByDescending(x => x.Evento.At).First();
            var tipo = Classificar(ultimo.Evento.Mensagem);
            var chave = Sha($"{g.Key.AccId}|{g.Key.Etapa}|{g.Key.Mensagem}");
            return new Notificacao(chave, ultimo.Projeto.AccId, ultimo.Projeto.Nome, ultimo.Evento.Etapa,
                tipo.Tipo, tipo.Titulo, Resumo(ultimo.Evento.Mensagem),
                g.Min(x => x.Evento.At), g.Max(x => x.Evento.At), g.Count(), "A verificar", false);
        }).OrderByDescending(n => n.UltimaOcorrencia).Take(150).ToList();
    }

    private static List<Ocorrencia> LerErros(int days)
    {
        var resultado = new List<Ocorrencia>();
        if (!Directory.Exists(LogDirectory)) return resultado;
        for (var i = 0; i < days; i++)
        {
            var dia = DateTime.Today.AddDays(-i);
            var arquivo = Path.Combine(LogDirectory, $"Erros_{dia:yyyyMMdd}.txt");
            if (!System.IO.File.Exists(arquivo) || new FileInfo(arquivo).Length > 5_000_000) continue;
            Ocorrencia? atual = null;
            foreach (var linha in System.IO.File.ReadLines(arquivo))
            {
                var match = Registro.Match(linha);
                if (match.Success)
                {
                    if (atual != null) resultado.Add(atual);
                    atual = DateTime.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
                        ? new Ocorrencia(data, match.Groups["project"].Value.Trim(),
                            match.Groups["stage"].Value.Trim(), match.Groups["message"].Value.Trim())
                        : null;
                }
                else if (atual != null && !string.IsNullOrWhiteSpace(linha) && atual.Mensagem.Length < 700)
                    atual = atual with { Mensagem = atual.Mensagem + " " + linha.Trim() };
            }
            if (atual != null) resultado.Add(atual);
        }
        return resultado;
    }

    private async Task<HashSet<string>> LerLidasAsync(int userId, CancellationToken ct)
    {
        await using var connection = await AbrirSql(ct);
        await using var cmd = new SqlCommand(
            "SELECT NotificationKey FROM dbo.NA_NotificationReads WHERE UserId=@userId", connection);
        cmd.Parameters.AddWithValue("@userId", userId);
        var resultado = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) resultado.Add(reader.GetString(0));
        return resultado;
    }

    private async Task<SqlConnection> AbrirSql(CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Connection string indisponivel.");
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static string Codigo(string s) => SeparadorCodigo.Match(s).Groups["code"].Value.ToUpperInvariant();
    private static string Normalizar(string s)
    {
        var nfd = (s ?? "").Normalize(NormalizationForm.FormD);
        var semAcento = new string(nfd.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(semAcento, @"\s+", " ").Trim().ToUpperInvariant();
    }
    private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    private static string Resumo(string s)
    {
        var texto = Regex.Replace(s ?? "", @"\s+", " ").Trim();
        return texto.Length > 350 ? texto[..350] + "..." : texto;
    }
    private static (string Tipo, string Titulo) Classificar(string s)
    {
        var v = s.ToUpperInvariant();
        if (v.Contains("ERR_WIP_ERROR") || (v.Contains("403") && (v.Contains("VERSION") || v.Contains("VERSAO"))))
            return ("ACC", "Possivel arquivo reservado ou bloqueado");
        if (v.Contains("PASTA N") && v.Contains("ENCONTRAD"))
            return ("PASTA", "Pasta nao encontrada na ACC");
        if (v.Contains("PERMISS") || v.Contains("FORBIDDEN") || v.Contains("401"))
            return ("ACESSO", "Falha de acesso ou permissao");
        return ("ROBO", "Falha durante a atualizacao do projeto");
    }
}