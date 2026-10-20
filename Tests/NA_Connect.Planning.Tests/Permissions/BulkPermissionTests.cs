using APS.WebApp.Services.Permissions;
using APS.WebApp.Services.Permissions.Bulk;
using APS.WebApp.Services.Permissions.Sync;

namespace NA_Connect.Planning.Tests.Permissions;

/// <summary>Operações administrativas em lote: perfis em vários usuários/projetos e cópia de acesso entre projetos.</summary>
public class BulkPermissionTests
{
    private const string CHS = "b.aaaaaaaa-0000-0000-0000-000000000001", BML = "b.aaaaaaaa-0000-0000-0000-000000000002",
        CON = "b.aaaaaaaa-0000-0000-0000-000000000003";
    private const int MASTER = 1, ADMIN = 2, LEITURA = 3, ENG = 4, PROD = 5;
    private const int JOAO = 10, MARIA = 11, ANA = 12, EU = 99;

    private sealed class Mundo
    {
        public FakeBulkRepo Repo { get; } = new();
        public FakePermissionRepository Perms { get; } = new();
        public PermissionSyncTests.FakeSyncRepository Sync { get; } = new();
        public BulkPermissionService Svc { get; }

        public Mundo()
        {
            Perms.Perfis.AddRange(new[]
            {
                new PermProfile(MASTER, "MasterAdmin", null, true, true, true, Array.Empty<string>()),
                new PermProfile(ADMIN, "Administrador", null, true, false, false, new[] { "home.view" }),
                new PermProfile(LEITURA, "Somente Leitura", null, true, false, false, new[] { "home.view" }),
                new PermProfile(ENG, "Engenharia", null, true, false, false, new[] { "cde.view" }),
                new PermProfile(PROD, "Controle de Produção", null, true, false, false, new[] { "production.view" }),
            });
            Sync.Projetos.AddRange(new[] { new ProjetoNa(1, CHS, "CHS001", true), new ProjetoNa(2, BML, "BML011.022", true), new ProjetoNa(3, CON, "CON006", true) });
            foreach (var (id, nome) in new[] { (JOAO, "João"), (MARIA, "Maria"), (ANA, "Ana"), (EU, "Eu") }) Repo.Usuarios[id] = new UsuarioResumo(id, $"{nome.ToLower()}@na.com", nome);
            Svc = new BulkPermissionService(Repo, Perms, Sync);
        }

        public void Vinculo(int u, int p, bool ativo = true, params int[] perfis)
        {
            Repo.Existentes.Add((u, p));
            if (ativo) Repo.Ativos.Add((u, p));
            foreach (var perfil in perfis) Repo.Perfis.Add((u, p, perfil));
        }

        public IEnumerable<int> PerfisDe(int u, int p) => Repo.Perfis.Where(x => x.U == u && x.P == p).Select(x => x.Perfil).OrderBy(x => x);
    }

    private static BulkPerfisRequest Perfis(int[] users, string[] projs, int[] perfis, string mode = "ADD") => new(users, projs, perfis, mode);

    [Fact]
    public async Task TresUsuarios_UmProjeto_Add_PreservaExistentes_ENaoDuplica()
    {
        var m = new Mundo();
        m.Vinculo(JOAO, 1, true, LEITURA);
        m.Vinculo(MARIA, 1, true, ENG);          // já possui
        m.Vinculo(ANA, 1, true);
        var previa = await m.Svc.PreverPerfisAsync(Perfis(new[] { JOAO, MARIA, ANA }, new[] { CHS }, new[] { ENG }), EU, default);
        Assert.Equal((3, 2, 1, 0), (previa.Combinacoes, previa.Alterar, previa.JaPossui, previa.Ignoradas));
        Assert.Empty(m.Repo.Auditoria); // prévia nunca grava

        var r = await m.Svc.AplicarPerfisAsync(Perfis(new[] { JOAO, MARIA, ANA }, new[] { CHS }, new[] { ENG }), EU, "eu@na.com", default);
        Assert.True(r.Sucesso);
        Assert.Equal((2, 1, 0, 0), (r.Alterados, r.JaCorretos, r.Ignorados, r.Falhas));
        Assert.Equal(new[] { LEITURA, ENG }, m.PerfisDe(JOAO, 1));   // ADD preserva Somente Leitura
        Assert.Equal(new[] { ENG }, m.PerfisDe(MARIA, 1));           // sem duplicar
        Assert.Equal(new[] { ENG }, m.PerfisDe(ANA, 1));
        Assert.Equal(BulkPermissionService.OpPerfisAdicionar, m.Repo.Auditoria.Single(a => a.Alvo.StartsWith("BULK")).Alvo);
        Assert.Equal(2, m.Repo.Auditoria.Count(a => a.Alvo == "PROFILE_CHANGED")); // auditoria individual por par alterado
    }

    [Fact]
    public async Task Replace_SubstituiSomenteOsPerfisDoProjeto()
    {
        var m = new Mundo();
        m.Vinculo(JOAO, 1, true, LEITURA, ADMIN);
        m.Vinculo(JOAO, 2, true, LEITURA);
        var r = await m.Svc.AplicarPerfisAsync(Perfis(new[] { JOAO }, new[] { CHS }, new[] { ENG, PROD }, "REPLACE"), EU, "eu@na.com", default);
        Assert.Equal(1, r.Alterados);
        Assert.Equal(new[] { ENG, PROD }, m.PerfisDe(JOAO, 1));
        Assert.Equal(new[] { LEITURA }, m.PerfisDe(JOAO, 2));   // outro projeto intocado
        Assert.Contains(r.Plano.Avisos, a => a.Contains("SUBSTITUIR"));
        Assert.Equal(BulkPermissionService.OpPerfisSubstituir, m.Repo.Auditoria.Single(a => a.Alvo.StartsWith("BULK")).Alvo);
    }

    [Fact]
    public async Task UmUsuario_TresProjetos_E_TresUsuarios_TresProjetos_CriaOuReativaVinculoInterno()
    {
        var m = new Mundo();
        m.Vinculo(JOAO, 1); m.Vinculo(JOAO, 2);                  // sem vínculo em CON006
        var r1 = await m.Svc.AplicarPerfisAsync(Perfis(new[] { JOAO }, new[] { CHS, BML, CON }, new[] { ENG }), EU, "eu@na.com", default);
        Assert.Equal((3, 0, 0), (r1.Alterados, r1.JaCorretos, r1.Ignorados));
        Assert.Equal(new[] { ENG }, m.PerfisDe(JOAO, 3));
        Assert.Contains((JOAO, 3), m.Repo.Ativos);                 // vínculo interno criado pelo administrador
        Assert.Equal(BulkVinculo.Criar, r1.Plano.Itens.Single(i => i.ProjetoId == 3).Vinculo);
        Assert.Equal((1, 0, 3), (r1.VinculosCriados, r1.VinculosReativados, r1.PerfisAdicionados));

        m.Vinculo(MARIA, 1, true, ENG); m.Vinculo(MARIA, 2); m.Vinculo(MARIA, 3);
        m.Vinculo(ANA, 3, false);                                   // vínculo INATIVO → reativado
        var previa = await m.Svc.PreverPerfisAsync(Perfis(new[] { JOAO, MARIA, ANA }, new[] { CHS, BML, CON }, new[] { ENG }), EU, default);
        Assert.Equal(9, previa.Combinacoes);
        Assert.Equal((5, 4, 0), (previa.Alterar, previa.JaPossui, previa.Ignoradas)); // João 3 já possui; Maria 1 já possui + 2; Ana 3 (2 criar + 1 reativar)
        var r = await m.Svc.AplicarPerfisAsync(Perfis(new[] { JOAO, MARIA, ANA }, new[] { CHS, BML, CON }, new[] { ENG }), EU, "eu@na.com", default);
        Assert.Equal((5, 4, 0, 0), (r.Alterados, r.JaCorretos, r.Ignorados, r.Falhas));
        Assert.Equal((2, 1), (r.VinculosCriados, r.VinculosReativados));
        Assert.Equal(new[] { ENG }, m.PerfisDe(ANA, 3));
    }

    [Fact]
    public async Task OverridesNaoSaoAlteradosPeloLoteDePerfis_NemEmReplace()
    {
        var m = new Mundo();
        m.Vinculo(JOAO, 1, true, LEITURA);
        m.Repo.Overrides[(JOAO, 1, "production.managerial.rnc.view")] = "DENY";
        await m.Svc.AplicarPerfisAsync(Perfis(new[] { JOAO }, new[] { CHS }, new[] { ENG }, "REPLACE"), EU, "eu@na.com", default);
        Assert.Equal("DENY", m.Repo.Overrides[(JOAO, 1, "production.managerial.rnc.view")]);
        Assert.DoesNotContain(m.Repo.Auditoria, a => a.Alvo == "OVERRIDE_CHANGED");
    }

    [Fact]
    public async Task MasterAdmin_NaoPodeSerAplicadoEmLote()
    {
        var m = new Mundo();
        m.Vinculo(JOAO, 1);
        var ex = await Assert.ThrowsAsync<BulkValidationException>(() => m.Svc.PreverPerfisAsync(Perfis(new[] { JOAO }, new[] { CHS }, new[] { MASTER }), EU, default));
        Assert.Contains("MasterAdmin", ex.Message);
        await Assert.ThrowsAsync<BulkValidationException>(() => m.Svc.AplicarPerfisAsync(Perfis(new[] { JOAO }, new[] { CHS }, new[] { ENG, MASTER }), EU, "eu@na.com", default));
        Assert.Empty(m.Repo.Perfis);
    }

    [Fact]
    public async Task AdministradorNaoAlteraOProprioAcessoNoLote()
    {
        var m = new Mundo();
        m.Vinculo(EU, 1); m.Vinculo(JOAO, 1);
        var r = await m.Svc.AplicarPerfisAsync(Perfis(new[] { EU, JOAO }, new[] { CHS }, new[] { ADMIN }), EU, "eu@na.com", default);
        Assert.Equal((1, 1), (r.Alterados, r.Ignorados));
        Assert.Empty(m.PerfisDe(EU, 1));
        Assert.Equal(BulkPermissionPlanner.MotivoProprioAcesso, r.Plano.Itens.Single(i => i.UserId == EU).Motivo);
    }

    [Fact]
    public async Task FalhaNoMeio_NaoDeixaEstadoParcial_ERetornaFalhas()
    {
        var m = new Mundo();
        m.Vinculo(JOAO, 1); m.Vinculo(MARIA, 1); m.Vinculo(ANA, 1);
        m.Repo.FalharNoPar = 2; // falha ao gravar o 2º par
        var r = await m.Svc.AplicarPerfisAsync(Perfis(new[] { JOAO, MARIA, ANA }, new[] { CHS }, new[] { ENG }), EU, "eu@na.com", default);
        Assert.False(r.Sucesso);
        Assert.Equal((0, 3), (r.Alterados, r.Falhas));
        Assert.NotNull(r.Erro);
        Assert.Empty(m.Repo.Perfis);       // nem o 1º par ficou gravado
        Assert.Empty(m.Repo.Auditoria);
    }

    // ------------------------------------------------------------- copiar acesso entre projetos ----
    private static BulkCopiaRequest Copia(string[] destinos, bool perfis = true, bool overrides = false, string mode = "ADD") =>
        new(JOAO, CHS, destinos, perfis, overrides, mode);

    [Fact]
    public async Task CopiaPerfis_ParaDestinosComVinculo_SemOverridesPorPadrao()
    {
        var m = new Mundo();
        m.Vinculo(JOAO, 1, true, ENG, PROD);
        m.Repo.Overrides[(JOAO, 1, "production.managerial.rnc.view")] = "DENY";
        m.Vinculo(JOAO, 2, true, LEITURA);
        // CON006 sem vínculo: o administrador escolheu o destino → o vínculo interno é criado com os perfis copiados
        var r = await m.Svc.AplicarCopiaAsync(Copia(new[] { BML, CON }), EU, "eu@na.com", default);
        Assert.Equal((2, 0, 0), (r.Alterados, r.JaCorretos, r.Ignorados));
        Assert.Equal(new[] { LEITURA, ENG, PROD }, m.PerfisDe(JOAO, 2));
        Assert.Equal(new[] { ENG, PROD }, m.PerfisDe(JOAO, 3));
        Assert.Contains((JOAO, 3), m.Repo.Ativos);
        Assert.False(m.Repo.Overrides.ContainsKey((JOAO, 2, "production.managerial.rnc.view"))); // overrides só se pedido
        Assert.Equal(BulkPermissionService.OpCopia, m.Repo.Auditoria.Single(a => a.Alvo.StartsWith("BULK")).Alvo);
    }

    [Fact]
    public async Task CopiaOverrides_SomenteQuandoSolicitado_AddNaoInverteExistente_ReplaceIguala()
    {
        var m = new Mundo();
        m.Vinculo(JOAO, 1, true, ENG);
        m.Repo.Overrides[(JOAO, 1, "production.managerial.rnc.view")] = "DENY";
        m.Repo.Overrides[(JOAO, 1, "cde.docs.download")] = "ALLOW";
        m.Vinculo(JOAO, 2, true, ENG);
        m.Repo.Overrides[(JOAO, 2, "cde.docs.download")] = "DENY";   // conflito
        m.Repo.Overrides[(JOAO, 2, "robot.view")] = "ALLOW";         // só no destino

        var add = await m.Svc.AplicarCopiaAsync(Copia(new[] { BML }, perfis: true, overrides: true), EU, "eu@na.com", default);
        Assert.Equal(1, add.Alterados);
        Assert.Equal("DENY", m.Repo.Overrides[(JOAO, 2, "production.managerial.rnc.view")]);  // adicionado
        Assert.Equal("DENY", m.Repo.Overrides[(JOAO, 2, "cde.docs.download")]);    // ADD não inverte o existente
        Assert.Equal("ALLOW", m.Repo.Overrides[(JOAO, 2, "robot.view")]);          // ADD mantém o que é só do destino
        Assert.Contains(add.Plano.Avisos, a => a.Contains("ALLOW/DENY"));

        var rep = await m.Svc.AplicarCopiaAsync(Copia(new[] { BML }, perfis: false, overrides: true, mode: "REPLACE"), EU, "eu@na.com", default);
        Assert.Equal(1, rep.Alterados);
        Assert.Equal("ALLOW", m.Repo.Overrides[(JOAO, 2, "cde.docs.download")]);   // igual à origem
        Assert.False(m.Repo.Overrides.ContainsKey((JOAO, 2, "robot.view")));         // removido (não existe na origem)
        Assert.Contains(m.Repo.Auditoria, a => a.Alvo == "OVERRIDE_CHANGED");

        var denovo = await m.Svc.PreverCopiaAsync(Copia(new[] { BML }, perfis: true, overrides: true, mode: "REPLACE"), EU, default);
        Assert.Equal(BulkStatus.JaPossui, Assert.Single(denovo.Itens).Status);     // idempotente
    }

    [Fact]
    public async Task Copia_ValidaOrigemDestinoEConteudo()
    {
        var m = new Mundo();
        m.Vinculo(JOAO, 1, true, ENG);
        await Assert.ThrowsAsync<BulkValidationException>(() => m.Svc.PreverCopiaAsync(Copia(new[] { CHS }), EU, default));        // só a origem
        await Assert.ThrowsAsync<BulkValidationException>(() => m.Svc.PreverCopiaAsync(Copia(new[] { BML }, false, false), EU, default));
        await Assert.ThrowsAsync<BulkValidationException>(() => m.Svc.PreverCopiaAsync(Copia(new[] { "b.nao-existe" }), EU, default));
        await Assert.ThrowsAsync<BulkValidationException>(() => m.Svc.PreverPerfisAsync(Perfis(new[] { JOAO }, new[] { CHS }, new[] { ENG }, "TALVEZ"), EU, default));
    }

    [Fact]
    public void Planner_AddReplaceIgnorar()
    {
        var e = new EstadoPar(1, 1, true, new HashSet<int> { 3 }, new Dictionary<string, string>());
        Assert.Equal(new[] { 4 }, BulkPermissionPlanner.Planejar(e, false, BulkModo.Adicionar, new HashSet<int> { 3, 4 }, null).PerfisAdicionar);
        var rep = BulkPermissionPlanner.Planejar(e, false, BulkModo.Substituir, new HashSet<int> { 4 }, null);
        Assert.Equal(new[] { 4 }, rep.PerfisAdicionar);
        Assert.Equal(new[] { 3 }, rep.PerfisRemover);
        Assert.Equal(BulkStatus.JaPossui, BulkPermissionPlanner.Planejar(e, false, BulkModo.Adicionar, new HashSet<int> { 3 }, null).Status);
        var semVinculo = BulkPermissionPlanner.Planejar(e with { VinculoAtivo = false }, false, BulkModo.Adicionar, new HashSet<int> { 4 }, null);
        Assert.Equal((BulkStatus.Alterar, BulkVinculo.Criar), (semVinculo.Status, semVinculo.Vinculo));
        Assert.Equal(BulkVinculo.Reativar, BulkPermissionPlanner.Planejar(e with { VinculoAtivo = false, VinculoExiste = true }, false, BulkModo.Adicionar, new HashSet<int> { 3 }, null).Vinculo);
        Assert.Equal(BulkStatus.Ignorar, BulkPermissionPlanner.Planejar(e with { UsuarioAtivo = false }, false, BulkModo.Adicionar, new HashSet<int> { 4 }, null).Status);
        Assert.Equal(BulkStatus.Ignorar, BulkPermissionPlanner.Planejar(e, true, BulkModo.Adicionar, new HashSet<int> { 4 }, null).Status);
    }

    // ------------------------------------------- cadastro interno N&A em vários projetos (sem ACC) ----
    /// <summary>17 projetos ativos no N&A; o usuário N&A (AccUserId NULL, sem Autodesk) só tem vínculo no primeiro.</summary>
    private static (Mundo M, string[] Accs) DezesseteProjetos()
    {
        var m = new Mundo();
        m.Sync.Projetos.Clear();
        var accs = Enumerable.Range(1, 17).Select(i => $"b.{i:00000000}-0000-0000-0000-000000000000").ToArray();
        for (int i = 0; i < accs.Length; i++) m.Sync.Projetos.Add(new ProjetoNa(100 + i, accs[i], $"PRJ{i + 1:000}", true));
        m.Vinculo(ANA, 100, true, ADMIN);                             // 1 já correto
        return (m, accs);
    }

    [Fact]
    public async Task A_B_C_D_UsuarioSemAcc_UmJaCorreto_DezesseisAdicionar_SemIgnorados()
    {
        var (m, accs) = DezesseteProjetos();

        var previa = await m.Svc.PreverPerfisAsync(Perfis(new[] { ANA }, accs, new[] { ADMIN }), EU, default);
        Assert.Equal((17, 16, 1, 0), (previa.Combinacoes, previa.Alterar, previa.JaPossui, previa.Ignoradas));
        Assert.All(previa.Itens.Where(i => i.Status == BulkStatus.Alterar), i => Assert.Equal(BulkVinculo.Criar, i.Vinculo));
        Assert.DoesNotContain(previa.Itens, i => (i.Motivo ?? "").Contains("ACC", StringComparison.OrdinalIgnoreCase));

        var r = await m.Svc.AplicarPerfisAsync(Perfis(new[] { ANA }, accs, new[] { ADMIN }), EU, "eu@na.com", default);
        Assert.True(r.Sucesso);
        Assert.Equal((16, 1, 0, 0), (r.Alterados, r.JaCorretos, r.Ignorados, r.Falhas));
        Assert.Equal((16, 0, 16), (r.VinculosCriados, r.VinculosReativados, r.PerfisAdicionados));
        Assert.Equal(17, m.Repo.Ativos.Count(x => x.U == ANA));                            // UserProjects nos 17
        Assert.All(Enumerable.Range(100, 17), p => Assert.Equal(new[] { ADMIN }, m.PerfisDe(ANA, p)));
        Assert.Equal(16, m.Repo.Auditoria.Count(a => a.Alvo == "USER_PROJECT_ADDED"));
    }

    [Fact]
    public async Task E_H_MesmoLoteDeNovo_NadaDuplica_TodosJaCorretos()
    {
        var (m, accs) = DezesseteProjetos();
        await m.Svc.AplicarPerfisAsync(Perfis(new[] { ANA }, accs, new[] { ADMIN }), EU, "eu@na.com", default);
        var perfisAntes = m.Repo.Perfis.Count;

        var r = await m.Svc.AplicarPerfisAsync(Perfis(new[] { ANA }, accs, new[] { ADMIN }), EU, "eu@na.com", default);

        Assert.Equal((0, 17, 0), (r.Alterados, r.JaCorretos, r.Ignorados));
        Assert.Equal(perfisAntes, m.Repo.Perfis.Count);
        Assert.Equal(17, m.Repo.Ativos.Count(x => x.U == ANA));
    }

    [Fact]
    public async Task F_G_VinculoInativoReativado_PerfilAusenteCriado()
    {
        var (m, accs) = DezesseteProjetos();
        m.Vinculo(ANA, 101, ativo: false);                             // inativo, sem perfil

        var r = await m.Svc.AplicarPerfisAsync(Perfis(new[] { ANA }, accs.Take(2).ToArray(), new[] { ADMIN }), EU, "eu@na.com", default);

        Assert.Equal((1, 0, 1), (r.Alterados, r.VinculosCriados, r.VinculosReativados));
        Assert.Contains((ANA, 101), m.Repo.Ativos);
        Assert.Equal(new[] { ADMIN }, m.PerfisDe(ANA, 101));
        Assert.Contains(m.Repo.Auditoria, a => a.Alvo == "USER_PROJECT_REACTIVATED" && a.P == 101);
    }

    [Fact]
    public async Task UsuarioInativoNoNa_IgnoradoComMotivoReal()
    {
        var (m, accs) = DezesseteProjetos();
        m.Repo.Usuarios[ANA] = m.Repo.Usuarios[ANA] with { Ativo = false };

        var previa = await m.Svc.PreverPerfisAsync(Perfis(new[] { ANA }, accs, new[] { ADMIN }), EU, default);

        Assert.Equal(17, previa.Ignoradas);
        Assert.All(previa.Itens, i => Assert.Equal(BulkPermissionPlanner.MotivoUsuarioInativo, i.Motivo));
    }

    [Fact]
    public void J_FluxoDoLoteNaoDependeDeAutodesk()
    {
        // O serviço só recebe repositórios internos (perm/dbo.Projetos); nenhum cliente Autodesk/token no construtor.
        var tipos = typeof(BulkPermissionService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ToArray();
        Assert.Equal(new[] { "IBulkPermissionRepository", "IPermissionRepository", "IPermissionSyncRepository" }, tipos);
        Assert.DoesNotContain(typeof(IPermissionSyncRepository).GetMethods(), m => m.Name.Contains("Acc", StringComparison.Ordinal) && m.Name.Contains("Membro", StringComparison.Ordinal));
    }

    // -------------------------------------------------------------------------------- fake ----
    /// <summary>Em memória e ATÔMICO como a transação SQL: aplica numa cópia e só confirma se todos os pares passarem.</summary>
    internal sealed class FakeBulkRepo : IBulkPermissionRepository
    {
        public Dictionary<int, UsuarioResumo> Usuarios { get; } = new();
        public HashSet<(int U, int P)> Ativos { get; private set; } = new();
        /// <summary>Linhas de UserProjects existentes (ativas ou inativas).</summary>
        public HashSet<(int U, int P)> Existentes { get; private set; } = new();
        public HashSet<(int U, int P, int Perfil)> Perfis { get; private set; } = new();
        public Dictionary<(int U, int P, string K), string> Overrides { get; private set; } = new();
        public List<(int? U, int? P, string Alvo)> Auditoria { get; private set; } = new();
        public int? FalharNoPar { get; set; }

        public Task<EstadoLote> CarregarEstadoAsync(IReadOnlyCollection<int> userIds, IReadOnlyCollection<int> projetoIds, CancellationToken ct)
        {
            var pares = (from u in userIds.Distinct() from p in projetoIds.Distinct()
                         select new EstadoPar(u, p, Ativos.Contains((u, p)),
                             Perfis.Where(x => x.U == u && x.P == p).Select(x => x.Perfil).ToHashSet(),
                             Overrides.Where(kv => kv.Key.U == u && kv.Key.P == p).ToDictionary(kv => kv.Key.K, kv => kv.Value, StringComparer.Ordinal),
                             Existentes.Contains((u, p)) || Ativos.Contains((u, p)), !Usuarios.TryGetValue(u, out var us) || us.Ativo)).ToList();
            return Task.FromResult(new EstadoLote(pares, Usuarios.Where(kv => userIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value)));
        }

        public Task AplicarAsync(IReadOnlyList<(PlanoPar Plano, EstadoPar Antes)> planos, AuditoriaLote auditoria, CancellationToken ct)
        {
            var perfis = new HashSet<(int, int, int)>(Perfis);
            var ativos = new HashSet<(int, int)>(Ativos);
            var existentes = new HashSet<(int, int)>(Existentes);
            var overrides = new Dictionary<(int, int, string), string>(Overrides);
            var audit = new List<(int?, int?, string)>(Auditoria);
            int n = 0;
            foreach (var (plano, _) in planos)
            {
                if (plano.Status != BulkStatus.Alterar) continue;
                if (++n == FalharNoPar) throw new InvalidOperationException("falha simulada no meio do lote");
                if (plano.Vinculo is not null)
                {
                    ativos.Add((plano.UserId, plano.ProjetoId)); existentes.Add((plano.UserId, plano.ProjetoId));
                    audit.Add((plano.UserId, plano.ProjetoId, plano.Vinculo == BulkVinculo.Reativar ? "USER_PROJECT_REACTIVATED" : "USER_PROJECT_ADDED"));
                }
                foreach (var id in plano.PerfisRemover) perfis.Remove((plano.UserId, plano.ProjetoId, id));
                foreach (var id in plano.PerfisAdicionar) perfis.Add((plano.UserId, plano.ProjetoId, id));
                if (plano.PerfisAdicionar.Count + plano.PerfisRemover.Count > 0) audit.Add((plano.UserId, plano.ProjetoId, "PROFILE_CHANGED"));
                foreach (var k in plano.OverridesRemover) overrides.Remove((plano.UserId, plano.ProjetoId, k));
                foreach (var (k, e) in plano.OverridesGravar) overrides[(plano.UserId, plano.ProjetoId, k)] = e;
                if (plano.OverridesGravar.Count + plano.OverridesRemover.Count > 0) audit.Add((plano.UserId, plano.ProjetoId, "OVERRIDE_CHANGED"));
            }
            audit.Add((null, null, auditoria.Alvo));
            Perfis = perfis; Overrides = overrides; Auditoria = audit; Ativos = ativos; Existentes = existentes; // "commit"
            return Task.CompletedTask;
        }
    }
}
