using APS.WebApp.Services.Planning;
using APS.WebApp.Services.Reports;

namespace NA_Connect.Planning.Tests;

/// <summary>
/// 30/09/2026 — HH realizado CORRIGIDO (fonte única no backend) e Avanço previsto/físico (Tipo 1 preservado,
/// Tipo 2 com gates consolidados por documento, corte na semana vigente).
/// </summary>
public class HhCorrigidoEAvancoTests
{
    // ------------------------------------------------------------------ HH corrigido (provider canônico) ----
    private static ActualEntry L(long id, string? doc, string disc, decimal hh, decimal custo) =>
        new(id, doc, disc, "NORMA_EXATA", hh, null, custo, "X");

    [Fact]
    public void HhRealizado_TotalDoProjeto_EhTimesheetCompleto_LdpServeSomenteParaDiagnostico()
    {
        var entries = new[]
        {
            L(1, "DOC-A", "CIVIL", 10, 100), L(2, "doc-a ", "CIVIL", 5, 50),
            L(3, "DOC-B", "MEC", 7, 70),
            L(4, "DOC-FORA", "MEC", 2, 20),
        };
        var ldp = new HashSet<string> { "DOC-A", "DOC-B" };
        var f = ProjectActualRules.Read("p", entries, null, ldp);

        Assert.Equal(24m, f.Total.Hh);                                               // todo o Timesheet
        Assert.Equal(240m, f.Total.KnownCalculatedCostSubtotal);
        Assert.Equal(2m, f.ForaDaLdp!.Hh);                                          // diagnóstico, sem retirar do total
        Assert.Equal("TODOS_OS_LANCAMENTOS_COM_DIAGNOSTICO_LDP", f.EscopoHh);
        Assert.Equal(f.Total.Hh, f.ByDocument.Sum(d => d.Values.Hh));                // inclui DOC-FORA
        Assert.Equal(f.Total.Hh, f.ByDiscipline.Sum(d => d.Values.Hh));
        Assert.Equal(f.Total.KnownCalculatedCostSubtotal, f.ByDiscipline.Sum(d => d.Values.KnownCalculatedCostSubtotal));
        Assert.Equal(15m, f.ByDocument.Single(d => d.Document == "DOC-A").Values.Hh);
    }

    [Fact]
    public void HhRealizado_EhAColunaProntaActualHhTotalCorrigido_SemReconstruir()
    {
        var docs = new[]
        {
            Doc("A", hh: 10) with { HHRealizado = 8.5m, Disciplina = "CIVIL" },             // valor da coluna, como veio do banco
            Doc("B", hh: 10) with { HHRealizado = 4m, Disciplina = " civil " },              // mesma disciplina (chave normalizada)
            Doc("C", hh: 10) with { HHRealizado = null, Disciplina = "MEC" },                // documento sem valor na view
        };
        var r = PmoAvancoRules.HhRealizado(docs);
        Assert.Equal(12.5m, r.Total);                                                    // Σ da coluna, nada somado por fora
        Assert.Equal(12.5m, r.PorDisciplina.Single(d => d.Disciplina == "CIVIL").Hh);
        Assert.Null(r.PorDisciplina.Single(d => d.Disciplina == "MEC").Hh);
        Assert.Equal("dbo.VW_Ldp_Unificada.Actual_HH_Total_Corrigido", r.Fonte);
        // A consulta lê a coluna pronta e não lê as parcelas (nenhuma reconstrução possível).
        Assert.Contains("Actual_HH_Total_Corrigido", PmoExecutiveData.Query);
        Assert.DoesNotContain("Actual_HH_Corrigido", PmoExecutiveData.Query);
        Assert.DoesNotContain("HH_OS_Contratado", PmoExecutiveData.Query);
    }

    [Fact]
    public void CustoRealizado_EhAColunaProntaActualCostTotalCorrigido_QuandoExisteNaView()
    {
        var docs = new[]
        {
            Doc("A", hh: 10) with { HHRealizado = 8m, CustoRealizado = 800m, Disciplina = "CIVIL" },
            Doc("B", hh: 10) with { HHRealizado = 2m, CustoRealizado = 200m, Disciplina = "MEC" },
        };
        var oficial = PmoAvancoRules.HhRealizado(docs, custoOficial: true);
        Assert.True(oficial.CustoOficial);
        Assert.Equal(1000m, oficial.CustoTotal);
        Assert.Equal(800m, oficial.PorDisciplina.Single(d => d.Disciplina == "CIVIL").Custo);
        // Ambiente sem a coluna: nada de custo inventado.
        var semColuna = PmoAvancoRules.HhRealizado(docs);
        Assert.False(semColuna.CustoOficial);
        Assert.Null(semColuna.CustoTotal);
        // Lê a coluna pronta; a consulta alternativa não referencia a coluna inexistente (sem 503).
        Assert.Contains("C.Actual_Cost_Corrigido AS Actual_Cost_Total_Corrigido", PmoExecutiveData.Query);
        Assert.Contains("Actual_Cost_Corrigido", PmoExecutiveData.Query);
        Assert.DoesNotContain("C.Actual_Cost_Corrigido AS Actual_Cost_Total_Corrigido", PmoExecutiveData.QuerySemCustoOficial);
        Assert.DoesNotContain("Actual_HH_Total_Corrigido, Actual_Cost_Corrigido", PmoExecutiveData.QuerySemCustoOficial);
        Assert.Contains("CAST(NULL AS DECIMAL(18,4)) AS Actual_Cost_Total_Corrigido", PmoExecutiveData.QuerySemCustoOficial);
    }

    // ---------------------------------------------------------------------------------- Avanço ----
    private static readonly DateTime Hoje = new(2026, 9, 30);                        // quarta-feira
    private static PmoDocumento Doc(string codigo, decimal? hh = 10, DateTime? baseline = null, DateTime? emissao = null, string status = "SEM_PRAZO",
        params PmoGate[] gates) =>
        new PmoDocumento(codigo, "p", codigo, codigo, "CIVIL", hh, null, null, baseline, null, emissao, null, null, null, null, null)
        { StatusDocumento = emissao.HasValue ? "EMITIDO" : status, Gates = gates };
    private static PmoGate G(int p, decimal? fisico, DateTime? baseline = null, bool ativo = true, bool cancelado = false, decimal? esforco = 10) =>
        new(p, ativo, baseline, null, null, fisico) { Cancelado = cancelado, EsforcoHoras = esforco };

    [Fact]
    public void SemanaVigente_DomingoASabado_RegraExistente()
    {
        var (ini, fim) = PmoAvancoRules.SemanaVigente(Hoje);
        Assert.Equal((new DateTime(2026, 9, 27), new DateTime(2026, 10, 3)), (ini, fim));
        Assert.Equal(DayOfWeek.Sunday, ini.DayOfWeek);
    }

    [Fact]
    public void Tipo1_FisicoIgualAoDaCurvaS_EPrevistoNaoUsaSemanasFuturas()
    {
        var docs = new[]
        {
            Doc("A", 10, new DateTime(2026, 9, 1), emissao: new DateTime(2026, 9, 10)),   // emitido
            Doc("B", 30, new DateTime(2026, 10, 2)),                                       // baseline na semana vigente (sexta)
            Doc("C", 60, new DateTime(2026, 10, 20)),                                      // semana futura
        };
        var a = PmoAvancoRules.Calcular(1, Hoje, docs, null);
        var curva = PmoCurvaSRules.Build("p", Hoje, docs, null);
        // Físico: exatamente a regra anterior (Curva S, valor agregado por emissão até hoje).
        var fisicoAnterior = Math.Round(curva.Pontos.Last(p => p.Data <= Hoje).RealizadoHh / curva.Cobertura.HhCurvaPrevisto * 100m, 2);
        Assert.Equal(fisicoAnterior, a.Fisico);
        Assert.Equal(10m, a.Fisico);
        // Previsto até o FIM da semana vigente: A + B (40 de 100); C (semana futura) fora.
        Assert.Equal(40m, a.Previsto);
        Assert.Equal(1, a.TipoLdp);
    }

    // ------------------------- TIPO 2: ESFORÇO (Work da fase), NUNCA média simples dos gates -------------------------
    private static readonly DateTime Vencido = new(2026, 9, 1), Futuro = new(2026, 10, 20);

    [Fact]
    public void Tipo2_30x80_mais_60x50_Da60_NaoMediaSimples65()
    {
        var doc = Doc("A", 10, null, null, "SEM_PRAZO", G(30, 80, esforco: 30), G(60, 50, esforco: 60));
        var a = PmoAvancoRules.Calcular(2, Hoje, new[] { doc }, null);
        Assert.Equal(54m, a.FisicoNumerador);                                          // 30×0,8 + 60×0,5
        Assert.Equal(90m, a.FisicoDenominador);                                        // 30 + 60
        Assert.Equal(60m, a.Fisico);
        Assert.NotEqual(65m, a.Fisico);
        Assert.Contains("Esforço", a.Ponderacao);
    }

    [Fact]
    public void Tipo2_SomenteGate30()
    {
        var a = PmoAvancoRules.Calcular(2, Hoje, new[] { Doc("A", 10, null, null, "SEM_PRAZO", G(30, 40, Vencido, esforco: 25)) }, null);
        Assert.Equal((40m, 100m), (a.Fisico!.Value, a.Previsto!.Value));
        Assert.Equal(40m, a.PorFase.Single(f => f.Gate == 30).Fisico);
        Assert.Null(a.PorFase.Single(f => f.Gate == 60).Fisico);                       // fase inexistente: sem valor, nunca 0%
    }

    [Fact]
    public void Tipo2_30_60_90_PorFaseEGeral_PonderadosPeloEsforco()
    {
        var doc = Doc("A", 10, null, null, "SEM_PRAZO",
            G(30, 100, Vencido, esforco: 20), G(60, 50, Vencido, esforco: 40), G(90, 0, Futuro, esforco: 40));
        var a = PmoAvancoRules.Calcular(2, Hoje, new[] { doc }, null);
        Assert.Equal(40m, a.Fisico);                                                   // (20 + 20 + 0) / 100
        Assert.Equal(60m, a.Previsto);                                                 // gate 90 vence depois do corte
        Assert.Equal(new decimal?[] { 100m, 50m, 0m }, a.PorFase.Select(f => f.Fisico));
        Assert.Equal(new decimal?[] { 100m, 100m, 0m }, a.PorFase.Select(f => f.Previsto));
    }

    [Fact]
    public void Tipo2_FaseCancelada_ForaDoNumeradorEDenominador()
    {
        var doc = Doc("A", 10, null, null, "SEM_PRAZO", G(30, 100, esforco: 30), G(60, 0, esforco: 60, cancelado: true), G(90, 0, esforco: 50, ativo: false));
        var a = PmoAvancoRules.Calcular(2, Hoje, new[] { doc }, null);
        Assert.Equal((30m, 30m, 100m), (a.FisicoNumerador, a.FisicoDenominador, a.Fisico!.Value));
    }

    [Fact]
    public void Tipo2_FaseSemEsforco_NaoVira0_Nem1_NemNumeroDoGate()
    {
        var doc = Doc("A", 10, null, null, "SEM_PRAZO", G(30, 100, esforco: 30), G(60, 0, esforco: null), G(90, 0, esforco: 0));
        var a = PmoAvancoRules.Calcular(2, Hoje, new[] { doc }, null);
        Assert.Equal(100m, a.Fisico);                                                  // só o gate 30 tem esforço válido
        Assert.Equal(30m, a.FisicoDenominador);
        Assert.Equal(2, a.FasesSemEsforco);
        Assert.Null(PmoAvancoRules.Calcular(2, Hoje, new[] { Doc("B", 10, null, null, "SEM_PRAZO", G(30, 50, esforco: null)) }, null).Fisico);
    }

    [Fact]
    public void Tipo2_VariosDocumentos_CadaFaseEntraComOSeuEsforco()
    {
        var a = Doc("A", 10, null, null, "SEM_PRAZO", G(30, 100, esforco: 10), G(60, 50, esforco: 30));
        var b = Doc("B", 10, null, null, "SEM_PRAZO", G(30, 0, esforco: 20), G(90, 100, esforco: 40));
        var r = PmoAvancoRules.Calcular(2, Hoje, new[] { a, b }, null);
        Assert.Equal(65m, r.FisicoNumerador);                                          // 10 + 15 + 0 + 40
        Assert.Equal(100m, r.FisicoDenominador);
        Assert.Equal(65m, r.Fisico);
        Assert.Equal(33.33m, r.PorFase.Single(f => f.Gate == 30).Fisico);              // (10 + 0) / 30
        Assert.Equal(50m, r.PorFase.Single(f => f.Gate == 60).Fisico);
        Assert.Equal(100m, r.PorFase.Single(f => f.Gate == 90).Fisico);
    }

    [Fact]
    public void Tipo2_Previsto_CorteNaSemanaVigente_PorFase()
    {
        var doc = Doc("A", 10, null, null, "SEM_PRAZO",
            G(30, 0, baseline: new DateTime(2026, 9, 1), esforco: 10),                   // vencido
            G(60, 0, baseline: new DateTime(2026, 10, 3), esforco: 30),                  // sábado da semana vigente
            G(90, 0, baseline: new DateTime(2026, 10, 4), esforco: 60));                 // domingo seguinte: fora
        var a = PmoAvancoRules.Calcular(2, Hoje, new[] { doc }, null);
        Assert.Equal(40m, a.Previsto);                                                 // (10 + 30) / 100
        Assert.Equal(0m, a.PorFase.Single(f => f.Gate == 90).Previsto);
    }

    [Fact]
    public void Tipo2_DocumentoCanceladoContinuaFora()
    {
        var cancelado = Doc("A", 10, null, null, "CANCELADO", G(30, 0, esforco: 100));
        var ok = Doc("B", 10, null, null, "SEM_PRAZO", G(30, 100, esforco: 10));
        Assert.Equal(100m, PmoAvancoRules.Calcular(2, Hoje, new[] { cancelado, ok }, null).Fisico);
    }

    [Fact]
    public void Tipo2_EsforcoLidoDoSnapshotDeWorkDoCronograma_ESomadoPorDocumentoEGate()
    {
        static string Json(decimal horas) => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        { ["__NA_CONNECT_WORK_V1"] = $"{{\"ItemHoras\":{horas.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"TotalHoras\":1000}}" });
        Assert.Equal(12.5m, PmoAvancoRules.LerEsforcoHoras(Json(12.5m)));
        Assert.Null(PmoAvancoRules.LerEsforcoHoras("{}"));
        Assert.Null(PmoAvancoRules.LerEsforcoHoras("não é json"));
        PmoTarefa T(int gate, decimal? h, bool resumo = false) => new("t", "p", "DOC", null, null, null, null, gate, null, true, resumo, null) { EsforcoHoras = h };
        var d = PmoAvancoRules.ComEsforcoDosGates(Doc("DOC", 10, null, null, "SEM_PRAZO", G(30, 0, esforco: null), G(60, 0, esforco: null), G(90, 0, esforco: null)),
            new[] { T(30, 8m), T(30, 2m), T(60, 5m), T(60, 99m, resumo: true) });
        Assert.Equal(new decimal?[] { 10m, 5m, null }, d.Gates.Select(g => g.EsforcoHoras));
    }

    [Theory]
    [InlineData("AT", null)]
    [InlineData(" at ", "XX")]
    [InlineData("XX", "AT")]
    public void LdpItemRules_AT_EhAtividade_E_NaoDocumento(string? docTypeCode, string? doc4)
    {
        Assert.True(LdpItemRules.IsActivity(docTypeCode, doc4));
        Assert.False(LdpItemRules.IsDocument(docTypeCode, doc4));
    }

    [Fact]
    public void AtividadeAT_NaoContaComoDocumento_MasContinuaNoHHRealizado()
    {
        var documento = Doc("DOC", 10, new DateTime(2026, 9, 1), new DateTime(2026, 9, 10)) with { HHRealizado = 8m };
        var atividade = Doc("AT-001", 100) with { DocTypeCode = "AT", HHRealizado = 50m };

        var curva = PmoCurvaSRules.Build("p", Hoje, new[] { documento, atividade }, 110m);
        var avanco = PmoAvancoRules.Calcular(1, Hoje, new[] { documento, atividade }, 110m);
        var hh = PmoAvancoRules.HhRealizado(new[] { documento, atividade });

        Assert.Equal(1, curva.Cobertura.TotalDocumentos);
        Assert.Equal(0, curva.Cobertura.SemBaseline); // AT sem baseline não vira documento sem baseline.
        Assert.Equal(100m, curva.Cobertura.HhAtividades); // mas o esforço planejado da atividade continua reconciliado.
        Assert.Equal(1, avanco.Documentos);
        Assert.Equal(58m, hh.Total);                  // esforço/HH da atividade continua no projeto.
    }

    [Fact]
    public void AtividadeAT_NuncaViraEmitido_AEmitir_Atrasado_Cancelado_OuSemPrazo()
    {
        var atividade = Doc("AT-001", 20, baseline: new DateTime(2026, 1, 1), emissao: null, status: "CANCELADO")
            with { Doc4 = " at ", StatusCliente = "CANCELED" };
        var concluida = PmoExecutiveRules.Completar(atividade, Array.Empty<PmoTarefa>(), 1, Hoje);

        Assert.True(concluida.IsActivity);
        Assert.Equal("ATIVIDADE", concluida.StatusDocumento);
        Assert.Equal("ATIVIDADE", concluida.StatusPrazo);
        Assert.False(concluida.Cancelado);
        Assert.Null(concluida.DataAlvo);
    }
}
