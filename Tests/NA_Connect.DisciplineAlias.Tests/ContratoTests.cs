using System.Reflection;
using System.Text.RegularExpressions;
using APS.WebApp.Controllers;
using APS.WebApp.Services.DisciplineAlias;
using APS.WebApp.Services.Permissions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.DisciplineAlias.Tests;

public sealed class ContratoTests
{
    private static string Sql(string arquivo) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Sql", arquivo));
    private static readonly Regex Objeto = new(@"\b(?:INTO|FROM|UPDATE|JOIN|TABLE|REFERENCES|OBJECT_ID\(N')\s*([A-Za-z_\[\]]+)\.([A-Za-z_\[\]]+)", RegexOptions.IgnoreCase);

    public static IEnumerable<object[]> ComandosDoStore() => typeof(SqlSiglaDisciplinaStore)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.Name.StartsWith("Sql"))
        .Select(f => new object[] { f.Name });

    [Theory]
    [MemberData(nameof(ComandosDoStore))]
    public void Store_so_acessa_o_schema_normalizador(string campo)
    {
        var sql = (string)typeof(SqlSiglaDisciplinaStore).GetField(campo)!.GetRawConstantValue()!;
        var objetos = Objeto.Matches(sql).Select(m => m.Groups[1].Value.Trim('[', ']')).ToList();
        Assert.NotEmpty(objetos);
        Assert.All(objetos, s => Assert.Equal("normalizador", s, ignoreCase: true));
        Assert.DoesNotMatch(new Regex(@"\b(DROP|ALTER|TRUNCATE|MERGE)\b", RegexOptions.IgnoreCase), sql);
    }

    [Fact]
    public void Migracao_up_e_aditiva_idempotente_e_sem_seed()
    {
        var up = Sql("20261019_001_NormalizadorSiglas_UP.sql");
        Assert.DoesNotMatch(new Regex(@"\b(DROP|ALTER|TRUNCATE|DELETE|UPDATE|MERGE|INSERT)\b", RegexOptions.IgnoreCase), up);
        Assert.All(Objeto.Matches(up).Select(m => m.Groups[1].Value), s => Assert.Equal("normalizador", s, ignoreCase: true));
        foreach (var t in new[] { "Importacao", "SiglaDisciplina", "SiglaDisciplinaAuditoria" })
            Assert.Contains($"IF OBJECT_ID(N'normalizador.{t}', N'U') IS NULL", up);
        Assert.Contains("IF SCHEMA_ID(N'normalizador') IS NULL", up);
        Assert.Contains("UNIQUE (SiglaChave, ContextoChave)", up);
    }

    [Fact]
    public void Migracao_down_so_remove_objetos_proprios_e_protege_dados()
    {
        var down = Sql("20261019_001_NormalizadorSiglas_DOWN.sql");
        var drops = Regex.Matches(down, @"DROP\s+(TABLE|SCHEMA)\s+([\w.]+)", RegexOptions.IgnoreCase).Select(m => m.Groups[2].Value).ToList();
        Assert.Equal(["normalizador.SiglaDisciplinaAuditoria", "normalizador.SiglaDisciplina", "normalizador.Importacao", "normalizador"], drops);
        Assert.Contains("@ConfirmoExportacao bit = 0", down);
        Assert.Contains("Rollback recusado", down);
        Assert.DoesNotMatch(new Regex(@"\bALTER\b|\bdbo\.", RegexOptions.IgnoreCase), down);
    }

    [Fact]
    public void Edicao_exige_permissao_de_configuracao_existente_e_leitura_aceita_painel()
    {
        RequirePermissionAttribute Attr(string metodo) => typeof(DisciplineAliasController).GetMethod(metodo)!.GetCustomAttribute<RequirePermissionAttribute>()!;
        foreach (var m in new[] { "Criar", "Atualizar", "DefinirAtivo", "Excluir", "Previa", "Confirmar" })
            Assert.Equal(["settings.projects.edit"], Attr(m).Keys);
        Assert.Contains("settings.projects.view", Attr("Listar").Keys);
        Assert.Contains("reports.gestao.view", Attr("Ativas").Keys);
        Assert.True(Attr("Ativas").AnyProject);
    }

    private sealed class Permissoes(CurrentPermissionUser? usuario) : IPermissionService
    {
        public bool Enforced => true;
        public Task<PermissionCheck> CheckAsync(IReadOnlyCollection<string> anyOfKeys, string? projectIdAcc, CancellationToken ct, bool anyProject = false) =>
            Task.FromResult(PermissionCheck.Allowed);
        public Task<EffectivePermissions> GetEffectiveAsync(string? projectIdAcc, CancellationToken ct) => throw new NotSupportedException();
        public Task<CurrentPermissionUser?> GetCurrentUserAsync(CancellationToken ct) => Task.FromResult(usuario);
        public Task<int?> ResolverProjetoIdAsync(string? projectIdAcc, CancellationToken ct) => Task.FromResult<int?>(null);
    }

    private static DisciplineAliasController Controller(MemoriaStore store, CurrentPermissionUser? usuario) =>
        new(new SiglaDisciplinaService(store), new Permissoes(usuario), NullLogger<DisciplineAliasController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private static readonly CurrentPermissionUser Eu = new(1, "acc", "teste@fixture.local", "Teste", false);

    [Fact]
    public async Task Controller_sem_migracao_responde_503_com_instalado_false()
    {
        var r = Assert.IsType<ObjectResult>(await Controller(new MemoriaStore { Instalado = false }, Eu).Listar(default));
        Assert.Equal(503, r.StatusCode);
        Assert.Contains("instalado = False", r.Value!.ToString());
    }

    [Fact]
    public async Task Controller_mapeia_duplicada_para_409_invalida_para_400_e_sem_sessao_401()
    {
        var store = new MemoriaStore();
        var c = Controller(store, Eu);
        Assert.IsType<OkObjectResult>(await c.Criar(new SalvarRegraSigla("XA", "Teste", null, null, true, 0), default));
        Assert.IsType<ConflictObjectResult>(await c.Criar(new SalvarRegraSigla(" xa", "Outro", null, null, true, 0), default));
        Assert.IsType<BadRequestObjectResult>(await c.Criar(new SalvarRegraSigla("", "Teste", null, null, true, 0), default));
        Assert.IsType<UnauthorizedObjectResult>(await Controller(store, null).Criar(new SalvarRegraSigla("XB", "T", null, null, true, 0), default));
        Assert.Equal("teste@fixture.local", store.Regras.Single().CriadoPor);
    }
}
