using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Reports;
using APS.WebApp.Services.Reports.Dashboards;

namespace NA_Connect.Planning.Tests;

/// <summary>
/// Hotfix 29/09/2026 (HTTP 503 / timeout): todo statement que lê PmoLdpSnapshot.Tabela com @projectId parametrizado
/// precisa de PmoLdpSnapshot.OpcaoPlano (OPTION (RECOMPILE)) no statement EXTERNO. Sem ele, BML011.013 (Tipo 1,
/// 12.870 cargas) passava de 90 s; com ele, dezenas de ms. O teste verifica o SQL REAL gerado por cada consumidor
/// (não só texto de arquivo) e falha se surgir consumidor novo não coberto.
/// </summary>
public class PmoLdpSnapshotPlanoTests
{
    /// <summary>SQL efetivo de cada consumidor conhecido (o mesmo texto enviado ao SqlClient).</summary>
    public static IEnumerable<object[]> Consumidores() => new[]
    {
        new object[] { "ProductionDashboards (LDP/Controle Documental)", string.Join("\n", ProductionDashboards.Todos.Values.Select(d => d.Sql).Where(s => s.Contains(PmoLdpSnapshot.Tabela)).Distinct()) },
        new object[] { "PmoExecutiveData (Gestão à Vista/Financeiro/Status)", PmoExecutiveData.Query },
        new object[] { "PmoPlanningFactsProvider", PmoPlanningFactsProvider.LdpSnapshotSql },
        new object[] { "LdpDetailsReportRepository", LdpDetailsReportRepository.BaseQuery },
        new object[] { "CdeDocumentListRepository", CdeDocumentListRepository.Query("ID_Registro") },
        new object[] { "ProductionDocumentsReportRepository", ProductionDocumentsReportRepository.Query },
        new object[] { "DocumentControlReportRepository", DocumentControlReportRepository.Query },
        new object[] { "LdpDocumentosAtuaisRepository (Envio governado pela LDP)", APS.WebApp.Services.UploadGovernado.LdpDocumentosAtuaisRepository.Query },
    };

    [Theory]
    [MemberData(nameof(Consumidores))]
    public void TodoStatementQueLeOSnapshotTemOPlanoMitigado(string consumidor, string batch)
    {
        // O snapshot e o hint não contêm ';' — o batch pode ser separado em statements com segurança.
        var statements = batch.Split(';').Where(s => s.Contains(PmoLdpSnapshot.Tabela)).ToList();
        Assert.True(statements.Count > 0, $"{consumidor}: não lê o snapshot (lista de consumidores desatualizada?).");
        foreach (var st in statements)
        {
            int fimSnapshot = st.LastIndexOf(PmoLdpSnapshot.Tabela, StringComparison.Ordinal) + PmoLdpSnapshot.Tabela.Length;
            int hint = st.IndexOf(PmoLdpSnapshot.OpcaoPlano, fimSnapshot, StringComparison.Ordinal);
            Assert.True(hint >= 0, $"{consumidor}: statement lê PmoLdpSnapshot.Tabela sem {PmoLdpSnapshot.OpcaoPlano} no statement externo.");
        }
    }

    [Fact]
    public void GestaoAVista_HintSoNoStatementDoSnapshot()
    {
        var statements = PmoExecutiveData.Query.Split(';');
        Assert.All(statements.Where(s => !s.Contains(PmoLdpSnapshot.Tabela)), s => Assert.DoesNotContain(PmoLdpSnapshot.OpcaoPlano, s));
        Assert.Single(statements, s => s.Contains(PmoLdpSnapshot.OpcaoPlano));
    }

    [Fact]
    public void Snapshot_SemHintDentroDaDerivedTable_ERegraInalterada()
    {
        Assert.DoesNotContain("OPTION", PmoLdpSnapshot.Tabela, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("OPTION (RECOMPILE)", PmoLdpSnapshot.OpcaoPlano);
        // Regras preservadas: Tipo 2 = MAX(Data_Sincronizacao); Tipo 1 = bloco após a última lacuna > 30 s.
        Assert.Contains("L.Data_Sincronizacao = (SELECT MAX(T2.Data_Sincronizacao)", PmoLdpSnapshot.Tabela);
        Assert.Contains($"WHERE G.Gap > {PmoLdpSnapshot.GapSegundosNovaCarga}", PmoLdpSnapshot.Tabela);
    }

    [Fact]
    public void UniversoDocumentalExcluiAT_MasPlanejamentoMantemAtividades()
    {
        var somenteDocumentos = LdpItemRules.SqlIsDocument("LdpAtual");

        Assert.Contains(somenteDocumentos, ProductionDocumentsReportRepository.Query);
        Assert.Contains(somenteDocumentos, DocumentControlReportRepository.Query);
        Assert.Contains(somenteDocumentos, LdpDetailsReportRepository.BaseQuery);
        Assert.Contains(somenteDocumentos, CdeDocumentListRepository.Query("ID_Registro"));
        Assert.Contains(somenteDocumentos, APS.WebApp.Services.UploadGovernado.LdpDocumentosAtuaisRepository.Query);
        Assert.Contains(somenteDocumentos,
            string.Join("\n", ProductionDashboards.Todos.Values.Select(d => d.Sql).Where(s => s.Contains(PmoLdpSnapshot.Tabela))));

        // Planejamento/HH/custo usa todas as linhas da LDP, inclusive AT.
        Assert.DoesNotContain(somenteDocumentos, PmoPlanningFactsProvider.LdpSnapshotSql);
        // Gestão mantém AT no payload econômico e classifica semanticamente em memória.
        Assert.DoesNotContain(somenteDocumentos, PmoExecutiveData.Query);
    }

    [Fact]
    public void NenhumConsumidorNovoDoSnapshotFicouForaDoContrato()
    {
        var raiz = new DirectoryInfo(AppContext.BaseDirectory);
        while (raiz != null && !File.Exists(Path.Combine(raiz.FullName, "AccIntegration.WebApp.csproj"))) raiz = raiz.Parent;
        Assert.NotNull(raiz);
        var conhecidos = new[]
        {
            "PmoLdpSnapshot.cs", "ProductionDashboards.cs", "PmoExecutiveData.cs", "PmoPlanningFactsProvider.cs", "LdpDetailsReportRepository.cs",
            "CdeDocumentListRepository.cs", "ProductionDocumentsReportRepository.cs", "ProductionReportRepositories.cs", "UploadGovernado.cs",
            // Não executa SQL com o snapshot: só lê do texto quais colunas a LDP atual expõe (catálogo dinâmico da Auditoria de Qualidade).
            "LdpColumnCatalog.cs",
        };
        var consumidores = new[] { "Services", "Controllers", "Areas" }
            .Select(d => Path.Combine(raiz!.FullName, d)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => File.ReadAllText(f).Contains("PmoLdpSnapshot.Tabela"))
            .Select(Path.GetFileName).ToList();
        var novos = consumidores.Except(conhecidos).ToList();
        Assert.True(novos.Count == 0, "Consumidor novo de PmoLdpSnapshot.Tabela sem cobertura do contrato de plano: " + string.Join(", ", novos));
    }
}
