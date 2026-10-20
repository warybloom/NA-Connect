using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Reports;
using NA_Connect.Services.Coordination;
namespace NA_Connect.GestaoVista.Tests;
public class PmoCanonicalFactsTests
{
    private static PlanningTask Row(int id, string wbs, bool summary, string? discipline, decimal? work, string? document = null)
        => new(id,id == 42 ? "0" : id.ToString(),wbs,summary,discipline,work,document);
    // Resolver de teste: só disciplina própria do Cronograma (equivalente ao caso sem match na LDP —
    // resolução via documento é responsabilidade de PmoPlanningFactsProvider, testada à parte).
    private static string? OwnDiscipline(PlanningTask t) => t.Discipline;
    [Fact]
    public void RollupsAreIndependentOfPhysicalOrderAndLeafSum()
    {
        var f = ProjectPlanningRules.Read("p",2,[Row(7,"1.1.1",false,"Piping",5),Row(42,"0",true,null,1000),
            Row(2,"1",true,null,1000),Row(3,"1.1",true,"Piping",600),Row(4,"1.2",true,null,399.9m)],"test",OwnDiscipline);
        Assert.Equal(1000m,f.PlannedHhTotal); Assert.Equal(600m,f.DisciplineTotal);
        Assert.Equal(399.9m,f.UndisciplinedHh); Assert.Equal(.1m,f.ReconciliationDifference);
        Assert.Equal("RESUMO_PROJETO",f.Source);
    }
    [Fact]
    public void IndependentPhasesCombineWithoutNestedSummaryDoubleCounting()
    {
        var f=ProjectPlanningRules.Read("p",2,[Row(20,"1",true,null,100),Row(21,"1.1",true,"Piping",40),
            Row(22,"1.1.1",true,"Piping",40),Row(23,"1.2",true,"Piping",60)],"test",OwnDiscipline);
        Assert.Equal(100m,f.DisciplineTotal); Assert.Equal(0m,f.ReconciliationDifference);
        Assert.Equal(new[]{21,23},Assert.Single(f.PlannedHhByDiscipline).SummaryIds);
    }
    [Fact]
    public void EngineeringActivityWithoutLdpMatchStillCountsTowardsPlannedTotal()
    {
        // Correção 2026-09-19: LDP não é mais "prova de existência" para HH Previsto — documento planejado
        // ainda sem correspondência na LDP (não cadastrado/renomeado/fase diferente) continua entrando,
        // desde que seja folha válida do cronograma. Ver ValidatedType1WithoutRootFallsBackToLeavesWith
        // RealDocumentOnly na história do arquivo para a regra anterior (revertida).
        var a=new PlanningTask(1,"a","1",false,"Piping",20,"SEM-MATCH-NA-LDP",new(1,1,null,true,false,"ALIAS"));
        var f=ProjectPlanningRules.Read("p",1,[a],"test",OwnDiscipline);
        Assert.Equal("FALLBACK_FOLHAS",f.Source); Assert.Equal(20m,f.PlannedHhTotal);
    }
    [Fact]
    public void ManagementDisciplineTaskIsExcludedFromPlannedTotal()
    {
        var a=new PlanningTask(1,"a","1",false,"Piping",20,null,new(1,1,null,true,false,"ALIAS"));
        var b=new PlanningTask(2,"b","2",false,"Management",30,null,new(1,2,null,true,false,"ALIAS"));
        var f=ProjectPlanningRules.Read("p",1,[a,b],"test",OwnDiscipline);
        Assert.Equal("FALLBACK_FOLHAS",f.Source); Assert.Equal(20m,f.PlannedHhTotal);
        Assert.Equal(30m,f.ManagementHh); Assert.Equal(1,f.ManagementTaskCount);
    }
    [Fact]
    public void TaskWithoutAnyDisciplineStillCountsSemDisciplinaIsNotGestao()
    {
        var a=new PlanningTask(1,"a","1",false,null,20,null,new(1,1,null,true,false,"ALIAS"));
        var f=ProjectPlanningRules.Read("p",1,[a],"test",OwnDiscipline);
        Assert.Equal(20m,f.PlannedHhTotal); Assert.Equal(0,f.ManagementTaskCount);
    }
    [Fact]
    public void ManagementSummaryWithChildrenIsSubtractedOnceNotTwice()
    {
        // Resumo "Management" com Work próprio (100) + dois filhos-folha (40+60) somando o mesmo total —
        // ManagementHh soma só as folhas (nunca o resumo), e a lista de disciplinas exibida nunca traz
        // Gestão/Gerenciamento, mesmo quando ele existe como resumo rotulado na árvore.
        var root = new PlanningTask(42,"0","0",true,null,300,null);
        var gestao = new PlanningTask(1,"1","1",true,"Management",100,null);
        var child1 = new PlanningTask(2,"2","1.1",false,null,40,null);
        var child2 = new PlanningTask(3,"3","1.2",false,null,60,null);
        var processo = new PlanningTask(4,"4","2",true,"Processo",200,null);
        var f = ProjectPlanningRules.Read("p",2,[root,gestao,child1,child2,processo],"test",OwnDiscipline);
        Assert.Equal("RESUMO_PROJETO",f.Source);
        Assert.Equal(200m,f.PlannedHhTotal);
        Assert.Equal(100m,f.ManagementHh);
        Assert.Equal(200m,f.DisciplineTotal);
        Assert.Equal(0m,f.UndisciplinedHh);
        Assert.Equal(0m,f.ReconciliationDifference);
        Assert.Equal("RECONCILIADO",f.Coverage);
        Assert.DoesNotContain(f.PlannedHhByDiscipline, g => g.Discipline is "MANAGEMENT" or "GERENCIAMENTO");
        Assert.Equal("PROCESSO", Assert.Single(f.PlannedHhByDiscipline).Discipline);
    }
    [Fact]
    public void Type1WithoutAnyHierarchyMetadataSumsValidNonSummaryTasksWithoutRequiringDocument()
    {
        // Tipo1 hoje não persiste resumo/parentesco (import descarta task.getSummary()) — ausência total de
        // metadados é o formato normal desse tipo, não um snapshot incompleto/legado. Correção 2026-09-19:
        // soma direta de Trabalho das tarefas não-resumo; vínculo com documento na LDP não é mais exigido —
        // só disciplina Gestão/Gerenciamento é excluída.
        var f=ProjectPlanningRules.Read("p",1,[Row(1,"1",false,"Piping",600),Row(2,"2",false,null,150)],
            "test",OwnDiscipline);
        Assert.Equal(750m,f.PlannedHhTotal); Assert.Equal("FALLBACK_TAREFAS_SEM_METADADOS",f.Source);
    }
    [Fact]
    public void Type1WithoutAnyHierarchyMetadataExcludesManagementTasks()
    {
        var f=ProjectPlanningRules.Read("p",1,[Row(1,"1",false,"Piping",600),Row(2,"2",false,"Gerenciamento",150)],
            "test",OwnDiscipline);
        Assert.Equal(600m,f.PlannedHhTotal); Assert.Equal("FALLBACK_TAREFAS_SEM_METADADOS",f.Source);
        Assert.Equal(150m,f.ManagementHh);
        // ManagementTaskCount é null aqui: sem metadado Tipo1, Metadata?.Version nunca é 1 (é null), então
        // a checagem pré-existente "versão de metadado incompatível" (tasks.Any(t=>t.Metadata?.Version!=1))
        // já zera esse campo por design, mesmo sem nenhum problema de versão real — pré-existente, não
        // alterada nesta correção.
        Assert.Null(f.ManagementTaskCount);
    }
    [Fact]
    public void Type1WithIncompatibleMetadataVersionStillCannotPretendToBeCompleteFallback()
    {
        // Diferente do caso acima: aqui HÁ metadado, mas de versão incompatível — sinal genuíno de
        // snapshot legado/misto, onde não se pode confiar nem nas tarefas não-resumo.
        var legacy=new PlanningTask(1,"a","1",false,null,600,null,new(2,1,null,true,false,"ALIAS"));
        var f=ProjectPlanningRules.Read("p",1,[legacy],"test",OwnDiscipline);
        Assert.Null(f.PlannedHhTotal); Assert.Equal("SEM_DADO",f.Source);
    }
    [Fact]
    public void InvalidOrMissingLeafWorkDoesNotBecomePartialTotal()
    {
        foreach(var invalid in new decimal?[]{null,-1}) {
            var f=ProjectPlanningRules.Read("p",2,[Row(1,"1",false,null,invalid),Row(2,"2",false,null,20)],
                "test",OwnDiscipline);
            Assert.Null(f.PlannedHhTotal);
        }
    }
    [Fact]
    public void UnresolvedDisciplineCannotBeRelabeledAsGeneral()
    {
        var root=new PlanningTask(1,"root","0",true,null,100,null,new(1,0,null,false,true,"DISCIPLINA_ALIAS_AMBIGUO"));
        var leaf=new PlanningTask(2,"leaf","1",false,null,100,null,new(1,1,"root",true,false,"DISCIPLINA_ALIAS_AMBIGUO"));
        var f=ProjectPlanningRules.Read("p",1,[leaf,root],"test",OwnDiscipline);
        Assert.Equal(100m,f.PlannedHhTotal); Assert.Null(f.UndisciplinedHh); Assert.Null(f.DisciplineTotal);
        Assert.Equal("INCOMPLETO",f.Coverage);
    }
    [Fact]
    public void MissingRootWorkAllowsValidLeafFallbackWithoutSummaries()
    {
        var f = ProjectPlanningRules.Read("p",2,[Row(42,"0",true,null,null),Row(5,"1",false,null,10)],
            "test",OwnDiscipline);
        Assert.Equal(10m,f.PlannedHhTotal);Assert.Equal("FALLBACK_FOLHAS",f.Source);
    }
    [Fact]
    public void ConflictingNestedDisciplinesAreNotRedistributed()
    {
        var f=ProjectPlanningRules.Read("p",2,[Row(42,"0",true,null,100),Row(3,"1",true,"A",100),Row(4,"1.1",true,"B",50)],"test",OwnDiscipline);
        Assert.Equal(100m,f.PlannedHhTotal); Assert.Null(f.DisciplineTotal); Assert.Equal("INCOMPLETO",f.Coverage);
    }
    [Fact]
    public void CancelledDocumentsNeverCountAsLateAndBaselineFallbackIsPreserved()
    {
        var d=new PmoDocumento("d","p","DOC",null,null,null,null,null,new DateTime(2026,1,1),null,null,null,"CANCELADO",null,null,null);
        var cancelled=PmoExecutiveRules.Completar(d,[],1,new DateTime(2026,9,16));
        Assert.Equal("CANCELADO",cancelled.StatusPrazo);
        Assert.Equal("ATRASADO",PmoExecutiveRules.Completar(d with {StatusCliente=null},[],1,new DateTime(2026,9,16)).StatusPrazo);
        Assert.Equal("SEM_DADO",PmoExecutiveRules.Completar(d with {StatusCliente=null,DataBaseline=null},[],1,new DateTime(2026,9,16)).StatusPrazo);
    }
}

/// <summary>
/// Document -> canonical discipline resolution shared by Planning (LDP cost) and Actual (Timesheet HH/cost).
/// Precedence: LDP N&amp;A exact -&gt; LDP Client exact -&gt; Cronograma exact -&gt; unresolved (never GERAL by
/// invention, never fuzzy/global). Ambiguous data is dropped, never guessed.
/// </summary>
public class DocumentDisciplineResolverTests
{
    [Fact]
    public void LdpDisciplineIsUsedRegardlessOfWhatTimesheetOriginallySaid()
    {
        var index = ProjectDisciplineIndex.Build([("D1","Mechanical")], [], []);
        var match = index.Resolve("D1");
        Assert.Equal("MECHANICAL", match.DisciplineKey);
        Assert.Equal(DocumentDisciplineSources.LdpNa, match.Source);
    }
    [Fact]
    public void BlankLdpDisciplineFallsThroughToCronograma()
    {
        var index = ProjectDisciplineIndex.Build([("D1", (string?)null)], [], [("D1","Piping")]);
        var match = index.Resolve("D1");
        Assert.Equal("PIPING", match.DisciplineKey); Assert.Equal(DocumentDisciplineSources.Cronograma, match.Source);
    }
    [Fact]
    public void NaMissClientHitUsesLdpClient()
    {
        var index = ProjectDisciplineIndex.Build([], [("C1","Electrical")], []);
        var match = index.Resolve("C1");
        Assert.Equal("ELECTRICAL", match.DisciplineKey); Assert.Equal(DocumentDisciplineSources.LdpClient, match.Source);
    }
    [Fact]
    public void LdpMissCronogramaHitUsesCronograma()
    {
        var index = ProjectDisciplineIndex.Build([], [], [("D9","Civil")]);
        var match = index.Resolve("D9");
        Assert.Equal("CIVIL", match.DisciplineKey); Assert.Equal(DocumentDisciplineSources.Cronograma, match.Source);
    }
    [Fact]
    public void NoSourceMatchIsUnresolvedNeverInventedAsGeneral()
    {
        var index = ProjectDisciplineIndex.Build([], [], []);
        var match = index.Resolve("UNKNOWN");
        Assert.Null(match.DisciplineKey); Assert.Equal(DocumentDisciplineSources.SemMatch, match.Source);
    }
    [Fact]
    public void ConflictingLdpAndCronogramaDisciplinesAreRegisteredButLdpStillPrevails()
    {
        var index = ProjectDisciplineIndex.Build([("D1","Mechanical")], [], [("D1","Piping")]);
        var conflict = Assert.Single(index.Conflicts);
        Assert.Equal("D1", conflict.DocumentCode); Assert.Equal("MECHANICAL", conflict.DisciplinaLdp); Assert.Equal("PIPING", conflict.DisciplinaCronograma);
        Assert.Equal("MECHANICAL", index.Resolve("D1").DisciplineKey);
    }
    [Theory]
    [InlineData("MECHANICAL")]
    [InlineData("Mechanical")]
    [InlineData(" mechanical ")]
    public void MechanicalCasingVariantsCollapseToOneKey(string raw)
    {
        var index = ProjectDisciplineIndex.Build([("D1", raw)], [], []);
        Assert.Equal("MECHANICAL", index.Resolve("D1").DisciplineKey);
    }
    [Theory]
    [InlineData("INFRASTRUCTURE")]
    [InlineData("Infrastructure")]
    public void InfrastructureCasingVariantsCollapseToOneKey(string raw)
    {
        var index = ProjectDisciplineIndex.Build([("D2", raw)], [], []);
        Assert.Equal("INFRASTRUCTURE", index.Resolve("D2").DisciplineKey);
    }
    [Fact]
    public void AmbiguousCodeAcrossRowsIsNeverGuessed()
    {
        var index = ProjectDisciplineIndex.Build([("D1","Mechanical"),("D1","Piping")], [], []);
        Assert.Null(index.Resolve("D1").DisciplineKey);
    }
}

/// <summary>
/// Regra OFICIAL definida 2026-09-22: disciplina do HH/Custo REALIZADO é resolvida SÓ por
/// Project_ID_ACC + Doc3 via a Norma de Coordenação do próprio projeto (nunca mais LDP/Cronograma/texto
/// livre do Timesheet). Ver Services/Coordination/CoordinationDisciplineResolver.cs.
/// </summary>
public class CoordinationDisciplineResolverTests
{
    private static readonly Dictionary<string, string> Mapa = new()
    {
        ["CV"] = "CIVIL", ["EL"] = "ELECTRICAL", ["3D"] = "DIGITAL SYSTEMS", ["MEM"] = "MEMORIAL",
    };
    [Fact]
    public void SuffixedSiglaFallsBackToBaseSigla()
    {
        var r = CoordinationDisciplineResolver.Resolve("CV3", 1, Mapa);
        Assert.True(r.Matched); Assert.Equal("CIVIL", r.Discipline);
        Assert.Equal(CoordinationDisciplineSources.NormaDoisCaracteres, r.Source);
    }
    [Fact]
    public void ExactSiglaMatchesDirectly()
    {
        var r = CoordinationDisciplineResolver.Resolve("CV", 1, Mapa);
        Assert.True(r.Matched); Assert.Equal("CIVIL", r.Discipline);
        Assert.Equal(CoordinationDisciplineSources.NormaExata, r.Source);
    }
    [Fact]
    public void OnlyTrailingNumericSuffixIsStrippedNeverAnInnerDigit()
    {
        // "3D1" -> base "3D" (dígito interno preservado), nunca "D".
        var r = CoordinationDisciplineResolver.Resolve("3D1", 1, Mapa);
        Assert.True(r.Matched); Assert.Equal("DIGITAL SYSTEMS", r.Discipline);
        Assert.Equal("3D", r.MatchedCode);
    }
    [Fact]
    public void NormaWithoutTwoCharSiglaUsesConsensusOfSameFirstTwoChars()
    {
        // Regra 15/10/2026: chave = "ME". Norma sem "ME", mas todas as siglas "ME*" concordam.
        var r = CoordinationDisciplineResolver.Resolve("MEM", 1, Mapa);
        Assert.True(r.Matched); Assert.Equal("MEMORIAL", r.Discipline); Assert.Equal("ME", r.MatchedCode);
        Assert.Equal(CoordinationDisciplineSources.NormaConsensoDoisCaracteres, r.Source);
    }
    [Fact]
    public void UnknownSiglaIsSemDisciplinaNeverInventedAsGeneral()
    {
        var r = CoordinationDisciplineResolver.Resolve("XX9", 1, Mapa);
        Assert.False(r.Matched); Assert.Null(r.Discipline);
        Assert.Equal(CoordinationDisciplineSources.SemDisciplina, r.Source);
    }
    [Fact]
    public void NullDoc3IsSemDisciplina()
    {
        var r = CoordinationDisciplineResolver.Resolve(null, 1, Mapa);
        Assert.False(r.Matched); Assert.Equal(CoordinationDisciplineSources.SemDisciplina, r.Source);
    }
    [Fact]
    public void ManagementSiglaIsNeverExcludedWhenMappedInTheNorma()
    {
        var mapaComGestao = new Dictionary<string, string>(Mapa) { ["GES"] = "GESTAO" };
        var r = CoordinationDisciplineResolver.Resolve("GES", 1, mapaComGestao);
        Assert.True(r.Matched); Assert.Equal("GESTAO", r.Discipline);
    }
    [Fact]
    public void DifferentProjectsUseDifferentNormsForTheSameSigla()
    {
        // Multi-projeto: a mesma sigla resolve para disciplinas diferentes conforme a Norma do PRÓPRIO
        // projeto — nunca cruza a norma de um projeto com o doc3 de outro.
        var normaProjetoA = new Dictionary<string, string> { ["CV"] = "CIVIL" };
        var normaProjetoB = new Dictionary<string, string> { ["CV"] = "CONTROLE DE VAZAO" };
        Assert.Equal("CIVIL", CoordinationDisciplineResolver.Resolve("CV", 1, normaProjetoA).Discipline);
        Assert.Equal("CONTROLE DE VAZAO", CoordinationDisciplineResolver.Resolve("CV", 2, normaProjetoB).Discipline);
    }
    [Theory]
    [InlineData(null, null, null, null, null, null)]
    [InlineData("CHS-06-CV-001-A", null, null, null, null, "CHS-06-CV-001-A")]
    [InlineData(null, "CHS", "06", "CV", "001", "CHS-06-CV-001")]
    [InlineData(null, "CHS", "", "CV", null, "CHS-CV")]
    public void DocumentCodeReusesCanonicalOrRebuildsOnlyFromExistingSegments(
        string? documentoCodigo, string? d1, string? d2, string? d3, string? d4, string? esperado)
        => Assert.Equal(esperado, CoordinationDisciplineResolver.ResolveDocumentCode(documentoCodigo, d1, d2, d3, d4, null));
}

/// <summary>
/// Aggregation-only: entries here are already resolved (Discipline/DisciplineSource), matching how
/// PmoActualFactsProvider calls ProjectActualRules.Read after resolving each Timesheet lançamento via
/// CoordinationDisciplineResolver. CustoInformado is the canonical real cost; CustoCalculado never
/// substitutes it.
/// </summary>
public class ProjectActualRulesTests
{
    private static ActualEntry Entry(long id, string? document, string discipline, string source,
        decimal? hh, decimal? custoInformado, decimal? custoCalculado, string normalizedSigla = "")
        => new(id, document, discipline, source, hh, custoInformado, custoCalculado, normalizedSigla);
    [Fact]
    public void CanonicalDisciplineIsUsedForAggregation()
    {
        var f = ProjectActualRules.Read("p", [Entry(1,"D1","MECHANICAL",CoordinationDisciplineSources.NormaExata,10,100,90)], 1);
        Assert.Equal("MECHANICAL", Assert.Single(f.ByDiscipline).Discipline);
    }
    [Fact]
    public void HhAndInformedCostDoNotDuplicateAcrossEntriesForTheSameDocument()
    {
        var entries = new[] {
            Entry(1,"D1","PIPING",CoordinationDisciplineSources.NormaExata,10,100,90),
            Entry(2,"D1","PIPING",CoordinationDisciplineSources.NormaExata,20,200,190),
        };
        var f = ProjectActualRules.Read("p", entries, 1);
        Assert.Equal(30m, f.Total.Hh); Assert.Equal(300m, f.Total.Cost);
        Assert.Equal(30m, Assert.Single(f.ByDocument).Values.Hh);
    }
    [Fact]
    public void SemDisciplinaEntryStaysInTotalNeverExcluded()
    {
        var f = ProjectActualRules.Read("p", [Entry(1,null,CoordinationDisciplineResolver.SemDisciplinaLabel,CoordinationDisciplineSources.SemDisciplina,5,50,45,"XX")], 1);
        Assert.Equal(5m, f.Total.Hh);
        Assert.Equal(CoordinationDisciplineResolver.SemDisciplinaLabel, Assert.Single(f.ByDiscipline).Discipline);
    }
    [Fact]
    public void GestaoDisciplineHhStaysInTotalNeverExcluded()
    {
        // Regra oficial 2026-09-22: TODAS as horas entram, inclusive a disciplina Gestão/Gerenciamento
        // quando mapeada na Norma — nunca mais excluída do HH Realizado.
        var f = ProjectActualRules.Read("p", [Entry(1,"D1","GESTAO",CoordinationDisciplineSources.NormaExata,40,400,380)], 1);
        Assert.Equal(40m, f.Total.Hh);
        Assert.Equal("GESTAO", Assert.Single(f.ByDiscipline).Discipline);
    }
    [Fact]
    public void InformedCostAndCalculatedCostAreNeverConfused()
    {
        var f = ProjectActualRules.Read("p", [Entry(1,"D1","PIPING",CoordinationDisciplineSources.NormaExata,1,null,69.14772727m)], 1);
        Assert.Null(f.Total.Cost);
        Assert.Equal(69.14772727m, f.Total.KnownCalculatedCostSubtotal);
        Assert.Equal(1, f.Total.MissingCost);
    }
    [Fact]
    public void EmptyActualsAreUnknownNotZero() => Assert.Null(ProjectActualRules.Read("p", [], 1).Total.Hh);
    [Fact]
    public void SumOfByDisciplineEqualsTotalIncludingSemDisciplina()
    {
        var entries = new[] {
            Entry(1,"D1","CIVIL",CoordinationDisciplineSources.NormaExata,10,null,10),
            Entry(2,"D2","ELECTRICAL",CoordinationDisciplineSources.NormaSufixoNumerico,20,null,20),
            Entry(3,null,CoordinationDisciplineResolver.SemDisciplinaLabel,CoordinationDisciplineSources.SemDisciplina,5,null,5,"XX"),
        };
        var f = ProjectActualRules.Read("p", entries, 1);
        Assert.Equal(35m, f.Total.Hh);
        Assert.Equal(35m, f.ByDiscipline.Sum(d => d.Values.Hh ?? 0));
        Assert.Equal(1, f.MatchedExact); Assert.Equal(1, f.MatchedBySuffix); Assert.Equal(1, f.Unmatched);
    }
    [Fact]
    public void UnmappedSiglaDiagnosticAggregatesEntriesAndHh()
    {
        var entries = new[] {
            Entry(1,null,CoordinationDisciplineResolver.SemDisciplinaLabel,CoordinationDisciplineSources.SemDisciplina,3,null,3,"XX"),
            Entry(2,null,CoordinationDisciplineResolver.SemDisciplinaLabel,CoordinationDisciplineSources.SemDisciplina,7,null,7,"XX"),
        };
        var f = ProjectActualRules.Read("p", entries, 1);
        var diag = Assert.Single(f.UnmappedSiglas);
        Assert.Equal("XX", diag.Sigla); Assert.Equal(2, diag.Entries); Assert.Equal(10m, diag.Hh);
    }
    [Fact]
    public void EntriesWithoutDocumentGoToSemDocumentoBucketButStayInTotalAndByDiscipline()
    {
        var entries = new[] {
            Entry(1,"D1","CIVIL",CoordinationDisciplineSources.NormaExata,10,null,10),
            Entry(2,null,"CIVIL",CoordinationDisciplineSources.NormaExata,7,null,7), // sem documento, mas com disciplina resolvida
        };
        var f = ProjectActualRules.Read("p", entries, 1);
        Assert.Equal(17m, f.Total.Hh);
        Assert.Equal(7m, f.SemDocumento.Hh);
        Assert.Equal(10m, Assert.Single(f.ByDocument).Values.Hh);
        Assert.Equal(17m, f.ByDiscipline.Sum(d => d.Values.Hh ?? 0));
        // SUM(por documento) + SEM DOCUMENTO = Total
        Assert.Equal(f.Total.Hh, f.ByDocument.Sum(d => d.Values.Hh ?? 0) + (f.SemDocumento.Hh ?? 0));
    }
    [Fact]
    public void NoEntryWithoutDocumentMeansSemDocumentoIsUnknownNotZero()
        => Assert.Null(ProjectActualRules.Read("p", [Entry(1,"D1","CIVIL",CoordinationDisciplineSources.NormaExata,10,null,10)], 1).SemDocumento.Hh);
}
