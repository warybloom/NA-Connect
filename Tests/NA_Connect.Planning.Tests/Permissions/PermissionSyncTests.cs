using APS.WebApp.Services.Identity;
using APS.WebApp.Services.Permissions.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests.Permissions;

/// <summary>Sincronização ACC → Users / UserProjects / perfil padrão (Somente Leitura por projeto).</summary>
public class PermissionSyncTests
{
    private const string P1 = "b.11111111-0000-0000-0000-000000000001", P2 = "b.11111111-0000-0000-0000-000000000002",
        P3 = "b.11111111-0000-0000-0000-000000000003", P4 = "b.11111111-0000-0000-0000-000000000004",
        FORA = "b.99999999-0000-0000-0000-000000000009", INATIVO_NA = "b.11111111-0000-0000-0000-000000000005";

    private static readonly AutodeskUserInfo Joao = new("SUB-JOAO", "joao@na.com", "João Silva");

    private sealed class Mundo
    {
        public FakeSyncRepository Repo { get; } = new();
        public FakeAcc Acc { get; } = new();
        public PermissionSyncService Svc { get; }
        public Mundo()
        {
            Repo.Projetos.AddRange(new[]
            {
                new ProjetoNa(1, P1, "CHS001", true), new ProjetoNa(2, P2, "BML011", true), new ProjetoNa(3, P3, "CON006", true),
                new ProjetoNa(4, P4, "BML022", true), new ProjetoNa(5, INATIVO_NA, "OLD001", false),
            });
            Repo.Perfis[10] = "Somente Leitura"; Repo.Perfis[20] = "Engenharia";
            Svc = new PermissionSyncService(Repo, Acc, new ConfigurationBuilder().Build(), NullLogger<PermissionSyncService>.Instance);
        }
        public Task<ResumoSincronizacao> Login(AutodeskUserInfo? u = null) => Svc.SincronizarUsuarioAsync(u ?? Joao, "tok", default);
        public int JoaoId => Repo.Users.Single(x => x.Email.Equals("joao@na.com", StringComparison.OrdinalIgnoreCase)).Id;
    }

    // ----------------------------------------------------------------------------- planner ----
    private static EstadoVinculo E(bool? ativo, bool temPerfil = false, bool configurado = false, bool adminDesativou = false) =>
        new(1, 1, ativo, temPerfil, configurado, adminDesativou);

    [Fact]
    public void Planner_Regras()
    {
        static IReadOnlyList<OperacaoVinculo> p(EstadoVinculo e, bool presenteNoAcc, bool projetoAtivoNoNa, bool listagemCompleta, bool existePerfilPadrao) =>
            PermissionSyncPlanner.Planejar(e, presenteNoAcc, projetoAtivoNoNa, listagemCompleta, existePerfilPadrao);
        Assert.Equal(new[] { OperacaoVinculo.Adicionar, OperacaoVinculo.AtribuirPerfilPadrao }, p(E(null), true, true, true, true));
        Assert.Equal(new[] { OperacaoVinculo.Adicionar }, p(E(null, temPerfil: true), true, true, true, true));
        Assert.Equal(new[] { OperacaoVinculo.Adicionar }, p(E(null, configurado: true), true, true, true, true)); // admin já configurou/limpou
        Assert.Equal(new[] { OperacaoVinculo.Reativar }, p(E(false, temPerfil: true), true, true, true, true));
        Assert.Empty(p(E(false, adminDesativou: true), true, true, true, true));      // decisão do admin prevalece
        Assert.Empty(p(E(true, temPerfil: true), true, true, true, true));
        Assert.Equal(new[] { OperacaoVinculo.Desativar }, p(E(true), false, true, true, true));
        Assert.Empty(p(E(true), false, true, listagemCompleta: false, true));          // falha Autodesk: nada muda
        Assert.Empty(p(E(false), false, true, true, true));
        Assert.Empty(p(E(null), true, projetoAtivoNoNa: false, true, true));           // fora/inativo no N&A
        Assert.Equal(new[] { OperacaoVinculo.Adicionar }, p(E(null), true, true, true, existePerfilPadrao: false));
    }

    // ----------------------------------------------------------------------- fluxo de login ----
    [Fact]
    public async Task PrimeiroLogin_CriaUsuario_SegundoNaoDuplica()
    {
        var m = new Mundo();
        await m.Login();
        Assert.Single(m.Repo.Users);
        Assert.Contains(m.Repo.Audit, a => a.Alvo == "USER_CREATED");
        await m.Login();
        await m.Login(Joao with { Email = "JOAO@NA.COM", Nome = "João S." }); // e-mail com outra caixa = mesmo usuário
        Assert.Single(m.Repo.Users);
        Assert.Single(m.Repo.Audit, a => a.Alvo == "USER_CREATED");
        Assert.Equal("João S.", m.Repo.Users[0].Nome);
    }

    [Fact]
    public async Task EmailComCaixaDiferente_SemAutodeskId_MesmoUsuario()
    {
        var m = new Mundo();
        await m.Login(new AutodeskUserInfo("", "Maria@NA.com", "Maria"));
        await m.Login(new AutodeskUserInfo("SUB-MARIA", "maria@na.com", "Maria"));
        Assert.Single(m.Repo.Users);
        Assert.Equal("SUB-MARIA", m.Repo.Users[0].Acc); // passa a ter a identidade externa preferencial
    }

    [Fact]
    public async Task TresProjetosAcc_TresVinculos_ComSomenteLeitura_IgnorandoForaDoNa()
    {
        var m = new Mundo();
        m.Acc.Projetos = new[] { P1, P2, P3, FORA, INATIVO_NA };
        var r = await m.Login();
        Assert.Equal(new[] { 1, 2, 3 }, m.Repo.Vinculos.Where(v => v.U == m.JoaoId && v.Ativo).Select(v => v.P).OrderBy(x => x));
        Assert.Equal(3, m.Repo.PerfisUP.Count(x => x.U == m.JoaoId && x.Perfil == 10));
        Assert.DoesNotContain(m.Repo.PerfisUP, x => x.P is null);            // nunca global
        Assert.Equal((3, 3, 2), (r.Vinculados, r.PerfisPadraoAtribuidos, r.IgnoradosForaDoNa));
        Assert.Equal(3, m.Repo.Audit.Count(a => a.Alvo == "USER_PROJECT_ADDED"));
        Assert.Equal(3, m.Repo.Audit.Count(a => a.Alvo == "DEFAULT_PROFILE_ASSIGNED"));
    }

    [Fact]
    public async Task ProjetoComEngenharia_NaoESobrescrito()
    {
        var m = new Mundo();
        m.Acc.Projetos = new[] { P1 };
        await m.Login();
        // Admin troca Somente Leitura → Engenharia no CHS001.
        m.Repo.PerfisUP.RemoveAll(x => x.U == m.JoaoId && x.P == 1);
        m.Repo.PerfisUP.Add((m.JoaoId, 1, 20));
        m.Repo.Audit.Add(new(m.JoaoId, 1, "PROFILE_CHANGED", "GLOBAL"));
        await m.Login();
        await m.Login();
        Assert.Equal(new[] { 20 }, m.Repo.PerfisUP.Where(x => x.U == m.JoaoId && x.P == 1).Select(x => x.Perfil));
    }

    [Fact]
    public async Task AdminRemoveuTodosOsPerfis_SomenteLeituraNaoVoltaSozinho()
    {
        var m = new Mundo();
        m.Acc.Projetos = new[] { P1 };
        await m.Login();
        m.Repo.PerfisUP.Clear();
        m.Repo.Audit.Add(new(m.JoaoId, 1, "PROFILE_CHANGED", P1));
        await m.Login();
        Assert.Empty(m.Repo.PerfisUP);
    }

    [Fact]
    public async Task GanhouProjetoNovo_CriaVinculoESomenteLeitura()
    {
        var m = new Mundo();
        m.Acc.Projetos = new[] { P1 };
        await m.Login();
        m.Acc.Projetos = new[] { P1, P4 };
        var r = await m.Login();
        Assert.Contains(m.Repo.Vinculos, v => v.U == m.JoaoId && v.P == 4 && v.Ativo);
        Assert.Contains(m.Repo.PerfisUP, x => x.U == m.JoaoId && x.P == 4 && x.Perfil == 10);
        Assert.Equal((1, 1), (r.Vinculados, r.PerfisPadraoAtribuidos));
    }

    [Fact]
    public async Task PerdeuProjeto_Inativo_SemApagarNada_EReentradaReativa()
    {
        var m = new Mundo();
        m.Acc.Projetos = new[] { P1, P2 };
        await m.Login();
        m.Repo.Overrides.Add((m.JoaoId, 2, "production.managerial.rnc.view", "DENY"));

        m.Acc.Projetos = new[] { P1 };
        var r = await m.Login();
        Assert.Equal(1, r.Desativados);
        Assert.Contains(m.Repo.Vinculos, v => v.U == m.JoaoId && v.P == 2 && !v.Ativo);
        Assert.Contains(m.Repo.PerfisUP, x => x.U == m.JoaoId && x.P == 2);            // perfil preservado
        Assert.Contains(m.Repo.Overrides, o => o.U == m.JoaoId && o.P == 2);           // exceção preservada
        Assert.Contains(m.Repo.Audit, a => a.Alvo == "USER_PROJECT_DEACTIVATED" && a.P == 2);

        m.Acc.Projetos = new[] { P1, P2 };
        r = await m.Login();
        Assert.Equal((1, 0), (r.Reativados, r.PerfisPadraoAtribuidos));                // reativa sem reatribuir
        Assert.Contains(m.Repo.Vinculos, v => v.U == m.JoaoId && v.P == 2 && v.Ativo);
        Assert.Single(m.Repo.PerfisUP, x => x.U == m.JoaoId && x.P == 2);
        Assert.Single(m.Repo.Vinculos, v => v.U == m.JoaoId && v.P == 2);             // não duplica linha
    }

    [Fact]
    public async Task FalhaAutodesk_NaoDesativaNenhumProjeto()
    {
        var m = new Mundo();
        m.Acc.Projetos = new[] { P1, P2, P3 };
        await m.Login();

        m.Acc.Projetos = Array.Empty<string>();
        m.Acc.Completa = false; // hubs/projetos falharam (HTTP, timeout, token expirado)
        var r = await m.Login();
        Assert.False(r.ListagemCompleta);
        Assert.Equal(0, r.Desativados);
        Assert.Equal(3, m.Repo.Vinculos.Count(v => v.U == m.JoaoId && v.Ativo));

        // Parcial: um hub falhou — o que veio pode entrar, mas nada é desativado.
        m.Acc.Projetos = new[] { P1, P4 };
        r = await m.Login();
        Assert.Equal(0, r.Desativados);
        Assert.Equal(4, m.Repo.Vinculos.Count(v => v.U == m.JoaoId && v.Ativo));
    }

    [Fact]
    public async Task AdminDesativouManualmente_LoginNaoReativa()
    {
        var m = new Mundo();
        m.Acc.Projetos = new[] { P1 };
        await m.Login();
        var v = m.Repo.Vinculos.Single(x => x.U == m.JoaoId && x.P == 1);
        m.Repo.Vinculos.Remove(v); m.Repo.Vinculos.Add(v with { Ativo = false });
        m.Repo.Audit.Add(new(m.JoaoId, 1, "USER_PROJECT_DEACTIVATED", "ADMIN"));
        var r = await m.Login();
        Assert.Equal(0, r.Reativados);
        Assert.Contains(m.Repo.Vinculos, x => x.U == m.JoaoId && x.P == 1 && !x.Ativo);
    }

    [Fact]
    public async Task SemPerfilPadrao_NaoAtribuiNada_ENuncaMasterAdmin()
    {
        var m = new Mundo();
        m.Repo.Perfis.Remove(10); // "Somente Leitura" inexistente/inativo
        m.Acc.Projetos = new[] { P1 };
        await m.Login();
        Assert.Empty(m.Repo.PerfisUP);
        Assert.Single(m.Repo.Vinculos);
    }

    // ------------------------------------------------------------------ sincronização do projeto ----
    [Fact]
    public async Task SyncDoProjeto_MembrosNovosReativadosEDesativados()
    {
        var m = new Mundo();
        m.Acc.Projetos = new[] { P1 };
        await m.Login(); // João já vinculado ao CHS001
        await m.Login(new AutodeskUserInfo("SUB-ANA", "ana@na.com", "Ana"));
        var anaId = m.Repo.Users.Single(u => u.Email == "ana@na.com").Id;

        m.Acc.Membros = new[] { new MembroAcc("SUB-JOAO", "JOAO@na.com", "João"), new MembroAcc(null, "novo@cliente.com", "Novo") };
        var projeto = m.Repo.Projetos[0];
        var r = await m.Svc.SincronizarProjetoAsync(projeto, "tok-admin", porUserId: 99, porEmail: "admin@na.com", default);

        Assert.Equal(3, m.Repo.Users.Count);                           // João não duplicou; "novo" foi criado
        Assert.Equal((2, 1, 1, 1), (r.Encontrados, r.UsuariosCriados, r.Vinculados, r.Desativados));
        Assert.Contains(m.Repo.Vinculos, v => v.U == anaId && v.P == 1 && !v.Ativo);   // Ana saiu do projeto no ACC
        var novo = m.Repo.Users.Single(u => u.Email == "novo@cliente.com");
        Assert.Contains(m.Repo.PerfisUP, x => x.U == novo.Id && x.P == 1 && x.Perfil == 10);
        Assert.Null(novo.UltimoAcesso);                                 // criado pela sync, nunca entrou
        Assert.NotNull(m.Repo.Resumos.SingleOrDefault());
    }

    [Fact]
    public async Task SyncDoProjeto_FalhaAutodesk_LancaENaoAlteraNada()
    {
        var m = new Mundo();
        m.Acc.Projetos = new[] { P1 };
        await m.Login();
        var antes = m.Repo.Vinculos.ToList();
        m.Acc.FalharMembros = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => m.Svc.SincronizarProjetoAsync(m.Repo.Projetos[0], "tok", 99, "admin@na.com", default));
        Assert.Equal(antes, m.Repo.Vinculos);
        Assert.Empty(m.Repo.Resumos);
    }

    [Fact]
    public void ParserDeMembrosAcc_IgnoraSemEmailEDeletados_EDeduplicaPorEmail()
    {
        var json = System.Text.Json.JsonDocument.Parse("""
            [{"email":"a@na.com","name":"A","autodeskId":"SA","status":"active"},
             {"email":"A@NA.com","firstName":"A","lastName":"B","status":"pending"},
             {"email":"x@na.com","status":"deleted"},
             {"name":"sem email"}]
            """).RootElement.EnumerateArray().ToList();
        var membros = MembrosAccParser.Parse(json);
        var a = Assert.Single(membros);
        Assert.Equal("a@na.com", a.Email, ignoreCase: true);
    }

    // -------------------------------------------------------------------------------- fakes ----
    internal sealed class FakeAcc : IAccMembershipSource
    {
        public string[] Projetos { get; set; } = Array.Empty<string>();
        public bool Completa { get; set; } = true;
        public MembroAcc[] Membros { get; set; } = Array.Empty<MembroAcc>();
        public bool FalharMembros { get; set; }
        public Task<ProjetosAcc> ProjetosDoUsuarioAsync(string accessToken, CancellationToken ct) =>
            Task.FromResult(new ProjetosAcc(Projetos.ToHashSet(StringComparer.OrdinalIgnoreCase), Completa, Completa ? null : "falha"));
        public Task<IReadOnlyList<MembroAcc>> MembrosDoProjetoAsync(string projectIdAcc, string accessToken, CancellationToken ct) =>
            FalharMembros ? throw new HttpRequestException("403", null, System.Net.HttpStatusCode.Forbidden) : Task.FromResult<IReadOnlyList<MembroAcc>>(Membros);
    }

    internal sealed record UserRow(int Id, string? Acc, string Email, string? Nome, DateTime? UltimoAcesso);
    internal sealed record VinculoRow(int U, int P, bool Ativo);
    internal sealed record AuditRow(int? U, int? P, string Alvo, string? Ref);

    /// <summary>Repositório em memória com a MESMA semântica do SQL (SqlPermissionSyncRepository).</summary>
    internal sealed class FakeSyncRepository : IPermissionSyncRepository
    {
        public List<ProjetoNa> Projetos { get; } = new();
        public Dictionary<int, string> Perfis { get; } = new();
        public List<UserRow> Users { get; } = new();
        public List<VinculoRow> Vinculos { get; } = new();
        public List<(int U, int? P, int Perfil)> PerfisUP { get; } = new();
        public List<(int U, int? P, string Key, string Effect)> Overrides { get; } = new();
        public List<AuditRow> Audit { get; } = new();
        public List<ResumoSincronizacao> Resumos { get; } = new();

        public Task<IReadOnlyList<ProjetoNa>> ProjetosNaAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ProjetoNa>>(Projetos);

        public Task<UsuarioUpsert> UpsertUsuarioAsync(string? acc, string email, string? nome, bool registrarAcesso, int? porUserId, string? porEmail, string origem, CancellationToken ct)
        {
            var u = (!string.IsNullOrWhiteSpace(acc) ? Users.FirstOrDefault(x => x.Acc == acc) : null)
                    ?? Users.FirstOrDefault(x => x.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
            if (u is null)
            {
                u = new UserRow(Users.Count + 1, string.IsNullOrWhiteSpace(acc) ? null : acc, email, nome, registrarAcesso ? DateTime.UtcNow : null);
                Users.Add(u);
                Audit.Add(new(u.Id, null, "USER_CREATED", origem));
                return Task.FromResult(new UsuarioUpsert(u.Id, true));
            }
            var i = Users.IndexOf(u);
            Users[i] = u with
            {
                Acc = u.Acc ?? (string.IsNullOrWhiteSpace(acc) ? null : acc),
                Email = Users.Any(o => o.Id != u.Id && o.Email.Equals(email, StringComparison.OrdinalIgnoreCase)) ? u.Email : email,
                Nome = string.IsNullOrWhiteSpace(nome) ? u.Nome : nome,
                UltimoAcesso = registrarAcesso ? DateTime.UtcNow : u.UltimoAcesso,
            };
            return Task.FromResult(new UsuarioUpsert(u.Id, false));
        }

        public Task<int?> PerfilPadraoIdAsync(string nome, CancellationToken ct) =>
            Task.FromResult(Perfis.Where(p => p.Value == nome).Select(p => (int?)p.Key).FirstOrDefault());

        public Task<IReadOnlyList<EstadoVinculo>> EstadosAsync(IReadOnlyCollection<(int UserId, int ProjetoId)> pares, int? vinculosDoUsuario, int? vinculosDoProjeto, CancellationToken ct)
        {
            var todos = pares.ToHashSet();
            foreach (var v in Vinculos.Where(v => v.U == vinculosDoUsuario || v.P == vinculosDoProjeto)) todos.Add((v.U, v.P));
            var lista = todos.Select(x =>
            {
                var v = Vinculos.SingleOrDefault(y => y.U == x.UserId && y.P == x.ProjetoId);
                var ultimoStatus = Audit.LastOrDefault(a => a.U == x.UserId && a.P == x.ProjetoId &&
                    a.Alvo is "USER_PROJECT_ADDED" or "USER_PROJECT_REACTIVATED" or "USER_PROJECT_DEACTIVATED");
                return new EstadoVinculo(x.UserId, x.ProjetoId, v?.Ativo,
                    PerfisUP.Any(p => p.U == x.UserId && p.P == x.ProjetoId),
                    Audit.Any(a => a.U == x.UserId && a.P == x.ProjetoId && a.Alvo is "PROFILE_CHANGED" or "DEFAULT_PROFILE_ASSIGNED" or "USER_PROFILE"),
                    ultimoStatus is { Alvo: "USER_PROJECT_DEACTIVATED", Ref: "ADMIN" });
            }).ToList();
            return Task.FromResult<IReadOnlyList<EstadoVinculo>>(lista);
        }

        public Task AplicarAsync(IReadOnlyList<OperacaoPlanejada> ops, int? perfilPadraoId, int? porUserId, string? porEmail, string origem, CancellationToken ct)
        {
            foreach (var op in ops)
            {
                var v = Vinculos.SingleOrDefault(y => y.U == op.UserId && y.P == op.ProjetoId);
                switch (op.Operacao)
                {
                    case OperacaoVinculo.Adicionar when v is null:
                        Vinculos.Add(new(op.UserId, op.ProjetoId, true)); Audit.Add(new(op.UserId, op.ProjetoId, "USER_PROJECT_ADDED", origem)); break;
                    case OperacaoVinculo.Reativar when v is { Ativo: false }:
                        Vinculos.Remove(v); Vinculos.Add(v with { Ativo = true }); Audit.Add(new(op.UserId, op.ProjetoId, "USER_PROJECT_REACTIVATED", origem)); break;
                    case OperacaoVinculo.Desativar when v is { Ativo: true }:
                        Vinculos.Remove(v); Vinculos.Add(v with { Ativo = false }); Audit.Add(new(op.UserId, op.ProjetoId, "USER_PROJECT_DEACTIVATED", origem)); break;
                    case OperacaoVinculo.AtribuirPerfilPadrao when perfilPadraoId is int perfil && !PerfisUP.Any(p => p.U == op.UserId && p.P == op.ProjetoId):
                        PerfisUP.Add((op.UserId, op.ProjetoId, perfil)); Audit.Add(new(op.UserId, op.ProjetoId, "DEFAULT_PROFILE_ASSIGNED", origem)); break;
                }
            }
            return Task.CompletedTask;
        }

        public Task RegistrarResumoAsync(int projetoId, int? porUserId, string? porEmail, ResumoSincronizacao resumo, CancellationToken ct)
        { Resumos.Add(resumo); return Task.CompletedTask; }
        public Task<ResumoSincronizacao?> UltimoResumoAsync(int projetoId, CancellationToken ct) => Task.FromResult(Resumos.LastOrDefault());
        public Task<IReadOnlyList<MembroProjetoView>> MembrosAsync(int projetoId, CancellationToken ct) => throw new NotSupportedException();
    }
}
