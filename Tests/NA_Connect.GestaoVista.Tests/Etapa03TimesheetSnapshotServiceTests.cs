using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;
using APS.WebApp.Services.GestaoVista;
using Microsoft.Extensions.Logging.Abstractions;
using NA_Connect.GestaoVista.Tests.TestHelpers;
using OfficeOpenXml;

namespace NA_Connect.GestaoVista.Tests
{
    /// <summary>
    /// Correção 17/09/2026 (nova ETAPA 03 — snapshot do Timesheet para Gestão à Vista): cobre a
    /// orquestração real de Etapa03TimesheetSnapshotService com o extrator REAL (GestaoVistaExtractor,
    /// puro, sem banco) contra workbooks sintéticos em memória (mesmo padrão de
    /// LdpGestaoVistaSyncServiceResilienceTests) e um fake em memória de
    /// IGestaoVistaTimesheetSnapshotRepository (nenhum SQL Server real necessário) — simula DELETE+INSERT
    /// por Project_ID_ACC, isolamento entre projetos, e o sinal de divergência que o repositório real
    /// devolveria. A regra de tolerância em si (DentroDaTolerancia) é testada separadamente, como função
    /// pura. Confirmação dos números literais do pedido (1385 linhas, 5447.68h) é feita na validação real
    /// do CHS001 — aqui a mesma mecânica é provada com volumes menores, mais rápidos de montar.
    /// </summary>
    public class Etapa03TimesheetSnapshotServiceTests
    {
        private sealed class FakeSnapshotRepository : IGestaoVistaTimesheetSnapshotRepository
        {
            public bool TabelaDisponivel = true;
            public bool ForcarDivergenciaNaProximaChamada = false;
            public readonly Dictionary<string, List<GestaoVistaTimesheetSnapshotLinha>> Estado = new();
            public readonly Dictionary<string, decimal> CustoHoraPorNome = new();
            public readonly List<string> ConsultasTaxa = new();
            public int ChamadasSubstituir;
            public readonly List<IReadOnlyList<GestaoVistaTimesheetSnapshotLinha>> LinhasRecebidasPorChamada = new();

            public Task<bool> TabelaDisponivelAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(TabelaDisponivel);

            public Task<(int Linhas, decimal HH)> ObterResumoAtualAsync(string projectIdAcc, CancellationToken cancellationToken = default)
            {
                if (!Estado.TryGetValue(projectIdAcc, out var linhas)) return Task.FromResult((0, 0m));
                return Task.FromResult((linhas.Count, linhas.Sum(l => l.HorasTrabalhadas ?? 0m)));
            }

            public Task<decimal?> ObterCustoHoraVigenteAsync(string projectIdAcc, string nomeProfissional, DateTime dataReferencia, CancellationToken cancellationToken = default)
            {
                ConsultasTaxa.Add(nomeProfissional);
                return Task.FromResult(CustoHoraPorNome.TryGetValue(nomeProfissional ?? string.Empty, out var v) ? (decimal?)v : null);
            }

            public Task<GestaoVistaTimesheetSnapshotCommitResultado> SubstituirSnapshotAsync(
                string projectIdAcc, IReadOnlyList<GestaoVistaTimesheetSnapshotLinha> linhas, decimal hhEsperadoExcel,
                DateTime dataSincronizacao, CancellationToken cancellationToken = default)
            {
                ChamadasSubstituir++;
                LinhasRecebidasPorChamada.Add(linhas);

                var antes = Estado.TryGetValue(projectIdAcc, out var existentes) ? existentes : new List<GestaoVistaTimesheetSnapshotLinha>();
                int linhasAntes = antes.Count;
                decimal hhAntes = antes.Sum(l => l.HorasTrabalhadas ?? 0m);
                decimal somaCandidata = linhas.Sum(l => l.HorasTrabalhadas ?? 0m);

                bool divergente = ForcarDivergenciaNaProximaChamada || !GestaoVistaTimesheetSnapshotRepository.DentroDaTolerancia(hhEsperadoExcel, somaCandidata);
                ForcarDivergenciaNaProximaChamada = false;

                if (divergente)
                {
                    return Task.FromResult(new GestaoVistaTimesheetSnapshotCommitResultado
                    {
                        Sucesso = false,
                        Motivo = "DIVERGENCIA_EXCEL_BANCO",
                        LinhasAntes = linhasAntes,
                        HHAntes = hhAntes,
                        Removidos = 0,
                        LinhasInseridas = 0,
                        DiferencaExcelBanco = Math.Abs(hhEsperadoExcel - somaCandidata),
                        Erros = new List<string> { "divergência (simulada ou real) entre HH do Excel e HH que seria persistido" },
                    });
                }

                // DELETE + INSERT — só as linhas DESTE Project_ID_ACC; outros projetos no dicionário
                // nunca são tocados (mesma garantia que o repositório real dá via WHERE Project_ID_ACC).
                Estado[projectIdAcc] = linhas.ToList();

                return Task.FromResult(new GestaoVistaTimesheetSnapshotCommitResultado
                {
                    Sucesso = true,
                    Motivo = "OK",
                    LinhasAntes = linhasAntes,
                    HHAntes = hhAntes,
                    Removidos = linhasAntes,
                    LinhasInseridas = linhas.Count,
                    LinhasDepois = linhas.Count,
                    HHDepois = somaCandidata,
                    DiferencaExcelBanco = Math.Abs(hhEsperadoExcel - somaCandidata),
                });
            }
        }

        private static string EscreverWorkbookTemporario(ExcelPackage pkg)
        {
            string caminho = Path.Combine(Path.GetTempPath(), $"ETAPA03_TESTE_{Guid.NewGuid()}.xlsx");
            File.WriteAllBytes(caminho, pkg.GetAsByteArray());
            return caminho;
        }

        private static (Etapa03TimesheetSnapshotService Servico, FakeSnapshotRepository Repo) NovoServico()
        {
            var extractor = new GestaoVistaExtractor(NullLogger<GestaoVistaExtractor>.Instance);
            var repo = new FakeSnapshotRepository();
            var servico = new Etapa03TimesheetSnapshotService(extractor, repo, NullLogger<Etapa03TimesheetSnapshotService>.Instance);
            return (servico, repo);
        }

        private static string MontarEEscreverTimesheet(int quantidadeLinhas, out decimal hhEsperado, bool incluirData = false)
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarTimesheet(pkg, eWorkSheetHidden.Visible, linhaCabecalho: 1, incluirData: incluirData);

            hhEsperado = 0m;
            for (int i = 0; i < quantidadeLinhas; i++)
            {
                decimal horas = 4 + (i % 5);
                hhEsperado += horas;
                var valores = new List<object> { $"Pessoa{i}", 8m, horas, "CHS001", "Engenheiro", "PROCESSO", $"DOC-{i}", $"Documento {i}.docx", "Desc" };
                if (incluirData) valores.Add(null);
                GestaoVistaWorkbookHelper.EscreverLinha(ws, i + 2, valores.ToArray());
            }

            return EscreverWorkbookTemporario(pkg);
        }

        [Fact]
        public async Task NLinhasNoExcel_BancoFinalTemExatamenteNLinhas()
        {
            var (servico, repo) = NovoServico();
            string caminho = MontarEEscreverTimesheet(50, out decimal hhEsperado);
            try
            {
                var resultado = await servico.ExecutarAsync("PROJ-A", caminho, "teste.xlsx", DateTime.UtcNow);

                Assert.True(resultado.Sucesso);
                Assert.Equal(50, resultado.Inseridos);
                Assert.Equal(50, repo.Estado["PROJ-A"].Count);
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task SomaDeHorasDoExcel_BateComSomaFinalNoBanco()
        {
            var (servico, repo) = NovoServico();
            string caminho = MontarEEscreverTimesheet(30, out decimal hhEsperado);
            try
            {
                var resultado = await servico.ExecutarAsync("PROJ-A", caminho, "teste.xlsx", DateTime.UtcNow);

                Assert.True(resultado.Sucesso);
                Assert.Equal(hhEsperado, resultado.HHExcel);
                Assert.Equal(hhEsperado, resultado.HHBancoDepois);
                Assert.Equal(hhEsperado, repo.Estado["PROJ-A"].Sum(l => l.HorasTrabalhadas ?? 0m));
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task SnapshotAnteriorComValorDiferente_NaoEntraNoKpiAtual_EhSubstituidoPorCompleto()
        {
            var (servico, repo) = NovoServico();
            // Snapshot antigo, de uma execução anterior, com valores completamente diferentes.
            repo.Estado["PROJ-A"] = new List<GestaoVistaTimesheetSnapshotLinha>
            {
                new() { ProjectIdAcc = "PROJ-A", NomeProfissional = "Fantasma", HorasTrabalhadas = 999m, DocumentoCodigo = "ANTIGO" },
            };

            string caminho = MontarEEscreverTimesheet(10, out decimal hhEsperado);
            try
            {
                var resultado = await servico.ExecutarAsync("PROJ-A", caminho, "teste.xlsx", DateTime.UtcNow);

                Assert.True(resultado.Sucesso);
                Assert.DoesNotContain(repo.Estado["PROJ-A"], l => l.DocumentoCodigo == "ANTIGO");
                Assert.Equal(10, repo.Estado["PROJ-A"].Count);
                Assert.Equal(hhEsperado, repo.Estado["PROJ-A"].Sum(l => l.HorasTrabalhadas ?? 0m));
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task NenhumaLinhaDeOutroProjectIdAcc_EhRemovida()
        {
            var (servico, repo) = NovoServico();
            repo.Estado["PROJ-OUTRO"] = new List<GestaoVistaTimesheetSnapshotLinha>
            {
                new() { ProjectIdAcc = "PROJ-OUTRO", NomeProfissional = "Intocavel", HorasTrabalhadas = 42m, DocumentoCodigo = "NUNCA-MEXER" },
            };

            string caminho = MontarEEscreverTimesheet(5, out _);
            try
            {
                await servico.ExecutarAsync("PROJ-A", caminho, "teste.xlsx", DateTime.UtcNow);

                Assert.Single(repo.Estado["PROJ-OUTRO"]);
                Assert.Equal("NUNCA-MEXER", repo.Estado["PROJ-OUTRO"][0].DocumentoCodigo);
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task DivergenciaExcelBanco_ResultaEmRollback_SnapshotAnteriorPreservado()
        {
            var (servico, repo) = NovoServico();
            repo.Estado["PROJ-A"] = new List<GestaoVistaTimesheetSnapshotLinha>
            {
                new() { ProjectIdAcc = "PROJ-A", NomeProfissional = "Original", HorasTrabalhadas = 100m, DocumentoCodigo = "ORIGINAL" },
            };
            repo.ForcarDivergenciaNaProximaChamada = true;

            string caminho = MontarEEscreverTimesheet(5, out _);
            try
            {
                var resultado = await servico.ExecutarAsync("PROJ-A", caminho, "teste.xlsx", DateTime.UtcNow);

                Assert.False(resultado.Sucesso);
                Assert.Equal("DIVERGENCIA_EXCEL_BANCO", resultado.Motivo);
                // Rollback: o snapshot anterior nunca foi tocado pelo fake (só sobrescreve em caso de sucesso).
                Assert.Single(repo.Estado["PROJ-A"]);
                Assert.Equal("ORIGINAL", repo.Estado["PROJ-A"][0].DocumentoCodigo);
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task DuasExecucoesIdenticas_MesmoResultadoFinal_SemDuplicacao()
        {
            var (servico, repo) = NovoServico();
            string caminho = MontarEEscreverTimesheet(20, out decimal hhEsperado);
            try
            {
                var primeira = await servico.ExecutarAsync("PROJ-A", caminho, "teste.xlsx", DateTime.UtcNow);
                var segunda = await servico.ExecutarAsync("PROJ-A", caminho, "teste.xlsx", DateTime.UtcNow);

                Assert.True(primeira.Sucesso);
                Assert.True(segunda.Sucesso);
                Assert.Equal(2, repo.ChamadasSubstituir);
                // Nunca 40 (20+20) — a segunda chamada substitui, não acumula.
                Assert.Equal(20, repo.Estado["PROJ-A"].Count);
                Assert.Equal(hhEsperado, repo.Estado["PROJ-A"].Sum(l => l.HorasTrabalhadas ?? 0m));
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task AbaSemColunaDeData_DataLancamentoContinuaNulaNoSnapshot_NuncaInventada()
        {
            var (servico, repo) = NovoServico();
            string caminho = MontarEEscreverTimesheet(5, out _, incluirData: false);
            try
            {
                var resultado = await servico.ExecutarAsync("PROJ-A", caminho, "teste.xlsx", DateTime.UtcNow);

                Assert.True(resultado.Sucesso);
                Assert.All(repo.Estado["PROJ-A"], l => Assert.Null(l.DataLancamento));
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task TabelaSnapshotIndisponivel_FalhaComMotivoExplicito_NuncaTentaSubstituir()
        {
            var (servico, repo) = NovoServico();
            repo.TabelaDisponivel = false;
            string caminho = MontarEEscreverTimesheet(5, out _);
            try
            {
                var resultado = await servico.ExecutarAsync("PROJ-A", caminho, "teste.xlsx", DateTime.UtcNow);

                Assert.False(resultado.Sucesso);
                Assert.Equal("TABELA_SNAPSHOT_NAO_DISPONIVEL", resultado.Motivo);
                Assert.Equal(0, repo.ChamadasSubstituir);
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task CustoCalculado_UsaCustoHoraVigenteDoRepositorio_MesmaRegraDaGestaoAVista()
        {
            var (servico, repo) = NovoServico();
            repo.CustoHoraPorNome["Pessoa0"] = 100m;
            string caminho = MontarEEscreverTimesheet(1, out _);
            try
            {
                var resultado = await servico.ExecutarAsync("PROJ-A", caminho, "teste.xlsx", DateTime.UtcNow);

                Assert.True(resultado.Sucesso);
                var linha = repo.Estado["PROJ-A"].Single();
                Assert.Equal(linha.HorasTrabalhadas!.Value * 100m, linha.CustoCalculado);
            }
            finally { File.Delete(caminho); }
        }

        // Correção 2026-09-22 (BUG 3 — Custo Real): CustoInformadoNaPlanilha (coluna "15. CUSTO" da
        // própria aba Timesheet) passa a ter prioridade sobre o recálculo independente
        // (HorasTrabalhadas × ObterCustoHoraVigenteAsync) — auditoria real BML011.013 encontrou 3
        // profissionais (Antonio Clareti, Everton Eustaquio, Tim Chowdhury) com custo válido na planilha
        // mas sem taxa vigente completa/correta na tabela de composição de custo, perdendo R$271.988,00.
        private static string MontarTimesheetComCusto(IReadOnlyList<(string Nome, decimal Horas, decimal? Custo)> pessoas)
        {
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarTimesheet(pkg, eWorkSheetHidden.Visible, linhaCabecalho: 1, incluirCusto: true);
            for (int i = 0; i < pessoas.Count; i++)
            {
                var p = pessoas[i];
                GestaoVistaWorkbookHelper.EscreverLinha(ws, i + 2,
                    p.Nome, 8m, p.Horas, "BML011.013", "Engenheiro", "PROCESSO", $"DOC-{i}", $"Documento {i}.docx", "Desc",
                    p.Custo.HasValue ? (object)p.Custo.Value : null);
            }
            string caminho = Path.Combine(Path.GetTempPath(), $"ETAPA03_CUSTO_TESTE_{Guid.NewGuid()}.xlsx");
            File.WriteAllBytes(caminho, pkg.GetAsByteArray());
            return caminho;
        }

        [Fact]
        public async Task ProfissionalSemTaxaVigenteMasComCustoNaPlanilha_UsaOValorDaPlanilha_NuncaFicaNulo()
        {
            // Everton Eustaquio / Tim Chowdhury: ObterCustoHoraVigenteAsync devolve null (sem registro),
            // mas a linha tem custo válido na própria planilha — o custo NUNCA pode desaparecer.
            var (servico, repo) = NovoServico();
            string caminho = MontarTimesheetComCusto(new[] { ("Everton Eustaquio", 447.08m, (decimal?)44708.00m) });
            try
            {
                var resultado = await servico.ExecutarAsync("BML", caminho, "teste.xlsx", DateTime.UtcNow);
                Assert.True(resultado.Sucesso);
                var linha = repo.Estado["BML"].Single();
                Assert.Equal(44708.00m, linha.CustoCalculado);
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task ProfissionalComTaxaVigenteZeradaMasComCustoNaPlanilha_UsaOValorDaPlanilha()
        {
            // Antonio Clareti: ObterCustoHoraVigenteAsync devolve 0 (taxa cadastrada errada/zerada), mas a
            // planilha tem R$250/h × 880h = R$220.000 — a planilha prevalece, nunca o recálculo zerado.
            var (servico, repo) = NovoServico();
            repo.CustoHoraPorNome["Antonio Clareti"] = 0m;
            string caminho = MontarTimesheetComCusto(new[] { ("Antonio Clareti", 880m, (decimal?)220000.00m) });
            try
            {
                var resultado = await servico.ExecutarAsync("BML", caminho, "teste.xlsx", DateTime.UtcNow);
                Assert.True(resultado.Sucesso);
                var linha = repo.Estado["BML"].Single();
                Assert.Equal(220000.00m, linha.CustoCalculado);
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task TresProfissionaisComBugMaisCincoQueJaFuncionam_TodosEntramCorretamente()
        {
            var (servico, repo) = NovoServico();
            // 5 profissionais "que já funcionam": sem custo na planilha, mas com taxa vigente normal —
            // continuam recalculando por HH × taxa, comportamento preservado.
            repo.CustoHoraPorNome["Recurso1"] = 80m; repo.CustoHoraPorNome["Recurso2"] = 90m;
            repo.CustoHoraPorNome["Recurso3"] = 100m; repo.CustoHoraPorNome["Recurso4"] = 110m; repo.CustoHoraPorNome["Recurso5"] = 120m;
            repo.CustoHoraPorNome["Antonio Clareti"] = 0m; // taxa zerada/bugada
            var pessoas = new List<(string, decimal, decimal?)>
            {
                ("Antonio Clareti", 880m, 220000.00m),
                ("Everton Eustaquio", 447.08m, 44708.00m),
                ("Tim Chowdhury", 56m, 7280.00m),
                ("Recurso1", 10m, null), ("Recurso2", 10m, null), ("Recurso3", 10m, null), ("Recurso4", 10m, null), ("Recurso5", 10m, null),
            };
            string caminho = MontarTimesheetComCusto(pessoas);
            try
            {
                var resultado = await servico.ExecutarAsync("BML", caminho, "teste.xlsx", DateTime.UtcNow);
                Assert.True(resultado.Sucesso);
                var porNome = repo.Estado["BML"].ToDictionary(l => l.NomeProfissional, l => l.CustoCalculado);

                Assert.Equal(220000.00m, porNome["Antonio Clareti"]);
                Assert.Equal(44708.00m, porNome["Everton Eustaquio"]);
                Assert.Equal(7280.00m, porNome["Tim Chowdhury"]);
                Assert.Equal(800m, porNome["Recurso1"]); Assert.Equal(900m, porNome["Recurso2"]);
                Assert.Equal(1000m, porNome["Recurso3"]); Assert.Equal(1100m, porNome["Recurso4"]); Assert.Equal(1200m, porNome["Recurso5"]);

                // Reconciliação: SUM custo real dos lançamentos = soma de todos os CustoCalculado conhecidos.
                var somaEsperada = 220000.00m + 44708.00m + 7280.00m + 800m + 900m + 1000m + 1100m + 1200m;
                Assert.Equal(somaEsperada, repo.Estado["BML"].Sum(l => l.CustoCalculado ?? 0m));
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task CustoDaPlanilhaNaoConsultaTaxa_FallbackESemCustoPreservamHoras()
        {
            var (servico, repo) = NovoServico();
            repo.CustoHoraPorNome["Fallback"] = 75m;
            string caminho = MontarTimesheetComCusto(new[] {
                ("Antonio Clareti", 880m, (decimal?)220000m),
                ("Everton Eustaquio", 447.08m, (decimal?)44708m),
                ("Tim Chowdhury", 56m, (decimal?)7280m),
                ("Zero informado", 1m, (decimal?)0m),
                ("Fallback", 10m, (decimal?)null),
                ("Sem taxa", 3m, (decimal?)null)
            });
            try
            {
                var resultado = await servico.ExecutarAsync("BML", caminho, "teste.xlsx", DateTime.UtcNow);
                Assert.True(resultado.Sucesso);
                Assert.Equal(new[] { "Fallback", "Sem taxa" }, repo.ConsultasTaxa);
                var rows = repo.Estado["BML"];
                Assert.Equal(750m, rows.Single(l => l.NomeProfissional == "Fallback").CustoCalculado);
                var semTaxa = rows.Single(l => l.NomeProfissional == "Sem taxa");
                Assert.Null(semTaxa.CustoCalculado);
                Assert.Equal(3m, semTaxa.HorasTrabalhadas);
                Assert.Equal(1397.08m, resultado.HHBancoDepois);
                Assert.Equal(272738m, rows.Sum(l => l.CustoCalculado ?? 0m));
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task FormulaSemCacheAteSnapshot_ReconciliaTotaisBmlSemAlterarHh()
        {
            // Fixture agregada dos valores informados na auditoria, não cópia da LDP real.
            var (servico, repo) = NovoServico();
            using var pkg = GestaoVistaWorkbookHelper.NovoPacote();
            var ws = GestaoVistaWorkbookHelper.MontarTimesheet(pkg);
            ws.Cells[1, 15].Value = "15. CUSTO"; // resolução por header, não posição do helper
            var taxas = pkg.Workbook.Worksheets.Add("Taxas Fixture");
            var pessoas = new[] {
                (Nome: "Antonio Clareti", Hh: 880m, Taxa: 250m),
                (Nome: "Everton Eustaquio", Hh: 447.08m, Taxa: 100m),
                (Nome: "Tim Chowdhury", Hh: 56m, Taxa: 130m)
            };
            for (int i = 0; i < pessoas.Length; i++)
            {
                int row = i + 2;
                var p = pessoas[i];
                GestaoVistaWorkbookHelper.EscreverLinha(ws, row, p.Nome, 0m, p.Hh, "BML", "", "", "", "", "");
                taxas.Cells[row, 3].Value = p.Nome;
                taxas.Cells[row, 9].Value = p.Taxa;
                ws.Cells[row, 15].Formula = $"C{row}*SUMIF('Taxas Fixture'!C:C,'{ws.Name}'!A{row},'Taxas Fixture'!I:I)";
                Assert.Null(ws.Cells[row, 15].Value);
            }
            GestaoVistaWorkbookHelper.EscreverLinha(ws, 5, "Demais lançamentos (agregados)", 0m, 5723.98m, "BML", "", "", "", "", "");
            ws.Cells[5, 15].Value = 492731.8739m;
            string caminho = EscreverWorkbookTemporario(pkg);
            byte[] original = File.ReadAllBytes(caminho);
            try
            {
                var resultado = await servico.ExecutarAsync("BML", caminho, "fixture.xlsx", DateTime.UtcNow);
                Assert.True(resultado.Sucesso);
                Assert.Empty(repo.ConsultasTaxa);
                Assert.Equal(7107.06m, resultado.HHExcel);
                Assert.Equal(7107.06m, resultado.HHBancoDepois);
                Assert.Equal(764719.8739m, repo.Estado["BML"].Sum(l => l.CustoCalculado ?? 0m));
                Assert.Equal(original, File.ReadAllBytes(caminho));
            }
            finally { File.Delete(caminho); }
        }

        [Fact]
        public async Task LdpLegadaSemColunaCusto_ContinuaRecalculandoPorTaxaVigente_ComportamentoAnteriorPreservado()
        {
            // NOVO não substitui o que já funcionava: LDP sem a coluna CUSTO (incluirCusto:false) —
            // CustoInformadoNaPlanilha é sempre null, então o recálculo por taxa vigente continua a única fonte.
            var (servico, repo) = NovoServico();
            repo.CustoHoraPorNome["Pessoa0"] = 75m;
            string caminho = MontarEEscreverTimesheet(1, out _);
            try
            {
                var resultado = await servico.ExecutarAsync("PROJ-A", caminho, "teste.xlsx", DateTime.UtcNow);
                Assert.True(resultado.Sucesso);
                var linha = repo.Estado["PROJ-A"].Single();
                Assert.Equal(linha.HorasTrabalhadas!.Value * 75m, linha.CustoCalculado);
            }
            finally { File.Delete(caminho); }
        }
    }

    /// <summary>Regra pura de resolução de custo (Etapa03CustoRules) — sem I/O, cobre exatamente a
    /// prioridade planilha &gt; recálculo, e o caso sem nenhuma das duas fontes.</summary>
    public class Etapa03CustoRulesTests
    {
        [Fact]
        public void PlanilhaComValor_TemPrioridadeSobreRecalculo()
            => Assert.Equal(220000m, APS.WebApp.Services.GestaoVista.Etapa03CustoRules.ResolverCustoCalculado(220000m, 880m, 0m));
        [Fact]
        public void PlanilhaComZeroExplicito_NaoDisparaFallback_ZeroEhUmValorReal()
            => Assert.Equal(0m, APS.WebApp.Services.GestaoVista.Etapa03CustoRules.ResolverCustoCalculado(0m, 10m, 999m));
        [Fact]
        public void SemPlanilha_RecalculaPorHhVezesTaxa()
            => Assert.Equal(750m, APS.WebApp.Services.GestaoVista.Etapa03CustoRules.ResolverCustoCalculado(null, 10m, 75m));
        [Fact]
        public void SemPlanilhaSemTaxaVigente_ResultaNuloNuncaZeroInventado()
            => Assert.Null(APS.WebApp.Services.GestaoVista.Etapa03CustoRules.ResolverCustoCalculado(null, 10m, null));
        [Fact]
        public void SemPlanilhaSemHoras_ResultaNulo()
            => Assert.Null(APS.WebApp.Services.GestaoVista.Etapa03CustoRules.ResolverCustoCalculado(null, null, 75m));
    }
}
