using System.Text.Json;
using APS.WebApp.Models;
using APS.WebApp.Services;
using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Reports;
using NA_Connect.Cronograma.Tests.TestHelpers;
using net.sf.mpxj;

namespace NA_Connect.Cronograma.Tests;

public class Tipo1AnalyticsTests
{
    [Theory]
    [InlineData("Disciplina")]
    [InlineData("Discipline")]
    [InlineData("  discipline\n")]
    public void AliasIsResolvedWithoutFixedField(string alias)
    {
        var p = new ProjectFile();
        ProjectFileTestHelper.DefinirAlias(p, TaskField.TEXT19, alias);
        Assert.Equal(TaskField.TEXT19, CronogramaTipo1Analytics.ResolveDiscipline(p).Field);
    }
    [Fact]
    public void VisibleTitleResolvesActualField()
    {
        var p = new ProjectFile();
        ProjectFileTestHelper.DefinirTituloColuna(p, "Entry", TaskField.TEXT27, "Discipline");
        Assert.Equal(TaskField.TEXT27, CronogramaTipo1Analytics.ResolveDiscipline(p).Field);
    }
    [Fact]
    public void AmbiguousAliasesNeverSelectFirstOrFallBack()
    {
        var p = new ProjectFile();
        ProjectFileTestHelper.DefinirAlias(p, TaskField.TEXT8, "Disciplina");
        ProjectFileTestHelper.DefinirAlias(p, TaskField.TEXT9, "Discipline");
        ProjectFileTestHelper.DefinirTituloColuna(p, "Entry", TaskField.TEXT8, "Disciplina");
        Assert.Null(CronogramaTipo1Analytics.ResolveDiscipline(p).Field);
    }
    [Fact]
    public void AmbiguousTitlesAreUnavailable()
    {
        var p = new ProjectFile();
        ProjectFileTestHelper.DefinirTituloColuna(p, "Entry", TaskField.TEXT8, "Disciplina");
        ProjectFileTestHelper.DefinirTituloColuna(p, "Other", TaskField.TEXT9, "Discipline");
        Assert.Null(CronogramaTipo1Analytics.ResolveDiscipline(p).Field);
    }
    [Fact]
    public void WorkConvertsMinutesToHoursInsteadOfUsingDurationOrActualWork()
    {
        var task = new ProjectFile().addTask();
        task.setWork(Duration.getInstance(150, TimeUnit.MINUTES));
        task.setDuration(Duration.getInstance(99, TimeUnit.DAYS));
        task.setActualWork(Duration.getInstance(800, TimeUnit.HOURS));
        Assert.Equal(2.5m, CronogramaTipo1Analytics.ReadWorkHours(task));
    }
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1d)]
    public void InvalidWorkStaysNull(double value)
    {
        var task = new ProjectFile().addTask();
        task.setWork(Duration.getInstance(value, TimeUnit.HOURS));
        Assert.Null(CronogramaTipo1Analytics.ReadWorkHours(task));
    }
    [Fact]
    public void MissingWorkStaysNull()
    {
        var task = new ProjectFile().addTask();
        task.setWork(null);
        Assert.Null(CronogramaTipo1Analytics.ReadWorkHours(task));
    }
    [Fact]
    public void NativeProjectSummaryCanExistOutsideTheOrdinaryParentTree()
    {
        var p=new ProjectFile();
        var root=p.addTask();root.setID(java.lang.Integer.valueOf(0));root.setWBS("0");root.setSummary(true);
        root.setWork(Duration.getInstance(100.01,TimeUnit.HOURS));
        var leaf=p.addTask();leaf.setWork(Duration.getInstance(100,TimeUnit.HOURS));
        Assert.Same(root,CronogramaTipo1Analytics.FindRoot(p));
        using var json=JsonDocument.Parse(CronogramaTipo1Analytics.Metadata(root,true,"ALIAS"));
        var meta=json.RootElement.GetProperty(CronogramaTipo1Analytics.MetadataKey).Deserialize<Tipo1AnalyticsMetadata>();
        var facts=ProjectPlanningRules.Read("p",1,[new(57,"root","0",true,null,100.01m,null,meta),
            new(2,"leaf","1",false,null,100,null,new(1,1,null,true,false,"ALIAS"))],"test",t => t.Discipline);
        Assert.Equal(100.01m,facts.PlannedHhTotal);Assert.Equal("RESUMO_PROJETO",facts.Source);
    }
    [Fact]
    public void PreparationPersistsRootDisciplineLeafAndNonDocumentaryTasksWithoutMatchingSummaries()
    {
        var p = new ProjectFile();
        ProjectFileTestHelper.DefinirAlias(p, TaskField.TEXT19, "Discipline");
        var root = p.addTask(); root.setSummary(true); root.setName("Project");
        root.setWork(Duration.getInstance(100, TimeUnit.HOURS));
        root.set(TaskField.TEXT1, "DOC-001"); // Must not conflict with the real document.
        var disc = root.addTask(); disc.setSummary(true); disc.setName("Area");
        disc.set(TaskField.TEXT19, "Piping"); disc.setWork(Duration.getInstance(70, TimeUnit.HOURS));
        var leaf = disc.addTask(); leaf.setName("Document"); leaf.set(TaskField.TEXT1, "DOC-001");
        leaf.set(TaskField.TEXT2, "65%"); leaf.set(TaskField.TEXT4, "80%");
        leaf.setWork(Duration.getInstance(4200, TimeUnit.MINUTES));
        var general = root.addTask(); general.setName("Meeting"); general.setWork(Duration.getInstance(30, TimeUnit.HOURS));
        var plan = new PlanoSincronizacaoCronogramaTipo1();
        var docs = new Dictionary<string, AccScheduleSyncService.MppScheduleRowData>();
        ServiceTestHelper.NovoAccScheduleSyncService().PrepararRegistrosSql("p", p, "test.mpp", TaskField.TEXT2,
            TaskField.TEXT4, TaskField.FINISH, new(), plan, docs);
        Assert.True(plan.PodePersistir);
        Assert.Equal(4, plan.RegistrosSqlPreparados.Count);
        Assert.Equal(2, plan.RegistrosSqlPreparados.Count(r => r.EhTarefaResumo));
        Assert.All(plan.RegistrosSqlPreparados.Where(r => r.EhTarefaResumo), r => Assert.False(r.CandidatoDocumento));
        var doc = Assert.Single(plan.RegistrosSqlPreparados, r => r.CandidatoDocumento);
        Assert.Equal(65m, doc.PhysicalDone);
        Assert.Equal(70m, doc.TrabalhoMpp);
        Assert.Equal(65m, docs["DOC001"].Realizado.ValorEmPontosPercentuais);
        Assert.DoesNotContain("AREA", docs.Keys);
        Assert.Null(plan.RegistrosSqlPreparados.Single(r => r.TaskName == "Meeting").DocNumber);
        var tasks = plan.RegistrosSqlPreparados.Select((r, i) => {
            using var json = JsonDocument.Parse(r.AnalyticsJson!);
            var meta = json.RootElement.GetProperty(CronogramaTipo1Analytics.MetadataKey).Deserialize<Tipo1AnalyticsMetadata>();
            return new PlanningTask(i+30, r.TaskUid, r.EdtWbs, r.EhTarefaResumo, r.Disciplina, r.TrabalhoMpp, r.DocNumber, meta);
        }).Reverse().ToArray();
        var facts = ProjectPlanningRules.Read("p", 1, tasks, "test", t => t.Discipline);
        Assert.Equal("RESUMO_PROJETO", facts.Source);
        Assert.Equal(100m, facts.PlannedHhTotal);
        Assert.Equal(70m, facts.DisciplineTotal);
        Assert.Equal(30m, facts.UndisciplinedHh);
        Assert.Equal(0m, facts.ReconciliationDifference);
    }
    [Fact]
    public void DocumentPhysicalProgressExcludesSummary()
    {
        var d = new PmoDocumento("d", "p", "DOC", null, null, null, null, null, null, null, null, null, null, null, null, null);
        var leaf = new PmoTarefa("l","p","DOC",null,65,null,null,null,null,true,false,20);
        var summary = leaf with { RowId="s", Resumo=true, Realizado=100 };
        Assert.Equal(65m, PmoExecutiveRules.Completar(d,[summary,leaf],1,DateTime.Today).AvancoFisico);
    }
    [Fact]
    public void PersistenceCommandCarriesSummaryDisciplineHoursAndNullsInType1Only()
    {
        var r = new RegistroCronogramaTipo1Preparado { ProjectIdAcc="p",TaskUid="root",TaskName="Project",
            EdtWbs="0",EhTarefaResumo=true,Disciplina="Piping",TrabalhoMpp=2.5m, AnalyticsJson="{}" };
        using var cmd = NA_Connect.Services.DatabaseService.CriarComandoSnapshotTipo1(r,DateTime.UtcNow);
        Assert.Equal(true,cmd.Parameters["@resumo"].Value);
        Assert.Equal("Piping",cmd.Parameters["@disciplina"].Value);
        Assert.Equal(2.5m,cmd.Parameters["@trabalho"].Value);
        Assert.Equal(DBNull.Value,cmd.Parameters["@phys"].Value);
        Assert.Equal(DBNull.Value,cmd.Parameters["@docNum"].Value);
        Assert.Contains("Tipo_LDP=1",cmd.CommandText);
        Assert.Contains("Data_Leitura_Cronograma=@snapshot",cmd.CommandText);
        r.TrabalhoMpp=null;
        using var missing=NA_Connect.Services.DatabaseService.CriarComandoSnapshotTipo1(r,DateTime.UtcNow);
        Assert.Equal(DBNull.Value,missing.Parameters["@trabalho"].Value);
    }
}
