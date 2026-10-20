using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using APS.WebApp.Models;
using APS.WebApp.Services.CronogramaTipo2;
using APS.WebApp.Services.LdpTipo2;
using Microsoft.Extensions.Logging.Abstractions;
using OfficeOpenXml;
using Xunit.Abstractions;

namespace NA_Connect.LdpTipo2.Tests.DiagnosticoManual
{
    /// <summary>
    /// Diagnóstico READ-ONLY contra arquivos REAIS de LDP Tipo 2 (.xlsx) e Cronograma Tipo 2 (.mpp) —
    /// item 10 do plano de finalização. Gated por variável de ambiente: sem ela definida (ou apontando
    /// para um arquivo inexistente), o teste é um no-op automático — nunca quebra CI nem outra máquina, e
    /// nunca hardcoda caminho absoluto de projeto/máquina específico no código.
    ///
    /// Nenhum dos dois arquivos é alterado: LdpTipo2Parser/CronogramaTipo2Parser só leem (Stream/caminho
    /// de entrada), e ResolverDecisoesDeMatching é puro (nenhuma escrita, nenhuma conexão SQL).
    ///
    /// Uso:
    ///   $env:NA_CONNECT_DIAG_LDP_XLSX = "C:\caminho\LDP.xlsx"
    ///   $env:NA_CONNECT_DIAG_CRONOGRAMA_MPP = "C:\caminho\Cronograma.mpp"
    ///   dotnet test Tests\NA_Connect.LdpTipo2.Tests\NA_Connect.LdpTipo2.Tests.csproj --filter "FullyQualifiedName~DiagnosticoLdpTipo2RealTests" -l "console;verbosity=detailed"
    /// </summary>
    public class DiagnosticoLdpTipo2RealTests
    {
        private readonly ITestOutputHelper _output;

        public DiagnosticoLdpTipo2RealTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string? CaminhoXlsx => Environment.GetEnvironmentVariable("NA_CONNECT_DIAG_LDP_XLSX");
        private static string? CaminhoMpp => Environment.GetEnvironmentVariable("NA_CONNECT_DIAG_CRONOGRAMA_MPP");

        [Fact]
        public async Task Diagnostico_LDP_Real_DeveDetectarLayoutDinamicamente()
        {
            if (string.IsNullOrWhiteSpace(CaminhoXlsx) || !File.Exists(CaminhoXlsx))
            {
                _output.WriteLine("NA_CONNECT_DIAG_LDP_XLSX não definido ou arquivo inexistente — teste pulado (no-op).");
                return;
            }

            // 1) Layout dinâmico resolvido diretamente (mesma API pública usada pelo parser) — para
            // reportar a assinatura detectada de cada bloco sem depender de nada além do arquivo real.
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            using (var pkgDiagnostico = new ExcelPackage(new FileInfo(CaminhoXlsx)))
            {
                var ws = LdpTipo2HeaderResolver.LocalizarAbaLdp(pkgDiagnostico);
                var avisosLayout = new System.Collections.Generic.List<string>();
                var layout = LdpTipo2HeaderResolver.ResolverLayout(ws, NullLogger.Instance, Path.GetFileName(CaminhoXlsx), avisosLayout);

                _output.WriteLine($"Linha de cabeçalho detectada: {layout.LinhaCabecalho} (primeira linha de documento: {layout.PrimeiraLinhaDocumento})");
                _output.WriteLine($"Coluna N&A DOCUMENT NUMBER: {layout.ColunasComuns.GetValueOrDefault(LdpTipo2CabecalhosComuns.NaDocumentNumber)}");
                _output.WriteLine($"Coluna CLIENT DOCUMENT NUMBER: {(layout.ColunasComuns.TryGetValue(LdpTipo2CabecalhosComuns.ClientDocumentNumber, out var colCliente) ? colCliente.ToString() : "não encontrada")}");
                _output.WriteLine($"Coluna DOCUMENT TITLE: {layout.ColunasComuns.GetValueOrDefault(LdpTipo2CabecalhosComuns.DocumentTitle)}");

                void LogarGate(LdpTipo2GateLayout g) => _output.WriteLine(
                    $"Bloco Gate {g.Percentual}%: Fase(dados)=col {g.ColunaFaseManual} | DocumentStatus=col {g.ColunaDocumentStatus} | " +
                    $"Situation=col {g.ColunaSituation} | PlannedBaseline=col {g.ColunaPlannedBaseline} | ReplannedDate=col {g.ColunaReplannedDate} | " +
                    $"PlannedPhysical=col {g.ColunaPlannedPhysical} | PhysicalCompleted=col {g.ColunaPhysicalCompleted} | IndicadorAplicabilidade(Fase)=col {g.ColunaIndicadorAplicabilidade}");

                LogarGate(layout.Gate30);
                LogarGate(layout.Gate60);
                LogarGate(layout.Gate90);

                foreach (var aviso in avisosLayout) _output.WriteLine("AVISO (layout): " + aviso);
            }

            // 2) Parse completo (mesmo caminho usado em produção) — contagens agregadas.
            var parser = new LdpTipo2Parser(NullLogger<LdpTipo2Parser>.Instance);
            await using var stream = File.OpenRead(CaminhoXlsx);
            var resultado = await parser.ProcessarAsync(stream, "DIAG-CHS001", Path.GetFileName(CaminhoXlsx), DateTime.UtcNow);

            _output.WriteLine("");
            _output.WriteLine($"Linhas analisadas: {resultado.LinhasAnalisadas}");
            _output.WriteLine($"Documentos encontrados: {resultado.DocumentosEncontrados}");
            _output.WriteLine($"Gate30 ativos: {resultado.Documentos.Count(d => d.Gates.Any(g => g.GatePercentual == 30 && g.GateAtivo))}");
            _output.WriteLine($"Gate60 ativos: {resultado.Documentos.Count(d => d.Gates.Any(g => g.GatePercentual == 60 && g.GateAtivo))}");
            _output.WriteLine($"Gate90 ativos: {resultado.Documentos.Count(d => d.Gates.Any(g => g.GatePercentual == 90 && g.GateAtivo))}");
            _output.WriteLine($"Gates ativos (total): {resultado.GatesAtivos} | Gates inativos (total): {resultado.GatesInativos}");

            int comCliente = resultado.Documentos.Count(d => !string.IsNullOrWhiteSpace(d.NumeroDocumentoCliente));
            _output.WriteLine($"Documentos com Client Document Number: {comCliente} | sem: {resultado.Documentos.Count - comCliente}");

            var duplicadosNa = resultado.Avisos.Where(a => a.Contains("N&A_DUPLICADO")).ToList();
            var duplicadosClient = resultado.Avisos.Where(a => a.Contains("CLIENT_DUPLICADO")).ToList();
            _output.WriteLine($"Duplicidades de N&A: {duplicadosNa.Count}");
            foreach (var a in duplicadosNa) _output.WriteLine("  " + a);
            _output.WriteLine($"Duplicidades de Client: {duplicadosClient.Count}");
            foreach (var a in duplicadosClient) _output.WriteLine("  " + a);

            var invalidos = resultado.Avisos.Where(a => a.Contains("valor inesperado no indicador de aplicabilidade")).ToList();
            _output.WriteLine($"Valores inválidos de aplicabilidade: {invalidos.Count}");
            foreach (var a in invalidos) _output.WriteLine("  " + a);

            _output.WriteLine($"Total de avisos: {resultado.Avisos.Count} | Total de erros: {resultado.Erros.Count}");
            foreach (var erro in resultado.Erros) _output.WriteLine("ERRO: " + erro);

            Assert.Empty(resultado.Erros);
        }

        [Fact]
        public async Task Diagnostico_Matching_Real_LDP_x_Cronograma()
        {
            if (string.IsNullOrWhiteSpace(CaminhoXlsx) || string.IsNullOrWhiteSpace(CaminhoMpp))
            {
                _output.WriteLine("NA_CONNECT_DIAG_LDP_XLSX e/ou NA_CONNECT_DIAG_CRONOGRAMA_MPP não definidos — teste pulado (no-op).");
                return;
            }

            if (!File.Exists(CaminhoXlsx) || !File.Exists(CaminhoMpp))
            {
                _output.WriteLine("Um dos arquivos apontados pelas variáveis de ambiente não existe — teste pulado (no-op).");
                return;
            }

            var ldpParser = new LdpTipo2Parser(NullLogger<LdpTipo2Parser>.Instance);
            LdpTipo2Resultado ldp;
            await using (var stream = File.OpenRead(CaminhoXlsx))
            {
                ldp = await ldpParser.ProcessarAsync(stream, "DIAG-CHS001", Path.GetFileName(CaminhoXlsx), DateTime.UtcNow);
            }

            var mppDiagnostico = new net.sf.mpxj.reader.UniversalProjectReader().read(CaminhoMpp);
            var resolver = new CronogramaTipo2FieldResolver(mppDiagnostico);
            _output.WriteLine($"Campos customizados com alias no MPP: {resolver.TotalCamposComAlias}");
            foreach (var kv in resolver.AliasesOriginais) _output.WriteLine($"  Alias: '{kv.Key}' -> {kv.Value}");

            var cronogramaParser = new CronogramaTipo2Parser(NullLogger<CronogramaTipo2Parser>.Instance);
            var cronograma = await cronogramaParser.ProcessarAsync(CaminhoMpp, "DIAG-CHS001", tipoLdp: 2, Path.GetFileName(CaminhoMpp), DateTime.UtcNow);

            _output.WriteLine($"LDP: {ldp.DocumentosEncontrados} documento(s), {ldp.Erros.Count} erro(s).");
            _output.WriteLine($"Cronograma: {cronograma.TarefasAnalisadas} tarefa(s) analisadas, {cronograma.RegistrosValidos} válida(s), {cronograma.Erros.Count} erro(s).");
            _output.WriteLine($"  Resumo={cronograma.TarefasResumo} | SemNumeroNA={cronograma.TarefasSemNumeroNa} | SemFase={cronograma.TarefasSemFase} | FaseInvalida={cronograma.TarefasComFaseInvalida} | Duplicados={cronograma.RegistrosDuplicados}");
            foreach (var r in cronograma.Registros.Where(r => r.StatusValidacaoDocumentoGate != CronogramaTipo2Parser.Status.Valido).GroupBy(r => r.StatusValidacaoDocumentoGate))
            {
                _output.WriteLine($"  Status '{r.Key}': {r.Count()} tarefa(s). Exemplo: NA='{r.First().NumeroDocumentoNaOriginal}' Fase='{r.First().FaseOriginal}' Msg='{r.First().MensagemValidacaoDocumentoGate}'");
            }
            foreach (var a in cronograma.Avisos.Take(5)) _output.WriteLine("  AVISO CRONOGRAMA: " + a);

            if (ldp.Erros.Count > 0 || cronograma.Erros.Count > 0)
            {
                foreach (var e in ldp.Erros) _output.WriteLine("ERRO LDP: " + e);
                foreach (var e in cronograma.Erros) _output.WriteLine("ERRO CRONOGRAMA: " + e);
                return;
            }

            // Adaptação em memória: LdpTipo2Gate -> GateLdpParaSincronizacao. IDs sintéticos/sequenciais —
            // nunca gravados em banco, só para exercitar o algoritmo puro de matching contra dado real.
            var gatesLdp = ldp.Documentos
                .SelectMany((doc, docIndex) => doc.Gates.Select((g, gateIndex) => new GateLdpParaSincronizacao
                {
                    Id = docIndex * 3L + gateIndex,
                    LdpRegistroId = docIndex,
                    ProjectIdAcc = "DIAG-CHS001",
                    NumeroDocumentoNa = g.NumeroDocumentoNa,
                    NumeroDocumentoCliente = g.NumeroDocumentoCliente,
                    GatePercentual = g.GatePercentual,
                    GateAtivo = g.GateAtivo
                }))
                .ToList();

            var candidatos = cronograma.Registros
                .Where(r => r.StatusValidacaoDocumentoGate == CronogramaTipo2Parser.Status.Valido)
                .ToList();

            _output.WriteLine($"Candidatos válidos do cronograma para matching: {candidatos.Count}");

            var (decisoes, candidatosUsados) = CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching(gatesLdp, candidatos);

            var porStatus = decisoes.GroupBy(d => d.Status).ToDictionary(g => g.Key, g => g.Count());
            foreach (var kv in porStatus) _output.WriteLine($"Status {kv.Key}: {kv.Value}");

            var porOrigem = decisoes.Where(d => d.OrigemMatch != null).GroupBy(d => d.OrigemMatch).ToDictionary(g => g.Key!, g => g.Count());
            _output.WriteLine($"Matches via {OrigemMatchLdp.Na}: {porOrigem.GetValueOrDefault(OrigemMatchLdp.Na)}");
            _output.WriteLine($"Matches via {OrigemMatchLdp.Cliente}: {porOrigem.GetValueOrDefault(OrigemMatchLdp.Cliente)}");
            _output.WriteLine($"Candidatos do cronograma sem gate correspondente na LDP: {candidatos.Count - candidatosUsados.Count}");

            foreach (var d in decisoes.Where(d => d.OrigemMatch == OrigemMatchLdp.Cliente).Take(10))
            {
                _output.WriteLine($"  Exemplo MATCH_CLIENT: NumeroNA canônico='{d.Gate.NumeroDocumentoNa}' | Client='{d.Gate.NumeroDocumentoCliente}' | Gate={d.Gate.GatePercentual}%");
            }
        }
    }
}
