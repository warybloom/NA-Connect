using APS.WebApp.Services.RobotRun;

namespace NA_Connect.RobotConfiguration.Tests;

/// <summary>Progresso REAL do robô: plano de etapas do catálogo + registros do Worker. Sem timer.</summary>
public sealed class ProgressTests
{
    private static string Id(int n) => $"b.00000000-0000-0000-0000-{n:000000000000}";
    private static RobotHistoricoEntry E(string projeto, string etapa, string status, string msg = "") => new(projeto, etapa, status, msg);
    private static RobotRunGate Fila(params RobotQueueItem[] items)
    {
        var gate = new RobotRunGate();
        Assert.True(gate.TryBeginQueue("main", items, out _));
        return gate;
    }

    [Fact]
    public void PlanoUsaCatalogo_Tipo2NaoContaGestaoVistaDuplicada_IndisponiveisForaDoPlano()
    {
        var tipo1 = RobotStageCatalog.FullPlan(1);
        Assert.DoesNotContain("02", tipo1); Assert.DoesNotContain("08", tipo1);
        Assert.Equal(RobotStageCatalog.Stages.Count(s => s.Available), tipo1.Length);
        Assert.Contains("GESTAO_VISTA", tipo1);
        Assert.DoesNotContain("GESTAO_VISTA", RobotStageCatalog.FullPlan(2));
        Assert.Equal(["06", "07"], RobotStageCatalog.Plan(["07", "06", "GESTAO_VISTA"], 2));
        Assert.Equal(["GESTAO_VISTA"], RobotStageCatalog.Plan(["GESTAO_VISTA"], 2));
    }

    [Fact]
    public void FilaConfigurada_UsaExatamenteAsEtapasDoItem_PercentualPorEtapasReais()
    {
        var gate = Fila(new RobotQueueItem(Id(1), "CHS001", 1, ["01", "04"]), new RobotQueueItem(Id(2), "BML", 2, ["05", "5B"]));
        var s = gate.Snapshot;
        Assert.Equal(4, s.OverallTotalStages); Assert.Equal(0, s.ProgressPercent); Assert.Equal(0, s.QueuePosition); Assert.Equal(2, s.QueueTotal);

        gate.QueueProjectStarted(1, new(Id(1), "CHS001", 1, ["01", "04"]), "BML");
        gate.Record(E("CHS001", "ETAPA 01 - TIMESHEET", "INICIADO"));
        s = gate.Snapshot;
        Assert.Equal(1, s.QueuePosition); Assert.Equal("CHS001", s.ProjectName); Assert.Equal("BML", s.NextProject);
        Assert.Equal(1, s.StagePosition); Assert.Equal(2, s.StageTotal); Assert.Equal("01", s.CurrentStageId);
        Assert.Equal(0, s.ProgressPercent); // iniciar etapa NÃO avança percentual

        gate.Record(E("CHS001", "ETAPA 01 - TIMESHEET", "CONCLUIDO"));
        gate.Record(E("CHS001", "ETAPA 04 - GRDs", "INICIADO"));
        s = gate.Snapshot;
        Assert.Equal(25, s.ProgressPercent); Assert.Equal(1, s.OverallCompletedStages);
        Assert.Equal(2, s.StagePosition); Assert.Equal(["COMPLETED", "RUNNING"], s.Stages.Select(x => x.State));
        Assert.Equal(["01", "04"], s.Stages.Select(x => x.Id));
    }

    [Fact]
    public void EtapaXdeY_Projeto05BNormalizado_E_ProjetoXdeY()
    {
        var gate = Fila(new RobotQueueItem(Id(1), "A", 1, ["01"]), new RobotQueueItem(Id(2), "B", 2, ["04", "05", "5B"]));
        gate.QueueProjectStarted(1, new(Id(1), "A", 1, ["01"]), "B");
        gate.Record(E("A", "ETAPA 01 - TIMESHEET", "CONCLUIDO"));
        gate.QueueProjectFinished(new(Id(1), "A", 1, ["01"]), new(false, "ok"));
        gate.QueueProjectStarted(2, new(Id(2), "B", 2, ["04", "05", "5B"]), null);
        gate.Record(E("B", "ETAPA 04 - GRDs", "CONCLUIDO"));
        gate.Record(E("B", "ETAPA 05 - WORKFLOW", "CONCLUIDO"));
        gate.Record(E("B", "ETAPA 05B - SNAPSHOT DAS ETAPAS ATUAIS DOS ACC REVIEWS", "INICIADO"));
        var s = gate.Snapshot;
        Assert.Equal(2, s.QueuePosition); Assert.Equal(2, s.QueueTotal); Assert.Null(s.NextProject);
        Assert.Equal(3, s.StagePosition); Assert.Equal(3, s.StageTotal); Assert.Equal("5B", s.CurrentStageId);
        Assert.Equal(75, s.ProgressPercent);
        Assert.Equal("5B", RobotRunGate.StageIdOf("ETAPA 05B - X")); Assert.Equal("01", RobotRunGate.StageIdOf("ETAPA 1 - X"));
        Assert.Null(RobotRunGate.StageIdOf("PROJETO")); Assert.Null(RobotRunGate.StageIdOf("DESCOBERTA E DOWNLOAD DA LDP"));
    }

    [Fact]
    public void Completed_ChegaA100_ENuncaPassaDe100()
    {
        var gate = Fila(new RobotQueueItem(Id(1), "A", 1, ["01", "04"]));
        gate.QueueProjectStarted(1, new(Id(1), "A", 1, ["01", "04"]), null);
        gate.Record(E("A", "ETAPA 01 - TIMESHEET", "CONCLUIDO"));
        gate.Record(E("A", "ETAPA 04 - GRDs", "CONCLUIDO"));
        // Registros repetidos/extras não inflam o percentual.
        gate.Record(E("A", "ETAPA 04 - GRDs", "CONCLUIDO"));
        gate.Record(E("A", "ETAPA 17 - Qualidade", "CONCLUIDO"));
        Assert.Equal(100, gate.Snapshot.ProgressPercent);
        var final = gate.Finish(new(false, "CONCLUÍDO COM SUCESSO"), DateTime.Now);
        Assert.Equal(RobotRunStatus.Completed, final.State); Assert.Equal(100, final.ProgressPercent);
        Assert.Equal(2, final.OverallCompletedStages); Assert.Equal(2, final.OverallTotalStages);
    }

    [Fact]
    public void ProjetoEncerradoMarcaNaoExecutadasComoSkipped_ErroFicaVisivel()
    {
        var gate = Fila(new RobotQueueItem(Id(1), "CHS001", 1, ["01", "04", "05"]));
        gate.QueueProjectStarted(1, new(Id(1), "CHS001", 1, ["01", "04", "05"]), null);
        gate.Record(E("CHS001", "ETAPA 01 - TIMESHEET", "CONCLUIDO"));
        gate.Record(E("CHS001", "ETAPA 04 - GRDs", "INICIADO"));
        gate.Record(E("CHS001", "ETAPA 04 - GRDs", "ERRO", "Falha no download\nstack trace..."));
        gate.Record(E("CHS001", "PROJETO", "CONCLUIDO COM ERROS"));
        var s = gate.Snapshot;
        Assert.Equal(["COMPLETED", "ERROR", "SKIPPED"], s.Stages.Select(x => x.State));
        Assert.Equal("Falha no download", s.Stages[1].Message);
        Assert.Equal(1, s.OverallFailedStages); Assert.Equal(1, s.OverallSkippedStages); Assert.Equal(100, s.ProgressPercent);
        var final = gate.Finish(new(true, "CONCLUÍDO COM ERROS: 1 projeto(s)/falha(s)."), DateTime.Now);
        Assert.Equal(RobotRunStatus.Error, final.State);
    }

    [Fact]
    public void ErroForaDeEtapa_FinalizaEtapaEmCurso_EHistoricoRecenteLimitado()
    {
        var gate = Fila(new RobotQueueItem(Id(1), "A", 1, ["01", "04"]));
        gate.QueueProjectStarted(1, new(Id(1), "A", 1, ["01", "04"]), null);
        gate.Record(E("A", "ETAPA 01 - TIMESHEET", "INICIADO"));
        gate.Record(E("A", "ETAPA 01 - TIMESHEET", "ERRO", "x"));
        for (int i = 0; i < 40; i++) gate.Record(E("A", "DESCOBERTA E DOWNLOAD DA LDP", "INICIADO"));
        var s = gate.Snapshot;
        Assert.Equal(RobotRunGate.RecentLimit, s.RecentEntries.Count);
        Assert.Equal("ERROR", s.Stages[0].State);
    }

    [Fact]
    public void ExecucaoManual_PlanoCompletoPorTipoLdp()
    {
        var gate = new RobotRunGate();
        Assert.True(gate.TryBeginManualProjects([new(Id(1), "T1") { TipoLdp = 1 }, new(Id(2), "T2") { TipoLdp = 2 }], DateTime.Now, out var s));
        Assert.Equal(RobotStageCatalog.FullPlan(1).Length + RobotStageCatalog.FullPlan(2).Length, s.OverallTotalStages);
        Assert.Equal(2, s.QueueTotal); Assert.Equal("T1", s.NextProject); Assert.Equal(0, s.ProgressPercent);
    }

    [Fact]
    public void CicloSemPlano_NaoInventaPercentual()
    {
        var gate = new RobotRunGate();
        Assert.True(gate.TryBegin(false, null, null, DateTime.Now, out var s));
        Assert.Null(s.ProgressPercent);
        gate.Record(E("A", "ETAPA 01 - TIMESHEET", "CONCLUIDO"));
        Assert.Null(gate.Snapshot.ProgressPercent);
        Assert.Single(gate.Snapshot.RecentEntries);
    }
}
