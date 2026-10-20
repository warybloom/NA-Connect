using APS.WebApp.Services.DocumentQuality;

namespace NA_Connect.Qualidade.Tests;

/// <summary>
/// Avanço físico por Document_Status (nunca Status_In_Flow): configuração resolvida pela chave CANÔNICA (aliases explícitos) no backend,
/// e regressão de TIMESHEET_SEM_LDP (decisão exigida, não confirmada → continua redistribuível).
/// </summary>
public sealed class InformationQualityProgressCanonicalTests
{
    private static InformationQualityLdpRow Row(string code, string? status, decimal? fisico = null, string? situacao = null) =>
        new(code, null, null, "MECHANICAL", null, null, "DS", null, 10m, 5m, null, fisico, status, situacao);
    private static ProgressConfig Cfg(long id, string status, decimal percent, bool ativo = true) => new(id, status, status, percent, ativo);
    private static readonly ProgressConfig[] ModeloSalvo =
    [
        Cfg(1, "NAO INICIADO", 0), Cfg(2, "COLETA DE DADOS", 10), Cfg(3, "INICIADO", 20), Cfg(4, "ELABORANDO", 40),
        Cfg(5, "FINALIZADO ELABORACAO", 60), Cfg(6, "AUTORIZACAO", 70), Cfg(7, "EMITIDO PARA CLIENTE", 80),
        Cfg(8, "EM ATENDIMENTO DE COMENTARIO", 90), Cfg(9, "APROVADO", 100),
    ];
    private static List<InformationQualityFinding> Build(IReadOnlyList<InformationQualityLdpRow> rows, IReadOnlyList<ProgressConfig>? configs = null,
        IEnumerable<ProgressDecision>? decisions = null, IReadOnlySet<string>? redistributed = null) =>
        InformationQualityProgressService.BuildProgressFindings(rows, configs ?? ModeloSalvo, decisions ?? [], redistributed ?? new HashSet<string>());

    [Theory]
    [InlineData("Elaborando", "ELABORANDO")]
    [InlineData("EM ELABORAÇÃO", "ELABORANDO")]
    [InlineData("  em   elaboracao ", "ELABORANDO")]
    [InlineData("IN PROGRESS", "ELABORANDO")]
    [InlineData("Work_In-Progress", "ELABORANDO")]
    [InlineData("APPROVED", "APROVADO")]
    [InlineData("Não iniciado", "NAO INICIADO")]
    [InlineData("CLIENT REVIEW", "CLIENT REVIEW")] // fora da lista: vale por si (sem fuzzy)
    [InlineData("ELABORAÇÃO", "ELABORACAO")]       // parecido, mas NÃO é alias explícito
    public void ChaveCanonica_AliasesExplicitosSemFuzzy(string status, string esperado) =>
        Assert.Equal(esperado, DocumentStatusCanonical.Key(status));

    [Fact]
    public void Caso1_StatusExatamenteIgual_GeraSimularAvanco()
    {
        var f = Assert.Single(Build([Row("D-1", "ELABORANDO")]));
        Assert.Equal("AVANCO_FISICO_AUSENTE_STATUS", f.Rule);
        Assert.True(f.DecisionRequired);
        Assert.Contains(f.Details, d => d.Label == "Percentual configurado" && d.Value == "40%");
    }

    [Theory]
    [InlineData("EM ELABORAÇÃO")]
    [InlineData("IN PROGRESS")]
    [InlineData("elaborando")]
    public void Caso2_StatusEquivalente_ResolveMesmaConfiguracao(string status)
    {
        var f = Assert.Single(Build([Row("D-2", status)]));
        Assert.Equal("AVANCO_FISICO_AUSENTE_STATUS", f.Rule);
        Assert.Equal(4, InformationQualityProgressService.Resolve(status, ModeloSalvo).Config!.Id);
        // Configuração salva com grafia antiga (antes da chave canônica) também casa.
        Assert.Equal(77, InformationQualityProgressService.Resolve(status, [Cfg(77, "EM ELABORAÇÃO", 40)]).Config!.Id);
    }

    [Theory]
    [InlineData("Aprovado", "APPROVED")]
    [InlineData("APPROVED", "Aprovado")]
    [InlineData("Não iniciado", "NOT STARTED")]
    [InlineData("Emitido para cliente", "ISSUED FOR CLIENT")]
    [InlineData("ISSUED TO CLIENT", "Enviado ao cliente")]
    [InlineData("Em atendimento de comentário", "ADDRESSING COMMENTS")]
    [InlineData("Autorização", "AWAITING AUTHORIZATION")]
    [InlineData("Finalizado elaboração", "ELABORATION COMPLETED")]
    [InlineData("Coleta de dados", "DATA GATHERING")]
    public void DeParaPortuguesIngles_ConfiguracaoNumIdiomaValeParaOOutro(string configurado, string naLdp)
    {
        var f = Assert.Single(Build([Row("D-PT-EN", naLdp)], [Cfg(1, configurado, 50)]));
        Assert.Equal("AVANCO_FISICO_AUSENTE_STATUS", f.Rule);
        Assert.Equal(DocumentStatusCanonical.Key(configurado), DocumentStatusCanonical.Key(naLdp));
    }

    [Fact]
    public void Caso3_StatusSemConfiguracao_PedeConfigurar()
    {
        var f = Assert.Single(Build([Row("D-3", "CLIENT REVIEW")]));
        Assert.Equal("AVANCO_STATUS_SEM_CONFIGURACAO", f.Rule);
        Assert.False(f.DecisionRequired);
        // Configuração inativa não vale.
        Assert.Equal("AVANCO_STATUS_SEM_CONFIGURACAO", Assert.Single(Build([Row("D-3", "ELABORANDO")], [Cfg(4, "ELABORANDO", 40, ativo: false)])).Rule);
    }

    [Fact]
    public void Caso4_Configurado0_NaoHaAvancoParaConfirmar() =>
        Assert.Empty(Build([Row("D-4", "NOT STARTED")]));

    [Fact]
    public void Caso5_ConfiguradoMaiorQueZero_GeraSimular() =>
        Assert.Equal("AVANCO_FISICO_AUSENTE_STATUS", Assert.Single(Build([Row("D-5", "APPROVED")])).Rule);

    [Fact]
    public void Caso6_DocumentoCancelado_NaoParticipa()
    {
        Assert.Empty(Build([Row("D-6", "CANCELED")]));
        Assert.Empty(Build([Row("D-6", "ELABORANDO", situacao: "CANCELED")]));
        Assert.Empty(Build([Row("D-6", "ELABORANDO", situacao: "RETIRADO")]));
    }

    [Fact]
    public void Caso7_AvancoRealMaiorQueZero_Prevalece()
    {
        Assert.Empty(Build([Row("D-7", "ELABORANDO", fisico: 25)]));
        Assert.Single(Build([Row("D-7", "ELABORANDO", fisico: 0)]));   // 0 = sem avanço
    }

    [Fact]
    public void Caso8_DocumentoJaValidado_MostraConfirmadoSemNovaAcao()
    {
        var decisao = new ProgressDecision(Guid.NewGuid(), "p", "d-8 ", "ELABORANDO", 4, null, 40, "CONFIRMADO", "u@na.com", DateTime.UtcNow);
        var f = Assert.Single(Build([Row("D-8", "ELABORANDO")], decisions: [decisao]));
        Assert.Equal("AVANCO_STATUS_CONFIRMADO", f.Rule);
        Assert.False(f.DecisionRequired);
    }

    [Fact]
    public void Caso9_DocumentoJaRedistribuido_NaoRecebeAvanco() =>
        Assert.Empty(Build([Row("D-9", "ELABORANDO")], redistributed: new HashSet<string> { "D-9" }));

    [Fact]
    public void ConfiguracoesEquivalentesConflitantes_NaoEscolheNenhuma()
    {
        var conflito = new[] { Cfg(1, "ELABORANDO", 40), Cfg(2, "EM ELABORAÇÃO", 45) };
        Assert.True(InformationQualityProgressService.Resolve("IN PROGRESS", conflito).Conflict);
        var f = Assert.Single(Build([Row("D-10", "IN PROGRESS")], conflito));
        Assert.Equal("AVANCO_STATUS_SEM_CONFIGURACAO", f.Rule);
        Assert.Contains("conflitantes", f.Description);
    }

    [Fact]
    public void Configuracao_ListaStatusObservadosComConfiguracaoEquivalente()
    {
        var settings = InformationQualityProgressService.BuildSettings([("EM ELABORAÇÃO", 12), ("CLIENT REVIEW", 3)], [Cfg(4, "ELABORANDO", 40), Cfg(9, "APROVADO", 100)]);
        var elab = Assert.Single(settings, s => s.DocumentStatus == "EM ELABORAÇÃO");
        Assert.True(elab.IsConfigured); Assert.Equal(40m, elab.Percentual); Assert.Equal(12, elab.DocumentCount);
        Assert.False(Assert.Single(settings, s => s.DocumentStatus == "CLIENT REVIEW").IsConfigured);
        Assert.Contains(settings, s => s.DocumentStatus == "APROVADO" && s.DocumentCount == 0 && s.IsConfigured); // salva sem documento observado
    }

    // ----------------------------------------------------------------------- TIMESHEET_SEM_LDP ----

    [Fact]
    public void TimesheetSemLdp_ExigeDecisao_ENaoConfirmado_ContinuaNaAuditoria()
    {
        var findings = InformationQualityAuditService.BuildFindings(
            [Row("BML011.013-940-ME7-DS-001", "APPROVED")],
            [new("BML011.013-940-ME7-DS-002", "MECÂNICA", 1.67m)], []);
        var f = Assert.Single(findings, x => x.Rule == "TIMESHEET_SEM_LDP");
        Assert.True(f.DecisionRequired);
        Assert.Equal("BML011.013-940-ME7-DS-002", f.DocumentCode);

        // Redistribuição confirmada de OUTRO documento não remove esta ocorrência.
        var outra = new RedistributionDecision(Guid.NewGuid(), "p", "BML011.013-000-CI5-CS-001", "CIVIL", .5m, 57m, "CONFIRMADO", "u", DateTime.UtcNow, []);
        InformationQualityAuditService.ApplyRedistributions(findings, [outra]);
        Assert.Contains(findings, x => x.Rule == "TIMESHEET_SEM_LDP" && x.DocumentCode == "BML011.013-940-ME7-DS-002");
    }

    [Fact]
    public void TimesheetSemLdp_ConfirmadaComEspacosOuCaixaDiferente_EhRemovidaComoNoFrontend()
    {
        var findings = InformationQualityAuditService.BuildFindings([], [new("BML011.013-940-ME7-DS-002", "MECÂNICA", 1.67m)], []);
        var mesma = new RedistributionDecision(Guid.NewGuid(), "p", " bml011.013-940-me7-ds-002 ", "MECÂNICA", 1.67m, 10m, "CONFIRMADO", "u", DateTime.UtcNow, []);
        InformationQualityAuditService.ApplyRedistributions(findings, [mesma]);
        Assert.DoesNotContain(findings, x => x.Rule == "TIMESHEET_SEM_LDP");
        Assert.Contains(findings, x => x.Rule == "REDISTRIBUICAO_CONFIRMADA");
        // Status diferente de CONFIRMADO não trata nada.
        var pendente = InformationQualityAuditService.BuildFindings([], [new("X-1", "MECÂNICA", 1m)], []);
        InformationQualityAuditService.ApplyRedistributions(pendente, [mesma with { SourceDocumentCode = "X-1", Status = "PENDENTE" }]);
        Assert.Contains(pendente, x => x.Rule == "TIMESHEET_SEM_LDP");
    }
}
