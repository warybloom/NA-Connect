namespace APS.WebApp.Services.RobotRun;

public sealed record RobotStage(string Id, string Name, int Order, bool Available, string Dependencies, bool CanRunAlone);
public static class RobotStageCatalog
{
    // Mesma ordem operacional do GrdAutomationWorker; IDs das etapas preservados.
    public static readonly IReadOnlyList<RobotStage> Stages = Array.AsReadOnly(new RobotStage[] {
        new("00", "DNA DO PROJETO", 0, true, "Valida DNA conforme o layout, sem executar Cronograma ou persistência final.", true),
        new("01", "TIMESHEET", 10, true, "LDP acessível; reconstrói a aba TIMESHEET.", true),
        new("02", "ETAPA 02", 20, false, "Temporariamente desabilitada no Worker.", false),
        new("03", "SNAPSHOT TIMESHEET", 30, true, "Exige 01 nesta execução.", false),
        new("04", "GRDs", 40, true, "LDP e GRDs acessíveis.", true),
        new("4B", "CLASSIFICAÇÃO DE EMISSÃO", 45, true, "Somente leitura da LDP atual no banco; não depende da 04.", true),
        new("05", "WORKFLOW", 50, true, "ACC Reviews e LDP acessível.", true),
        new("5B", "SNAPSHOT DAS ETAPAS ATUAIS DOS ACC REVIEWS", 55, true, "Exige 05 nesta execução; reutiliza workflowResult.", false),
        new("06", "CUSTO", 60, true, "Tipo2: executa Gestão à Vista uma única vez.", true),
        new("GESTAO_VISTA", "GESTÃO À VISTA", 65, true, "Extração existente de custos e Timesheet; Tipo2 reutiliza a execução de 06.", true),
        new("07", "ATRIBUTOS ACC", 70, true, "Lê dbo.VW_Ldp_Unificada disponível; não antecipa a persistência final.", true),
        new("09", "CRONOGRAMA", 90, true, "Tipo2: router com carga SQL inicial necessária ao matching.", true),
        new("10", "ESPELHAMENTO", 100, true, "Tipo2: parser da LDP atual e espelho existente na 01.06, sem persistência da LDP.", true),
        new("11", "PERSISTÊNCIA NA LDP UNIFICADA", 110, true, "Snapshot final da versão mais recente da LDP original.", true),
        new("12", "External Action", 140, true, "Pasta/arquivo oficial acessível.", true),
        new("13", "Internal Action", 150, true, "Pasta/arquivo oficial acessível.", true),
        new("14", "Scope Deviation", 160, true, "Pasta/arquivo oficial acessível.", true),
        new("15", "Weekly Schedule", 170, true, "Pasta/arquivo oficial acessível.", true),
        new("16", "Hold Control", 180, true, "Pasta/arquivo oficial acessível.", true),
        new("17", "Qualidade", 190, true, "Base Lançamento + RNC + Desvio de Qualidade.", true),
        new("08", "RunApprovalVerdictSyncAsync", 200, false, "Removida: escritor concorrente de Status_In_Flow.", false)
    });
    public static string Normalize(string id) => id?.ToUpperInvariant() switch
    {
        "5B" or "05B" => "5B",
        "4B" or "04B" => "4B",
        "GESTAO_VISTA" => "GESTAO_VISTA",
        _ => int.TryParse(id, out var n) ? n.ToString("00") : id
    };
    public static string[] Validate(IEnumerable<string> stages, int? tipo = null)
    {
        if (stages == null) throw new ArgumentException("Informe as etapas.");
        var ids = stages.Select(Normalize).ToArray();
        if (ids.Length == 0 || ids.Distinct().Count() != ids.Length || ids.Any(id => !Stages.Any(s => s.Id == id && s.Available)))
            throw new ArgumentException("Selecione etapas válidas, disponíveis e sem repetição.");
        if (ids.Contains("03") && !ids.Contains("01")) throw new ArgumentException("Etapa 03 exige 01.");
        if (ids.Contains("5B") && !ids.Contains("05")) throw new ArgumentException("Etapa 5B exige 05.");
        return Stages.Where(s => ids.Contains(s.Id)).OrderBy(s => s.Order).Select(s => s.Id).ToArray();
    }

    /// <summary>
    /// Plano REAL de etapas que o Worker executa para as etapas selecionadas (base do progresso exibido na UI). Só etapas disponíveis;
    /// em LDP Tipo2 a 06 já executa a Gestão à Vista e o Worker não roda GESTAO_VISTA de novo — logo ela não conta como trabalho pendente.
    /// </summary>
    public static string[] Plan(IEnumerable<string> stages, int tipoLdp)
    {
        var ids = Validate(stages);
        return tipoLdp == 2 && ids.Contains("06") ? ids.Where(id => id != "GESTAO_VISTA").ToArray() : ids;
    }
    /// <summary>Execução manual completa (botão INICIAR ROBÔ): todas as etapas disponíveis, ajustadas ao Tipo_LDP do projeto.</summary>
    public static string[] FullPlan(int tipoLdp) => Plan(Stages.Where(s => s.Available).Select(s => s.Id), tipoLdp);
    public static string NameOf(string id) => Stages.FirstOrDefault(s => s.Id == Normalize(id))?.Name ?? id;
}
public sealed record RobotProjectSelection(string ProjectId, int Order, bool Enabled, string[] Stages);
public sealed record RobotSchedule(string Time, string TimeZone); // Future contract; rejected until scheduler exists.
public sealed record RobotDefinition(string Id, string Name, bool Enabled, RobotProjectSelection[] Projects, RobotSchedule? Schedule = null);
public sealed record RobotConfiguration(int Version, long Revision, DateTimeOffset? SavedAt, RobotDefinition[] Robots)
{
    public static RobotConfiguration Empty => new(1, 0, null, [new("main", "Robô Principal", false, [])]);
}
public sealed record RegisteredRobotProject(string ProjectId, string Name, string HubId, bool Active, int TipoLdp);
public sealed record RobotQueueItem(string ProjectId, string Name, int Order, string[] Stages)
{
    /// <summary>Tipo_LDP do cadastro (só para o plano de progresso; o Worker continua decidindo pelo DNA da LDP).</summary>
    public int TipoLdp { get; init; } = 1;
}
public sealed class RobotConfigurationException(string message) : Exception(message);
public sealed class RobotConfigurationConflictException() : Exception("Configuração alterada por outra sessão. Recarregue antes de salvar/executar.");

public static class RobotConfigurationRules
{
    public static RobotConfiguration Validate(RobotConfiguration config)
    {
        if (config == null || config.Version != 1 || config.Revision < 0 || config.Robots == null || config.Robots.Length == 0 ||
            config.Robots.Any(r => r == null || string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 64) || config.Robots.Select(r => r.Id).Distinct().Count() != config.Robots.Length)
            throw new ArgumentException("Versão ou identificadores de robô inválidos.");
        var robots = config.Robots.Select(robot => {
        if (robot.Schedule != null) throw new ArgumentException("Agenda ainda não disponível.");
        if (string.IsNullOrWhiteSpace(robot.Name) || robot.Name.Length > 100 || robot.Projects == null || robot.Projects.Length > 1000)
            throw new ArgumentException("Nome ou lista de projetos inválidos.");
        if (robot.Projects.Any(p => p == null || !Guid.TryParseExact(p.ProjectId?.Replace("b.", ""), "D", out _) || p.Order < 1 || p.Order > 1000 || p.Stages == null) ||
            robot.Projects.Select(p => p.ProjectId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != robot.Projects.Length ||
            robot.Projects.Select(p => p.Order).Distinct().Count() != robot.Projects.Length)
            throw new ArgumentException("Projetos e ordens devem ser únicos e válidos.");
        var projects = robot.Projects.OrderBy(p => p.Order).Select(p => p with {
            Stages = p.Stages.Length == 0 && !p.Enabled ? [] : RobotStageCatalog.Validate(p.Stages)
        }).ToArray();
        return robot with { Projects = projects };
        }).ToArray();
        return config with { Robots = robots };
    }
    public static RobotQueueItem[] BuildQueue(RobotDefinition robot, IReadOnlyList<RegisteredRobotProject> registered)
    {
        if (!robot.Enabled) throw new ArgumentException("Robô inativo.");
        var items = robot.Projects.Where(p => p.Enabled).OrderBy(p => p.Order).Select(p => {
            var current = registered.SingleOrDefault(r => string.Equals(r.ProjectId, p.ProjectId, StringComparison.OrdinalIgnoreCase));
            if (current == null || !current.Active) throw new ArgumentException($"Projeto não cadastrado ou inativo: {p.ProjectId}.");
            return new RobotQueueItem(current.ProjectId, current.Name, p.Order, RobotStageCatalog.Validate(p.Stages, current.TipoLdp)) { TipoLdp = current.TipoLdp };
        }).ToArray();
        if (items.Length == 0) throw new ArgumentException("Selecione pelo menos um projeto ativo e suas etapas.");
        return items;
    }
}
