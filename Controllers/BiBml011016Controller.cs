using APS.WebApp.Services.BiBml011016;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Controllers;

/// <summary>
/// BI BML011.016 — backend ISOLADO e EXCLUSIVO do Master Admin (contrato: docs/CONTRATO_BI_BML011016.md).
///   GET api/bi-bml011016/status               → projeto resolvido no servidor, 9 páginas do PBIX e estado de mapeamento das fontes
///   GET api/bi-bml011016/fontes/{fonte}       → linhas reais (somente SELECT) com origem, datas, formatos e estado explícito
///   GET api/bi-bml011016/pbix/manifesto       → recursos Report/** do PBIX de referência (SHA256 conferido)
///   GET api/bi-bml011016/pbix/recurso?caminho → conteúdo de um recurso do manifesto
/// Gate: BiBml011016MasterOnly (401 anônimo / 403 não-master, mesmo com Permissions:Enforce=false) + repetição em cada action.
/// Nenhum projectId do request é usado; nenhuma escrita; nenhuma rota existente (inclusive /api/bi-chs001) foi alterada.
/// </summary>
[ApiController]
[Route("api/bi-bml011016")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[BiBml011016MasterOnly]
[RequirePermission("permissions.manage")]
public sealed class BiBml011016Controller(IPermissionService permissions, IConfiguration configuration, ILogger<BiBml011016Controller> logger,
    IBiBml011016DataSource? dataSource = null) : ControllerBase
{
    private IBiBml011016DataSource Fontes => dataSource ?? new BiBml011016SqlDataSource(configuration.GetConnectionString("LocalConnection")
        ?? throw new InvalidOperationException("Conexão de relatórios indisponível."));

    private BiBml011016Pbix Pbix => new(configuration["BiBml011016:PbixPath"]);

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (await BiBml011016MasterPolicy.AvaliarAsync(permissions, logger, ct) is { } bloqueio) return bloqueio;
        var projeto = await ResolverProjetoAsync(ct);
        var pbix = VerificarPbix();
        return Ok(new
        {
            relatorio = BiBml011016Catalogo.CodigoProjeto,
            pbix = new { sha256Esperado = BiBml011016Catalogo.Sha256Pbix, recursosDisponiveis = pbix.Estado == BiBml011016PbixEstado.Ok, motivo = pbix.Motivo },
            projeto,
            paginas = BiBml011016Catalogo.Paginas.Select(p => new
            {
                p.Ordem, p.Nome, p.Visuais, p.Fontes, estado = BiBml011016Catalogo.EstadoPagina(p), p.Observacao,
            }),
            fontes = BiBml011016Catalogo.Fontes.Select(f => new
            {
                fonte = f.Id, f.Origem, mapeamento = f.Mapeamento, motivo = f.MotivoPendente, f.TabelasPbix, f.Correspondencia,
            }),
            geradoEm = DateTime.UtcNow,
        });
    }

    [HttpGet("fontes/{fonte}")]
    public async Task<IActionResult> Fonte(string fonte, CancellationToken ct)
    {
        if (await BiBml011016MasterPolicy.AvaliarAsync(permissions, logger, ct) is { } bloqueio) return bloqueio;
        var definicao = BiBml011016Catalogo.Fonte(fonte);
        if (definicao is null) return NotFound(new { message = "Fonte desconhecida no BI BML011.016." });

        var projeto = await ResolverProjetoAsync(ct);
        if (definicao.Mapeamento != BiBml011016Mapeamento.Mapeada)
            return Ok(Envelope(definicao, projeto, BiBml011016Estado.Pendente, definicao.MotivoPendente, null));
        if (!projeto.EstaResolvido)
            return Ok(Envelope(definicao, projeto, BiBml011016Estado.Pendente,
                $"Projeto {BiBml011016Catalogo.CodigoProjeto} não resolvido no cadastro: {projeto.Motivo}", null));
        try
        {
            var leitura = await Fontes.LerAsync(definicao, projeto.ProjectIdAcc!, ct);
            return Ok(Envelope(definicao, projeto, leitura.Estado, leitura.Motivo, leitura));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            logger.LogWarning("BI BML011.016: falha ao ler a fonte {Fonte}. Tipo={Tipo}", definicao.Id, ex.GetType().Name);
            return StatusCode(503, new { message = "Fonte temporariamente indisponível." });
        }
    }

    /// <summary>Layout PBIR normalizado, privado: nao incluir os JSONs do projeto no bundle publico Vite.</summary>
    [HttpGet("layout/{ordem:int}")]
    public async Task<IActionResult> Layout(int ordem, CancellationToken ct)
    {
        if (await BiBml011016MasterPolicy.AvaliarAsync(permissions, logger, ct) is { } bloqueio) return bloqueio;
        if (ordem < 1 || ordem > BiBml011016Catalogo.Paginas.Count) return NotFound();
        var raiz = configuration["BiBml011016:LayoutsPath"];
        if (string.IsNullOrWhiteSpace(raiz))
            return StatusCode(503, new { message = "Local privado do layout nao configurado." });
        var caminho = Path.Combine(raiz, $"p{ordem:00}.json");
        if (!System.IO.File.Exists(caminho))
            return StatusCode(503, new { message = "Layout original do BI indisponivel no servidor." });
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        var bytes = await System.IO.File.ReadAllBytesAsync(caminho, ct);
        return File(bytes, "application/json");
    }

    /// <summary>Dados originais extraidos do DataModel PBIX, apenas leitura Master Admin.
    /// Endpoint separado das fontes SQL atuais para nunca misturar snapshot com sincronizacao.</summary>
    [HttpGet("pbix/snapshot/{tabela}")]
    public async Task<IActionResult> PbixSnapshot(string tabela, CancellationToken ct)
    {
        if (await BiBml011016MasterPolicy.AvaliarAsync(permissions, logger, ct) is { } bloqueio) return bloqueio;
        var arquivos = new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["ACTION ITEM LIST"] = "table_00.json",
            ["Assignment"] = "table_01.json",
            ["AssignmentByDay"] = "table_02.json",
            ["DESVIO"] = "table_03.json",
            ["HOLDS"] = "table_04.json",
            ["LDP (4)"] = "table_05.json",
            ["WBX"] = "table_09.json",
            ["dCalendario"] = "table_10.json",
            ["dEAP"] = "table_11.json",
            ["dSemana"] = "table_12.json",
        };
        if (!arquivos.TryGetValue(tabela, out var nome)) return NotFound();
        var raiz = configuration["BiBml011016:SnapshotPath"];
        if (string.IsNullOrWhiteSpace(raiz)) return StatusCode(503, new { message = "Snapshot privado nao configurado." });
        var manifesto = Path.Combine(raiz,"manifest.json");
        if (!System.IO.File.Exists(manifesto) || !System.IO.File.Exists(Path.Combine(raiz,nome)))
            return StatusCode(503, new { message = "Dados originais do PBIX indisponiveis." });
        // Origem nunca eh indicada como SQL atual: o header permite auditoria do snapshot historico.
        Response.Headers["X-BI-Data-Origin"] = "PBIX-ORIGINAL-SNAPSHOT";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        var bytes = await System.IO.File.ReadAllBytesAsync(Path.Combine(raiz,nome),ct);
        return File(bytes,"application/json");
    }

    [HttpGet("pbix/manifesto")]
    public async Task<IActionResult> PbixManifesto(CancellationToken ct)
    {
        if (await BiBml011016MasterPolicy.AvaliarAsync(permissions, logger, ct) is { } bloqueio) return bloqueio;
        var pbix = Pbix;
        var estado = VerificarPbix(pbix);
        return Ok(new
        {
            estado = estado.Estado, motivo = estado.Motivo, sha256 = estado.Sha256,
            recursos = estado.Estado == BiBml011016PbixEstado.Ok ? pbix.Manifesto() : Array.Empty<BiBml011016PbixRecurso>(),
        });
    }

    [HttpGet("pbix/recurso")]
    public async Task<IActionResult> PbixRecurso([FromQuery] string? caminho, CancellationToken ct)
    {
        if (await BiBml011016MasterPolicy.AvaliarAsync(permissions, logger, ct) is { } bloqueio) return bloqueio;
        if (!BiBml011016Pbix.CaminhoPermitido(caminho)) return NotFound();
        var pbix = Pbix;
        var estado = VerificarPbix(pbix);
        if (estado.Estado != BiBml011016PbixEstado.Ok) return Conflict(new { estado = estado.Estado, motivo = estado.Motivo });
        var conteudo = pbix.Ler(caminho!);
        if (conteudo is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // SVG/JSON do PBIX nunca executam script se abertos diretamente na aba.
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src data:; style-src 'unsafe-inline'; sandbox";
        return File(conteudo, BiBml011016Pbix.ContentType(caminho!));
    }

    private BiBml011016PbixEstado VerificarPbix(BiBml011016Pbix? pbix = null)
    {
        try { return (pbix ?? Pbix).Verificar(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogWarning("BI BML011.016: PBIX configurado ilegível. Tipo={Tipo}", ex.GetType().Name);
            return new(BiBml011016PbixEstado.Pendente, "PBIX configurado ilegível no servidor.", null);
        }
    }

    private async Task<BiBml011016Projeto> ResolverProjetoAsync(CancellationToken ct)
    {
        try
        {
            var candidatos = await Fontes.ListarCandidatosProjetoAsync(ct);
            return BiBml011016ProjetoResolver.Escolher(candidatos, configuration["BiBml011016:ProjectIdAcc"]);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            logger.LogWarning("BI BML011.016: cadastro de projetos indisponível. Tipo={Tipo}", ex.GetType().Name);
            return BiBml011016Projeto.Falha(BiBml011016Projeto.Indisponivel, "Cadastro de projetos (dbo.Projetos) indisponível no momento.");
        }
    }

    private static object Envelope(BiBml011016Fonte fonte, BiBml011016Projeto projeto, string estado, string? motivo, BiBml011016Leitura? leitura)
    {
        var linhas = leitura?.Linhas ?? Array.Empty<Dictionary<string, object?>>();
        var ultima = linhas.Select(l => l.TryGetValue("Data_Sincronizacao", out var v) ? v as DateTime? : null).Max();
        return new
        {
            fonte = fonte.Id,
            estado,
            motivo,
            origem = fonte.Origem,
            tabelasPbix = fonte.TabelasPbix,
            correspondencia = fonte.Correspondencia,
            projeto,
            ultimaSincronizacao = ultima,
            geradoEm = DateTime.UtcNow,
            colunas = leitura?.Colunas ?? Array.Empty<BiBml011016Coluna>(),
            totalLinhas = linhas.Count,
            linhas,
        };
    }
}
