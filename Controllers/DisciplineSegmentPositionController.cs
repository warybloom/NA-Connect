using APS.WebApp.Services.DisciplineAlias;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;
using NA_Connect.Services.Coordination;

namespace APS.WebApp.Controllers;

public sealed record ExcecaoPosicaoEntrada(string? Criterio, string? CodigoDocumento, string? SegmentoDoc3, string? SiglaEsperada, int Posicao);
public sealed record PreviaExcecoesPosicao(IReadOnlyList<ExcecaoPosicaoEntrada>? Novas, IReadOnlyList<long>? Desativar);
public sealed record IncluirExcecoesPosicao(IReadOnlyList<ExcecaoPosicaoEntrada>? Novas, string? Motivo, bool ConfirmarAlteracao);
public sealed record AtivoExcecaoPosicao(bool Ativo, int Versao);

/// <summary>
/// Configurações → Normalizador de Siglas → Exceções de posição da disciplina (26/10/2026, revisão BYD002).
/// NÃO existe "DOC4 do projeto": o padrão é sempre DOC3. Cada exceção vale para UM número de documento exato (CODIGO) ou para
/// um padrão limitado "DOC3 = X e DOC4 = sigla exata" (PADRAO), só no projeto dela. A sigla esperada precisa ser reconhecida
/// pela Norma do projeto (ou pelo Normalizador, onde a regra vigente já o aceita: projeto sem LDP). Inclusão exige prévia por
/// documento; piora ou troca de disciplina já identificada exige confirmação. Reverter = desativar (com auditoria).
/// Sem a migração 20261026_002, candidatos e prévia funcionam; gravar responde 503.
/// </summary>
[ApiController]
[Route("api/configuracoes/normalizador-siglas/posicao-disciplina")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DisciplineSegmentPositionController(IPermissionService permissions, IProjectAccessService projectAccess,
    IConfiguration configuration, ILogger<DisciplineSegmentPositionController> logger) : ControllerBase
{
    public const int MaxNovasPorLote = 500;
    private DisciplineSegmentStore Store => new(configuration);

    [HttpGet]
    [RequirePermission(DisciplineAliasController.PermissaoVer, DisciplineAliasController.PermissaoEditar)]
    public Task<IActionResult> Listar([FromQuery] string? projectId, CancellationToken ct) => Executar(async () =>
    {
        if (!await Store.InstaladoAsync(ct))
            return Ok(new { instalado = false, excecoes = Array.Empty<ExcecaoPosicaoRegistro>(), message = new PosicaoDisciplinaNaoInstaladaException().Message });
        return Ok(new { instalado = true, excecoes = await Store.ListarAsync(string.IsNullOrWhiteSpace(projectId) ? null : projectId, ct) });
    }, ct);

    [HttpGet("historico")]
    [RequirePermission(DisciplineAliasController.PermissaoVer, DisciplineAliasController.PermissaoEditar)]
    public Task<IActionResult> Historico([FromQuery] string? projectId, CancellationToken ct) =>
        Executar(async () => Ok(await Store.HistoricoAsync(string.IsNullOrWhiteSpace(projectId) ? null : projectId, ct)), ct);

    /// <summary>Documentos sem disciplina hoje: candidatos (sigla reconhecida na posição pesquisada) e pendentes com motivo. Não grava.</summary>
    [HttpGet("{projectId}/candidatos")]
    [RequirePermission(DisciplineAliasController.PermissaoVer, DisciplineAliasController.PermissaoEditar, AnyProject = true)]
    public Task<IActionResult> Candidatos(string projectId, [FromQuery] int posicao = 4, CancellationToken ct = default) => Executar(async () =>
    {
        if (await NegarProjetoAsync(projectId, ct) is { } negado) return negado;
        if (posicao is < ExcecaoPosicao.PosicaoMinima or > ExcecaoPosicao.PosicaoMaxima || posicao == ExcecaoPosicao.PosicaoPadrao)
            return BadRequest(new { message = "Posição inválida (use 2, 4 ou 5)." });
        var p = await new DisciplineSegmentImpactReader(configuration).LerAsync(projectId, ct);
        if (p is null) return NotFound(new { message = "Projeto não encontrado ou inativo." });
        var (candidatos, pendentes, semMapeamento) = DisciplineSegmentImpact.Candidatos(p.Documentos, p.Atual, posicao, p.Resolver);
        return Ok(new CandidatosExcecao(p.ProjectId, p.Projeto, p.SemLdp, p.Fonte, posicao, p.Documentos.Count, semMapeamento, candidatos, pendentes));
    }, ct);

    /// <summary>Prévia (não grava): exceções ativas × ativas + novas − desativadas, documento a documento.</summary>
    [HttpPost("{projectId}/impacto")]
    [RequirePermission(DisciplineAliasController.PermissaoVer, DisciplineAliasController.PermissaoEditar, AnyProject = true)]
    public Task<IActionResult> Impacto(string projectId, [FromBody] PreviaExcecoesPosicao body, CancellationToken ct) => Executar(async () =>
    {
        if (await NegarProjetoAsync(projectId, ct) is { } negado) return negado;
        var p = await new DisciplineSegmentImpactReader(configuration).LerAsync(projectId, ct);
        if (p is null) return NotFound(new { message = "Projeto não encontrado ou inativo." });
        var (novas, erros) = ValidarNovas(body.Novas ?? [], p);
        if (erros.Count > 0) return BadRequest(new { message = string.Join(" ", erros.Take(5)), erros });
        var desativar = (body.Desativar ?? []).ToHashSet();
        var depois = p.Atual.Ativas.Where(e => !desativar.Contains(e.Id))
            .Concat(novas.Select((n, i) => Regra(n, -(i + 1))));
        return Ok(Impacto(p, new DisciplineSegmentRules(depois)));
    }, ct);

    [HttpPost("{projectId}/excecoes")]
    [RequirePermission(DisciplineAliasController.PermissaoEditar, AnyProject = true)]
    public Task<IActionResult> Incluir(string projectId, [FromBody] IncluirExcecoesPosicao body, CancellationToken ct) => ComUsuario(async usuario =>
    {
        if (await NegarProjetoAsync(projectId, ct, editar: true) is { } negado) return negado;
        if (body.Novas is not { Count: > 0 }) return BadRequest(new { message = "Nenhuma exceção informada." });
        if (body.Novas.Count > MaxNovasPorLote) return BadRequest(new { message = $"No máximo {MaxNovasPorLote} exceções por lote." });
        var p = await new DisciplineSegmentImpactReader(configuration).LerAsync(projectId, ct);
        if (p is null) return NotFound(new { message = "Projeto não encontrado ou inativo." });
        var (novas, erros) = ValidarNovas(body.Novas, p);
        if (erros.Count > 0) return BadRequest(new { message = string.Join(" ", erros.Take(5)), erros });
        // A prévia é refeita no servidor: a tela pode estar desatualizada.
        var impacto = Impacto(p, new DisciplineSegmentRules(p.Atual.Ativas.Concat(novas.Select((n, i) => Regra(n, -(i + 1))))));
        if ((impacto.Resumo.Pioraram > 0 || impacto.Resumo.DisciplinaAlterada > 0) && !body.ConfirmarAlteracao)
            return Conflict(new { message = $"As exceções deixariam {impacto.Resumo.Pioraram} documento(s) sem disciplina e trocariam a disciplina de {impacto.Resumo.DisciplinaAlterada}. Revise a prévia ou confirme explicitamente.", impacto });
        var gravadas = await Store.IncluirAsync(projectId, novas.Select(n => n with { Motivo = body.Motivo }).ToArray(), usuario, ct);
        logger.LogInformation("Exceções de posição incluídas. Projeto={Projeto} Qtde={Qtde} Usuario={Usuario} Corrigidos={Corrigidos} Pioraram={Pioraram} Alterados={Alterados}",
            projectId, gravadas.Count, usuario, impacto.Resumo.CorrigidosPorExcecao, impacto.Resumo.Pioraram, impacto.Resumo.DisciplinaAlterada);
        return Ok(new { excecoes = gravadas, impacto.Resumo });
    }, ct);

    /// <summary>Ativar/desativar (reverter) uma exceção. Desativar volta os documentos dela ao DOC3.</summary>
    [HttpPost("{projectId}/excecoes/{id:long}/ativo")]
    [RequirePermission(DisciplineAliasController.PermissaoEditar, AnyProject = true)]
    public Task<IActionResult> DefinirAtivo(string projectId, long id, [FromBody] AtivoExcecaoPosicao body, CancellationToken ct) => ComUsuario(async usuario =>
    {
        if (await NegarProjetoAsync(projectId, ct, editar: true) is { } negado) return negado;
        var salva = await Store.DefinirAtivoAsync(projectId, id, body.Versao, body.Ativo, usuario, ct);
        return Ok(salva);
    }, ct);

    private static ExcecaoPosicao Regra(NovaExcecaoPosicao n, long idTemporario) =>
        new(idTemporario, n.Criterio, n.CodigoDocumento, n.SegmentoDoc3, n.SiglaEsperada, n.Posicao, true);

    /// <summary>
    /// Validação contra o PRÓPRIO projeto: sigla esperada reconhecida (Norma; Normalizador só em projeto sem LDP), código existente
    /// no inventário com a sigla na posição, padrão cujo DOC3 NÃO seja sigla reconhecida (senão seria ambíguo) e nada duplicado.
    /// </summary>
    private static (IReadOnlyList<NovaExcecaoPosicao> Novas, IReadOnlyList<string> Erros) ValidarNovas(
        IReadOnlyList<ExcecaoPosicaoEntrada> entradas, ProjetoDocumentosSegmentos p)
    {
        var erros = new List<string>();
        var novas = new List<NovaExcecaoPosicao>();
        var inventario = p.Documentos.ToDictionary(d => DisciplineSegments.Chave(d.Codigo), StringComparer.Ordinal);
        var chaves = new HashSet<string>(p.Atual.Ativas.Select(ChaveRegra), StringComparer.Ordinal);
        foreach (var (e, i) in entradas.Select((e, i) => (e, i + 1)))
        {
            var criterio = DisciplineSegments.Chave(e.Criterio);
            var estrutura = ExcecaoPosicao.Validar(criterio, e.CodigoDocumento, e.SegmentoDoc3, e.SiglaEsperada, e.Posicao);
            if (estrutura.Count > 0) { erros.AddRange(estrutura.Select(x => $"Item {i}: {x}")); continue; }
            var sigla = DisciplineSegments.Chave(e.SiglaEsperada);
            var resolvida = p.Resolver(sigla);
            if (!resolvida.Reconhecida) { erros.Add($"Item {i}: sigla '{sigla}' não é reconhecida para este projeto ({resolvida.Motivo})."); continue; }
            string? codigo = null, segmento = null;
            if (criterio == CriteriosExcecaoPosicao.Codigo)
            {
                codigo = DisciplineSegments.Chave(e.CodigoDocumento);
                if (!inventario.TryGetValue(codigo, out var doc)) { erros.Add($"Item {i}: documento '{codigo}' não existe no inventário do projeto ({p.Fonte})."); continue; }
                if (DisciplineSegments.Chave(DisciplineSegments.Segmento(doc.Segmentos, e.Posicao)) != sigla)
                { erros.Add($"Item {i}: o DOC{e.Posicao} de '{codigo}' não é '{sigla}'."); continue; }
            }
            else
            {
                segmento = DisciplineSegments.Chave(e.SegmentoDoc3);
                if (p.Resolver(segmento).Reconhecida)
                { erros.Add($"Item {i}: DOC3 '{segmento}' já é sigla reconhecida; padrão seria ambíguo. Use exceção por número de documento."); continue; }
            }
            var nova = new NovaExcecaoPosicao(criterio, codigo, segmento, sigla, e.Posicao, resolvida.Disciplina!.Trim(), null);
            if (!chaves.Add(ChaveRegra(Regra(nova, 0)))) { erros.Add($"Item {i}: exceção já existe (ativa ou repetida no lote)."); continue; }
            novas.Add(nova);
        }
        return (novas, erros);
    }

    private static string ChaveRegra(ExcecaoPosicao e) =>
        $"{e.Criterio}|{DisciplineSegments.Chave(e.CodigoDocumento)}|{DisciplineSegments.Chave(e.SegmentoDoc3)}|{DisciplineSegments.Chave(e.SiglaEsperada)}|{e.Posicao}";

    private static ImpactoPosicaoDisciplina Impacto(ProjetoDocumentosSegmentos p, DisciplineSegmentRules depois)
    {
        var (resumo, documentos) = DisciplineSegmentImpact.Comparar(p.Documentos, p.Atual, depois, p.Resolver);
        return new(p.ProjectId, p.Projeto, p.SemLdp, p.Fonte, p.Atual.Ativas, depois.Ativas, resumo, documentos);
    }

    private async Task<IActionResult?> NegarProjetoAsync(string projectId, CancellationToken ct, bool editar = false)
    {
        var acesso = await projectAccess.CheckProjectAccessAsync(projectId, ct);
        if (acesso == PermissionCheck.Unauthenticated) return Unauthorized(new { message = "Sessão expirada." });
        if (acesso == PermissionCheck.Denied) return StatusCode(403, new { message = "Projeto não autorizado." });
        if (acesso != PermissionCheck.Allowed) return StatusCode(503, new { message = "Não foi possível validar o acesso ao projeto." });
        string[] chaves = editar ? [DisciplineAliasController.PermissaoEditar] : [DisciplineAliasController.PermissaoVer, DisciplineAliasController.PermissaoEditar];
        var permissao = await permissions.CheckAsync(chaves, projectId, ct);
        if (permissao == PermissionCheck.Allowed) return null;
        return permissao == PermissionCheck.Unauthenticated ? Unauthorized(new { message = "Sessão expirada." })
            : StatusCode(403, new { message = "Você não tem permissão para este projeto." });
    }

    private async Task<IActionResult> ComUsuario(Func<string, Task<IActionResult>> acao, CancellationToken ct)
    {
        var eu = await permissions.GetCurrentUserAsync(ct);
        var usuario = eu is null ? null : !string.IsNullOrWhiteSpace(eu.Email) ? eu.Email : eu.Nome;
        if (string.IsNullOrWhiteSpace(usuario)) return Unauthorized(new { message = "Sessão expirada." });
        return await Executar(() => acao(usuario), ct);
    }

    private async Task<IActionResult> Executar(Func<Task<IActionResult>> acao, CancellationToken ct)
    {
        try { return await acao(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (PosicaoDisciplinaNaoInstaladaException ex) { return StatusCode(503, new { instalado = false, message = ex.Message }); }
        catch (NormalizadorNaoInstaladoException ex) { return StatusCode(503, new { instalado = false, message = ex.Message }); }
        catch (PosicaoDisciplinaInvalidaException ex) { return BadRequest(new { message = ex.Message, erros = ex.Erros }); }
        catch (PosicaoDisciplinaConcorrenciaException ex) { return Conflict(new { message = ex.Message }); }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
        { return Conflict(new { message = "Exceção já cadastrada (ativa ou desativada). Para usar de novo, reative-a na lista em vez de incluir." }); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Exceções de posição indisponíveis. Ref={Ref}", HttpContext.TraceIdentifier);
            return StatusCode(503, new { message = $"Exceções de posição da disciplina temporariamente indisponíveis. (ref: {HttpContext.TraceIdentifier})" });
        }
    }
}
