using APS.WebApp.Services.Identity;

namespace APS.WebApp.Services.Permissions.Sync;

/// <summary>
/// Sincronização ACC → permissões (independe de Permissions:Enforce: os dados são mantidos mesmo sem bloqueio ativo).
///  • LOGIN: eu → meus projetos ACC (hubs/projetos já existentes no AutodeskApiService) → meus vínculos → perfil padrão ausente.
///  • PROJETO (botão do admin): membros ACC daquele projeto → usuários/vínculos/perfil padrão do projeto.
/// Regras de decisão: PermissionSyncPlanner. MasterAdmin nunca é atribuído automaticamente.
/// </summary>
public sealed class PermissionSyncService(IPermissionSyncRepository repo, IAccMembershipSource acc, IConfiguration configuration,
    ILogger<PermissionSyncService> logger)
{
    public const string OrigemLogin = "ACC_SYNC:LOGIN", OrigemProjeto = "ACC_SYNC:PROJECT";

    private string NomePerfilPadrao => configuration["Permissions:DefaultProjectProfile"] is { Length: > 0 } n ? n : "Somente Leitura";

    private async Task<int?> PerfilPadraoAsync(CancellationToken ct)
    {
        var id = await repo.PerfilPadraoIdAsync(NomePerfilPadrao, ct);
        if (id is null) logger.LogWarning("Perfil padrão '{Perfil}' inexistente/inativo: nenhum perfil será atribuído automaticamente.", NomePerfilPadrao);
        return id;
    }

    /// <summary>Sincroniza o usuário que acabou de entrar. Falha Autodesk nunca desativa vínculos.</summary>
    public async Task<ResumoSincronizacao> SincronizarUsuarioAsync(AutodeskUserInfo usuario, string accessToken, CancellationToken ct)
    {
        if (usuario.Email.Length == 0) throw new InvalidOperationException("UserInfo sem e-mail: usuário não pode ser sincronizado.");
        var (userId, criado) = await repo.UpsertUsuarioAsync(usuario.AutodeskUserId, usuario.Email, usuario.Nome, registrarAcesso: true,
            porUserId: null, porEmail: usuario.Email, origem: OrigemLogin, ct);

        var projetosAcc = await acc.ProjetosDoUsuarioAsync(accessToken, ct);
        var projetosNa = await repo.ProjetosNaAsync(ct);
        var porAcc = projetosNa.GroupBy(p => p.ProjectIdAcc, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var porId = projetosNa.ToDictionary(p => p.ProjetoId);

        var candidatos = projetosAcc.ProjectIdsAcc.Where(porAcc.ContainsKey).Select(a => porAcc[a]).Where(p => p.Ativo).ToList();
        int ignorados = projetosAcc.ProjectIdsAcc.Count - candidatos.Count;
        var estados = await repo.EstadosAsync(candidatos.Select(p => (userId, p.ProjetoId)).ToList(), vinculosDoUsuario: userId, vinculosDoProjeto: null, ct);
        var perfilPadrao = await PerfilPadraoAsync(ct);

        var ops = new List<OperacaoPlanejada>();
        foreach (var e in estados)
        {
            if (!porId.TryGetValue(e.ProjetoId, out var projeto)) continue;
            bool presente = projetosAcc.ProjectIdsAcc.Contains(projeto.ProjectIdAcc);
            ops.AddRange(PermissionSyncPlanner.Planejar(e, presente, projeto.Ativo, projetosAcc.Completa, perfilPadrao is not null)
                .Select(o => new OperacaoPlanejada(userId, e.ProjetoId, o)));
        }
        await repo.AplicarAsync(ops, perfilPadrao, porUserId: userId, porEmail: usuario.Email, origem: OrigemLogin, ct);
        var resumo = Resumir(OrigemLogin, projetosAcc.Completa, projetosAcc.ProjectIdsAcc.Count, criado ? 1 : 0, ops, ignorados, projetosAcc.Falha);
        logger.LogInformation("Sincronização ACC no login: completa={Completa} projetosAcc={Acc} vinculados={V} reativados={R} desativados={D} perfisPadrao={P}",
            resumo.ListagemCompleta, resumo.Encontrados, resumo.Vinculados, resumo.Reativados, resumo.Desativados, resumo.PerfisPadraoAtribuidos);
        return resumo;
    }

    /// <summary>
    /// Sincroniza os membros ACC de UM projeto N&amp;A. Se a Autodesk falhar, LANÇA antes de qualquer escrita
    /// (lista parcial/erro nunca vira "ninguém mais tem acesso").
    /// </summary>
    public async Task<ResumoSincronizacao> SincronizarProjetoAsync(ProjetoNa projeto, string accessToken, int? porUserId, string? porEmail, CancellationToken ct)
    {
        if (!projeto.Ativo) throw new InvalidOperationException("Projeto inativo no N&A Connect.");
        var membros = await acc.MembrosDoProjetoAsync(projeto.ProjectIdAcc, accessToken, ct); // lança em falha: nada é alterado

        int criados = 0;
        var membrosIds = new HashSet<int>();
        foreach (var m in membros)
        {
            var (id, criado) = await repo.UpsertUsuarioAsync(m.AutodeskUserId, m.Email, m.Nome, registrarAcesso: false, porUserId, porEmail, OrigemProjeto, ct);
            membrosIds.Add(id);
            if (criado) criados++;
        }
        var estados = await repo.EstadosAsync(membrosIds.Select(u => (u, projeto.ProjetoId)).ToList(), vinculosDoUsuario: null,
            vinculosDoProjeto: projeto.ProjetoId, ct);
        var perfilPadrao = await PerfilPadraoAsync(ct);

        var ops = new List<OperacaoPlanejada>();
        foreach (var e in estados)
            ops.AddRange(PermissionSyncPlanner.Planejar(e, membrosIds.Contains(e.UserId), projetoAtivoNoNa: true, listagemCompleta: true, perfilPadrao is not null)
                .Select(o => new OperacaoPlanejada(e.UserId, projeto.ProjetoId, o)));
        await repo.AplicarAsync(ops, perfilPadrao, porUserId, porEmail, OrigemProjeto, ct);

        var resumo = Resumir(OrigemProjeto, true, membros.Count, criados, ops, 0, null);
        await repo.RegistrarResumoAsync(projeto.ProjetoId, porUserId, porEmail, resumo, ct);
        return resumo;
    }

    private static ResumoSincronizacao Resumir(string origem, bool completa, int encontrados, int criados, IReadOnlyList<OperacaoPlanejada> ops,
        int ignorados, string? aviso) =>
        new(DateTime.UtcNow, origem, completa, encontrados, criados,
            ops.Count(o => o.Operacao == OperacaoVinculo.Adicionar), ops.Count(o => o.Operacao == OperacaoVinculo.Reativar),
            ops.Count(o => o.Operacao == OperacaoVinculo.Desativar), ops.Count(o => o.Operacao == OperacaoVinculo.AtribuirPerfilPadrao),
            ignorados, aviso);
}

/// <summary>
/// Dispara a sincronização do PRÓPRIO usuário logo após o login, em segundo plano (o login não espera a Autodesk).
/// Não sincroniza todos os membros de todos os projetos — isso é feito por projeto, no botão do admin.
/// </summary>
public sealed class PermissionLoginSync(IServiceScopeFactory scopes, ILogger<PermissionLoginSync> logger)
{
    public void Disparar(AutodeskUserInfo usuario, string accessToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopes.CreateScope();
                var perms = scope.ServiceProvider.GetRequiredService<IPermissionRepository>();
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                if (!await perms.SchemaInstaladoAsync(cts.Token)) return;
                await scope.ServiceProvider.GetRequiredService<PermissionSyncService>().SincronizarUsuarioAsync(usuario, accessToken, cts.Token);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Sincronização ACC do login falhou ({Tipo}); nenhum vínculo foi desativado por causa disso.", ex.GetType().Name);
            }
        });
    }
}
