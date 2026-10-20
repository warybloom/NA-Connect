namespace APS.WebApp.Services.Permissions.Sync;

/// <summary>Operação sobre o vínculo usuário × projeto decidida pela sincronização ACC.</summary>
public enum OperacaoVinculo { Adicionar, Reativar, Desativar, AtribuirPerfilPadrao }

/// <summary>
/// Estado atual de UM vínculo (usuário × projeto N&amp;A) lido do banco.
/// Ativo: null = não existe linha em perm.UserProjects.
/// TemPerfil: existe qualquer linha em perm.UserProjectProfiles para o par.
/// PerfilJaConfigurado: o par já teve perfil configurado (admin) ou padrão atribuído (auditoria) — nunca reatribuir.
/// DesativadoPeloAdmin: o último evento de status do par foi uma desativação MANUAL do administrador.
/// </summary>
public sealed record EstadoVinculo(int UserId, int ProjetoId, bool? Ativo, bool TemPerfil, bool PerfilJaConfigurado, bool DesativadoPeloAdmin);

public sealed record OperacaoPlanejada(int UserId, int ProjetoId, OperacaoVinculo Operacao);

/// <summary>
/// REGRAS da sincronização (puras, sem banco/Autodesk):
///  • Presente no ACC + projeto ativo no N&amp;A: cria o vínculo (ou reativa, exceto se o admin desativou manualmente).
///  • Vínculo ativo sem NENHUM perfil e que nunca teve perfil configurado/padrão: recebe o perfil padrão (Somente Leitura).
///    Perfil existente (ex.: Engenharia) nunca é trocado; perfil removido de propósito pelo admin nunca volta sozinho.
///  • Ausente no ACC: desativa SOMENTE com listagem ACC completa e bem-sucedida. Falha/timeout/token expirado = nada muda.
///  • Nada é apagado: perfis, exceções e auditoria permanecem (reentrada reativa mantendo a configuração).
///  • Projeto fora do N&amp;A (ou inativo no N&amp;A) nunca entra no sistema de permissões.
/// </summary>
public static class PermissionSyncPlanner
{
    public static IReadOnlyList<OperacaoVinculo> Planejar(EstadoVinculo estado, bool presenteNoAcc, bool projetoAtivoNoNa,
        bool listagemCompleta, bool existePerfilPadrao)
    {
        var ops = new List<OperacaoVinculo>();
        if (presenteNoAcc)
        {
            if (!projetoAtivoNoNa) return ops;
            if (estado.Ativo is null) ops.Add(OperacaoVinculo.Adicionar);
            else if (estado.Ativo == false)
            {
                if (estado.DesativadoPeloAdmin) return ops; // decisão administrativa prevalece sobre a sincronização
                ops.Add(OperacaoVinculo.Reativar);
            }
            if (existePerfilPadrao && !estado.TemPerfil && !estado.PerfilJaConfigurado) ops.Add(OperacaoVinculo.AtribuirPerfilPadrao);
            return ops;
        }
        if (listagemCompleta && estado.Ativo == true) ops.Add(OperacaoVinculo.Desativar);
        return ops;
    }
}

/// <summary>Resultado de uma sincronização (exibido na aba Usuários e gravado na auditoria).</summary>
public sealed record ResumoSincronizacao(
    DateTime Em, string Origem, bool ListagemCompleta, int Encontrados, int UsuariosCriados, int Vinculados, int Reativados,
    int Desativados, int PerfisPadraoAtribuidos, int IgnoradosForaDoNa, string? Aviso = null);
