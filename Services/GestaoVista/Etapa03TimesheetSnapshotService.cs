using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;
using Microsoft.Extensions.Logging;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Implementação real da ETAPA 03. Fluxo: abre a LDP local (já reconstruída pela Etapa 01) -> extrai
    /// a aba TIMESHEET (reaproveita IGestaoVistaExtractor/GestaoVistaTimesheetParser — leitura por
    /// cabeçalho normalizado, nunca posição fixa) -> resolve CustoCalculado por linha (mesma regra de
    /// IGestaoVistaRepository.ObterCustoHoraVigenteAsync) -> substitui o snapshot atual em UMA transação
    /// (IGestaoVistaTimesheetSnapshotRepository.SubstituirSnapshotAsync, que só comita se
    /// HHExcel==HHBanco dentro da tolerância). Nunca lança exceção para o chamador — sempre devolve um
    /// GestaoVistaTimesheetSnapshotResultado com Sucesso e Motivo preenchidos.
    /// </summary>
    public sealed class Etapa03TimesheetSnapshotService : IEtapa03TimesheetSnapshotService
    {
        private readonly IGestaoVistaExtractor _extractor;
        private readonly IGestaoVistaTimesheetSnapshotRepository _repository;
        private readonly ILogger<Etapa03TimesheetSnapshotService> _logger;

        public Etapa03TimesheetSnapshotService(
            IGestaoVistaExtractor extractor, IGestaoVistaTimesheetSnapshotRepository repository,
            ILogger<Etapa03TimesheetSnapshotService> logger)
        {
            _extractor = extractor;
            _repository = repository;
            _logger = logger;
        }

        public async Task<GestaoVistaTimesheetSnapshotResultado> ExecutarAsync(
            string projectIdAcc, string ldpPathLocal, string nomeArquivo, DateTime dataSincronizacao,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("[ETAPA 03][INICIO]");

            try
            {
                GestaoVistaExtracaoResultado extracao;
                using (var stream = new FileStream(ldpPathLocal, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    extracao = _extractor.Processar(stream, projectIdAcc, nomeArquivo);
                }

                if (extracao.Erros.Count > 0)
                {
                    return Falha("EXTRACAO_FALHOU", extracao.Erros);
                }

                var diagTimesheet = extracao.Worksheets.Find(d => d.PapelIdentificado == "Timesheet");
                if (diagTimesheet == null || !diagTimesheet.Encontrada)
                {
                    return Falha("ABA_TIMESHEET_NAO_ENCONTRADA",
                        new List<string> { "Aba TIMESHEET não encontrada ou sem cabeçalho reconhecido na LDP publicada pela Etapa 01." });
                }

                var linhas = extracao.Timesheet;
                int linhasExcel = linhas.Count;
                decimal hhExcel = linhas.Sum(l => l.HorasTrabalhadas ?? 0m);
                _logger.LogInformation("[ETAPA 03][TIMESHEET][EXCEL] Linhas={Linhas} | HHTrabalhadas={HHTrabalhadas}", linhasExcel, hhExcel);

                bool tabelaDisponivel = await _repository.TabelaDisponivelAsync(cancellationToken);
                if (!tabelaDisponivel)
                {
                    return Falha("TABELA_SNAPSHOT_NAO_DISPONIVEL",
                        new List<string> { "gestao.TimesheetSnapshotAtual não existe neste banco — migration manual pendente." });
                }

                var antes = await _repository.ObterResumoAtualAsync(projectIdAcc, cancellationToken);
                _logger.LogInformation("[ETAPA 03][TIMESHEET][BANCO_ANTES] Linhas={Linhas} | HHTrabalhadas={HHTrabalhadas}", antes.Linhas, antes.HH);

                var linhasParaPersistir = new List<GestaoVistaTimesheetSnapshotLinha>(linhas.Count);
                int viaPlanilha = 0, viaPlanilhaZero = 0, viaRecalculo = 0, semCusto = 0;
                decimal totalCustoPlanilha = 0m, totalCustoCalculado = 0m;
                foreach (var l in linhas)
                {
                    DateTime dataReferencia = l.DataLancamento ?? dataSincronizacao;
                    // A taxa é fallback: sua consulta não pode impedir a persistência de um custo
                    // já válido na Timesheet (inclusive zero explícito).
                    decimal? custoHoraVigente = l.CustoInformadoNaPlanilha.HasValue ? null
                        : await _repository.ObterCustoHoraVigenteAsync(
                            projectIdAcc, l.NomeProfissional, dataReferencia, cancellationToken);
                    decimal? custoCalculado = Etapa03CustoRules.ResolverCustoCalculado(
                        l.CustoInformadoNaPlanilha, l.HorasTrabalhadas, custoHoraVigente);
                    if (l.CustoInformadoNaPlanilha.HasValue)
                    {
                        viaPlanilha++;
                        if (l.CustoInformadoNaPlanilha.Value == 0m) viaPlanilhaZero++;
                        totalCustoPlanilha += l.CustoInformadoNaPlanilha.Value;
                    }
                    else if (custoCalculado.HasValue) viaRecalculo++;
                    else semCusto++;
                    totalCustoCalculado += custoCalculado ?? 0m;

                    linhasParaPersistir.Add(new GestaoVistaTimesheetSnapshotLinha
                    {
                        ProjectIdAcc = projectIdAcc,
                        NomeProfissional = l.NomeProfissional,
                        HorasPlanejadas = l.HorasPlanejadas,
                        HorasTrabalhadas = l.HorasTrabalhadas,
                        Projeto = l.Projeto,
                        Funcao = l.Funcao,
                        Disciplina = l.Disciplina,
                        DocumentoCodigo = l.DocumentoCodigo,
                        Documento = l.Documento,
                        Descricao = l.Descricao,
                        Doc1 = l.Doc1,
                        Doc2 = l.Doc2,
                        Doc3 = l.Doc3,
                        Doc4 = l.Doc4,
                        Doc5 = l.Doc5,
                        DataLancamento = l.DataLancamento,
                        CustoCalculado = custoCalculado,
                    });
                }

                var commit = await _repository.SubstituirSnapshotAsync(
                    projectIdAcc, linhasParaPersistir, hhExcel, dataSincronizacao, cancellationToken);

                _logger.LogInformation(
                    "[ETAPA 03][TIMESHEET][SNAPSHOT] Removidos={Removidos} | Inseridos={Inseridos}",
                    commit.Removidos, commit.LinhasInseridas);

                // TotalCustoSnapshot: lido de volta do banco após o commit; em rollback, soma do que se tentou gravar.
                decimal totalCustoSnapshot = commit.Sucesso && commit.CustoDepois.HasValue ? commit.CustoDepois.Value : totalCustoCalculado;
                _logger.LogInformation(
                    "[ETAPA 03][TIMESHEET][CUSTO] ViaPlanilha={ViaPlanilha} (dos quais Zero={ViaPlanilhaZero}) | ViaRecalculoTaxaVigente={ViaRecalculo} | SemCustoCalculado={SemCusto} | TotalCustoPlanilha={TotalCustoPlanilha} | TotalCustoSnapshot={TotalCustoSnapshot} | Diferenca={Diferenca}",
                    viaPlanilha, viaPlanilhaZero, viaRecalculo, semCusto, totalCustoPlanilha, totalCustoSnapshot, totalCustoSnapshot - totalCustoPlanilha);

                if (!commit.Sucesso)
                {
                    _logger.LogInformation(
                        "[ETAPA 03][VALIDACAO] HHExcel={HHExcel} | HHBanco={HHBanco} | Diferenca={Diferenca} | Resultado=ROLLBACK",
                        hhExcel, commit.HHDepois ?? commit.HHAntes, commit.DiferencaExcelBanco ?? 0m);
                    return Falha(commit.Motivo, commit.Erros, linhasExcel, hhExcel, antes.Linhas, antes.HH, commit.Removidos, commit.LinhasInseridas);
                }

                _logger.LogInformation(
                    "[ETAPA 03][TIMESHEET][BANCO_DEPOIS] Linhas={Linhas} | HHTrabalhadas={HHTrabalhadas}",
                    commit.LinhasDepois, commit.HHDepois);
                _logger.LogInformation(
                    "[ETAPA 03][VALIDACAO] HHExcel={HHExcel} | HHBanco={HHBanco} | Diferenca={Diferenca} | Resultado=OK",
                    hhExcel, commit.HHDepois, commit.DiferencaExcelBanco);
                _logger.LogInformation("[ETAPA 03][FIM] Resultado=EXECUTADA");

                return new GestaoVistaTimesheetSnapshotResultado
                {
                    Sucesso = true,
                    Motivo = "OK",
                    LinhasExcel = linhasExcel,
                    HHExcel = hhExcel,
                    LinhasBancoAntes = antes.Linhas,
                    HHBancoAntes = antes.HH,
                    Removidos = commit.Removidos,
                    Inseridos = commit.LinhasInseridas,
                    LinhasBancoDepois = commit.LinhasDepois ?? 0,
                    HHBancoDepois = commit.HHDepois ?? 0m,
                    Diferenca = commit.DiferencaExcelBanco ?? 0m,
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ETAPA 03][TIMESHEET] Falha inesperada não tratada.");
                return Falha("EXCECAO_NAO_TRATADA", new List<string> { ex.Message });
            }
        }

        private GestaoVistaTimesheetSnapshotResultado Falha(
            string motivo, List<string> erros, int linhasExcel = 0, decimal hhExcel = 0m,
            int linhasBancoAntes = 0, decimal hhBancoAntes = 0m, int removidos = 0, int inseridos = 0)
        {
            _logger.LogError("[ETAPA 03][FIM] Resultado=FALHOU Motivo={Motivo}", motivo);
            return new GestaoVistaTimesheetSnapshotResultado
            {
                Sucesso = false,
                Motivo = motivo,
                Erros = erros ?? new List<string>(),
                LinhasExcel = linhasExcel,
                HHExcel = hhExcel,
                LinhasBancoAntes = linhasBancoAntes,
                HHBancoAntes = hhBancoAntes,
                Removidos = removidos,
                Inseridos = inseridos,
            };
        }
    }
}
