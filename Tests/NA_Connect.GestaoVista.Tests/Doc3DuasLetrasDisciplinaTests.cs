using APS.WebApp.Services.Planning;
using NA_Connect.Services.Coordination;

namespace NA_Connect.GestaoVista.Tests;

/// <summary>
/// REGRA DEFINITIVA (15/10/2026): disciplina = 2 primeiros caracteres do DOC3, via Norma do PRÓPRIO projeto.
/// Casos reais auditados no BML011.013 (Norma 2 "BML011"), inclusive o cadastro errado CIM=3D MODELING.
/// </summary>
public class Doc3DuasLetrasDisciplinaTests
{
    // Recorte fiel da Norma 2 (BML011): siglas de 2 caracteres + variantes, com o erro CIM=3D MODELING.
    private static readonly Dictionary<string, string> Norma2 = new()
    {
        ["CI"] = "CIVIL", ["CI0"] = "CIVIL", ["CI1"] = "CIVIL", ["CIM"] = "3D MODELING",
        ["3D"] = "DIGITAL SYSTEMS", ["3D0"] = "DIGITAL SYSTEMS", ["3D1"] = "DIGITAL SYSTEMS", ["3DM"] = "DIGITAL SYSTEMS",
        ["ME"] = "MECHANICAL", ["ME7"] = "MECHANICAL", ["MEM"] = "MECHANICAL",
        ["PX"] = "PROCESS", ["PX3"] = "PROCESS", ["PX5"] = "PROCESS",
        ["PI"] = "PIPING", ["PI2"] = "PIPING", ["PIM"] = "PIPING",
    };

    // Cadastro global auditado: ID 8 DIGITAL SYSTEMS e ID 19 3D MODELING com Nome_PT/Nome_EN "DIGITAL SYSTEMS".
    private static readonly DisciplineStandardDto[] Global =
    {
        new(8, "DIGITAL SYSTEMS", "SISTEMAS DIGITAIS", "DIGITAL SYSTEMS", true, new[] { "DIGITAL SYSTEM", "DIGITAL SYSTEMS", "SISTEMAS DIGITAIS" }),
        new(19, "3D MODELING", "DIGITAL SYSTEMS", "DIGITAL SYSTEMS", true, new[] { "3D MODELING" }),
        new(17, "CIVIL", "CIVIL", "CIVIL", true, Array.Empty<string>()),
        new(10, "MECHANICAL", "MECÂNICA", "MECHANICAL", true, Array.Empty<string>()),
        new(22, "MECHANICA", "MECÂNICA", "MECHANICA", true, Array.Empty<string>()),
        new(1, "PROCESS", "PROCESSO", "PROCESS", true, Array.Empty<string>()),
        new(7, "PIPING", "TUBULAÇÃO", "PIPING", true, Array.Empty<string>()),
    };

    private static IReadOnlyDictionary<string, string> Canonicas => DisciplineStandardRepository.CanonicalNameMap(Global);

    [Theory]
    [InlineData("CIM", "CIVIL")] [InlineData("CI0", "CIVIL")] [InlineData("CI1", "CIVIL")]
    [InlineData("3D0", "DIGITAL SYSTEMS")] [InlineData("3D1", "DIGITAL SYSTEMS")] [InlineData("3DM", "DIGITAL SYSTEMS")]
    [InlineData("ME7", "MECHANICAL")] [InlineData("MEM", "MECHANICAL")]
    [InlineData("PX3", "PROCESS")] [InlineData("PX5", "PROCESS")]
    [InlineData("PI2", "PIPING")] [InlineData("PIM", "PIPING")]
    public void DisciplinaEhSempreOsDoisPrimeirosCaracteresDoDoc3(string doc3, string esperado)
    {
        var r = CoordinationDisciplineResolver.Resolve(doc3, 2, Norma2, Canonicas);
        Assert.True(r.Matched, r.Diagnostic);
        Assert.Equal(esperado, r.Discipline);
        Assert.Equal(doc3[..2], r.MatchedCode);
    }

    [Fact]
    public void CimNuncaViraTresDModelingPeloTerceiroCaractere()
    {
        var r = CoordinationDisciplineResolver.Resolve(" cim ", 2, Norma2, Canonicas);
        Assert.Equal("CIVIL", r.Discipline);
        Assert.Equal(CoordinationDisciplineSources.NormaDoisCaracteres, r.Source);
    }

    [Fact]
    public void NomePadraoVenceNomePtEnRepetidoNoCadastroGlobal()
    {
        // Antes: "DIGITAL SYSTEMS" era descartado como ambíguo (ID 8 x ID 19) e todo o 3D caía em SEM DISCIPLINA.
        Assert.Equal("DIGITAL SYSTEMS", Canonicas["DIGITAL SYSTEMS"]);
        Assert.Equal("3D MODELING", Canonicas["3D MODELING"]);
        // Ambiguidade no mesmo nível continua descartada, nunca escolhe o primeiro.
        Assert.False(Canonicas.ContainsKey("MECANICA"));
    }

    [Fact]
    public void Sf0DaNormaDoProjetoNaoPrecisaDePadronizacaoGlobal()
    {
        var mapa = new Dictionary<string, string> { ["SF"] = "SECURITY", ["SF0"] = "SECURITY" };
        var nomesGlobaisSemSecurity = new Dictionary<string, string> { ["SAFETY"] = "SAFETY" };
        var r = CoordinationDisciplineResolver.Resolve("SF0", 4, mapa, nomesGlobaisSemSecurity);
        Assert.True(r.Matched, r.Diagnostic);
        Assert.Equal("SECURITY", r.Discipline);
        Assert.Equal("SF", r.MatchedCode);
    }

    [Fact]
    public void MesmaSiglaPodeTerSignificadosDiferentesEmNormasDeProjetosDiferentes()
    {
        var normaA = new Dictionary<string, string> { ["SF"] = "SECURITY" };
        var normaB = new Dictionary<string, string> { ["SF"] = "SAFETY" };
        var global = new Dictionary<string, string> { ["SECURITY"] = "SAFETY" };
        var a = CoordinationDisciplineResolver.Resolve("SFM", 4, normaA, global);
        var b = CoordinationDisciplineResolver.Resolve("SF1", 5, normaB, global);
        Assert.True(a.Matched);
        Assert.True(b.Matched);
        Assert.Equal("SECURITY", a.Discipline);
        Assert.Equal("SAFETY", b.Discipline);
    }

    [Fact]
    public void PrefixoSemSiglaDeDuasLetrasEComDisciplinasDiferentesFicaSemDisciplinaComDiagnostico()
    {
        var norma = new Dictionary<string, string> { ["TU0"] = "PIPING", ["TUM"] = "TUBULAÇÃO" };
        var r = CoordinationDisciplineResolver.Resolve("TU1", 5, norma);
        Assert.False(r.Matched);
        Assert.Equal(CoordinationDisciplineSources.SemDisciplina, r.Source);
        Assert.Contains("cadastrar 'TU'", r.Diagnostic);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("C")]
    public void Doc3AusenteOuCurtoNuncaInventaDisciplina(string? doc3)
    {
        var r = CoordinationDisciplineResolver.Resolve(doc3, 2, Norma2, Canonicas);
        Assert.False(r.Matched); Assert.Null(r.Discipline);
    }

    [Fact]
    public void DocumentoResolvePelaTerceiraParteDoCodigo()
    {
        var d = new ProjectDoc3Disciplines(2, Norma2, Canonicas);
        Assert.Equal("CIVIL", d.Canonical("BML011.013-000-CIM-3D-001"));
        Assert.Equal("DIGITAL SYSTEMS", d.Canonical("BML011.013-000-3D0-AT-001"));
        Assert.Null(d.Canonical("SEM-CODIGO"));
        Assert.Null(d.Canonical(null));
    }

    [Fact]
    public void PrevistoDaLdpUsaODoc3AntesDoTextoDisciplineSemMudarHh()
    {
        var d = new ProjectDoc3Disciplines(2, Norma2, Canonicas);
        var rows = new List<LdpPlanRow>
        {
            new("BML011.013-000-CIM-3D-001", null, "CIVIL", 106.88m, 1000m),
            new("BML011.013-000-3D0-AT-001", null, "3D MODELING", 352m, 2000m),
            new("BML011.013-000-3D1-RE-001", null, "DIGITAL SYSTEM", 38m, 300m),
            new("SEM-DOC3", null, "MECHANICAL", 5m, null),
        };
        var index = ProjectDisciplineIndex.Build([], [], []);
        var cronograma = ProjectPlanningRules.Read("P", 1, [], "teste", _ => null);
        var f = LdpPlanningRules.Apply(cronograma, rows, index, d.Canonical);
        Assert.Equal(501.88m, f.PlannedHhTotal);
        Assert.Equal(106.88m, f.PlannedHhByDiscipline.Single(x => x.Discipline == "CIVIL").PlannedHh);
        Assert.Equal(390m, f.PlannedHhByDiscipline.Single(x => x.Discipline == "DIGITAL SYSTEMS").PlannedHh);
        Assert.Equal(5m, f.PlannedHhByDiscipline.Single(x => x.Discipline == "MECHANICAL").PlannedHh);
        Assert.DoesNotContain(f.PlannedHhByDiscipline, x => x.Discipline == "3D MODELING");
    }
}
