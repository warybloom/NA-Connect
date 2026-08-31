#nullable disable
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Threading.Tasks;
using System;

namespace APS.WebApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TimesheetReportController : ControllerBase
    {
        // 1. CONEXÃO DO DB DA N&A (Lê as Views do sistema antigo)
        private readonly string _connectionString = @"Server=.\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=True;TrustServerCertificate=True;";

       
        // =========================================================================
        // NOVA ROTA: LISTA DE PROJETOS (Para o ComboBox Dinâmico)
        // =========================================================================
        [HttpGet("projetos-lista")]
        public async Task<IActionResult> GetProjetosLista()
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string sql = @"
                        SELECT DISTINCT doc1 AS Codigo 
                        FROM VW_Custo_Diario_Documento 
                        WHERE doc1 IS NOT NULL AND doc1 <> '' 
                        ORDER BY doc1;
                    ";
                    var projetos = await conn.QueryAsync(sql);
                    return Ok(new { sucesso = true, dados = projetos });
                }
            }
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = $"Erro SQL: {ex.Message}" }); }
        }

        // =========================================================================
        // ROTA 1: TIMESHEET DIÁRIO (Agora aceita 'TODOS')
        // =========================================================================
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboardTimesheet(
            [FromQuery] string projetoCodigo, 
            [FromQuery] string semana = null, 
            [FromQuery] string disciplina = null, 
            [FromQuery] string documento = null)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string sql = @"
                        SELECT * INTO #TempCusto 
                        FROM VW_Custo_Diario_Documento 
                        WHERE (@projetoCodigo = 'TODOS' OR doc1 = @projetoCodigo)
                          AND (@semana IS NULL OR Periodo_Semanal_Label = @semana)
                          AND (@disciplina IS NULL OR Discipline = @disciplina)
                          AND (@documento IS NULL OR NA_Document_Number = @documento);

                        DECLARE @UltimaData DATE = (SELECT MAX(Data_Date) FROM #TempCusto WHERE HH_Realizado_Diario > 0);
                        DECLARE @TotalRealizado DECIMAL(18,4) = (SELECT SUM(ISNULL(HH_Realizado_Diario, 0)) FROM #TempCusto);
                        
                        DECLARE @DataPrevista DATE = (
                            SELECT MIN(Data_Date) FROM (
                                SELECT Data_Date, SUM(HH_Previsto_Diario) OVER (ORDER BY Data_Date ROWS UNBOUNDED PRECEDING) AS Acumulado 
                                FROM (SELECT Data_Date, SUM(ISNULL(HH_Previsto_Diario, 0)) AS HH_Previsto_Diario FROM #TempCusto GROUP BY Data_Date) Base
                            ) Acum WHERE Acum.Acumulado >= @TotalRealizado
                        );
                        
                        DECLARE @Desvio INT = ISNULL(DATEDIFF(DAY, @UltimaData, @DataPrevista), 0);

                        -- CARDS DE TOPO
                        SELECT 
                            SUM(ISNULL(HH_Previsto_Diario, 0)) AS Total_HH_Previsto, 
                            SUM(ISNULL(HH_Realizado_Diario, 0)) AS Total_HH_Realizado, 
                            SUM(ISNULL(Custo_Previsto_Diario, 0)) AS Total_Custo_Previsto, 
                            SUM(ISNULL(Custo_Realizado_Diario, 0)) AS Total_Custo_Realizado, 
                            @Desvio AS Desvio_Dias 
                        FROM #TempCusto;

                        -- TABELA DISCIPLINA
                        SELECT 
                            Discipline AS Disciplina, 
                            SUM(ISNULL(HH_Previsto_Diario, 0)) AS HH_Previsto, 
                            SUM(ISNULL(HH_Realizado_Diario, 0)) AS HH_Realizado, 
                            SUM(ISNULL(Custo_Previsto_Diario, 0)) AS Custo_Previsto, 
                            SUM(ISNULL(Custo_Realizado_Diario, 0)) AS Custo_Realizado 
                        FROM #TempCusto 
                        WHERE Discipline IS NOT NULL 
                        GROUP BY Discipline 
                        ORDER BY Discipline;

                        -- TABELA DOCUMENTO
                        SELECT 
                            Document_Title AS Documento, 
                            NA_Document_Number AS Codigo, 
                            SUM(ISNULL(HH_Previsto_Diario, 0)) AS HH_Previsto, 
                            SUM(ISNULL(HH_Realizado_Diario, 0)) AS HH_Realizado, 
                            SUM(ISNULL(Custo_Previsto_Diario, 0)) AS Custo_Previsto, 
                            SUM(ISNULL(Custo_Realizado_Diario, 0)) AS Custo_Realizado 
                        FROM #TempCusto 
                        WHERE Document_Title IS NOT NULL 
                        GROUP BY Document_Title, NA_Document_Number 
                        ORDER BY Document_Title;

                        -- GRÁFICOS DIÁRIOS
                        SELECT 
                            Data_Date, 
                            CONVERT(varchar(5), Data_Date, 103) AS DataFormatada, 
                            SUM(ISNULL(HH_Previsto_Diario, 0)) AS HH_Previsto, 
                            SUM(ISNULL(HH_Realizado_Diario, 0)) AS HH_Realizado, 
                            SUM(ISNULL(Custo_Previsto_Diario, 0)) AS Custo_Previsto, 
                            SUM(ISNULL(Custo_Realizado_Diario, 0)) AS Custo_Realizado 
                        FROM #TempCusto 
                        WHERE Data_Date IS NOT NULL 
                        GROUP BY Data_Date 
                        ORDER BY Data_Date;

                        -- LISTA DE SEMANAS
                        SELECT DISTINCT 
                            Periodo_Semanal_Label AS Semana, 
                            Inicio_da_Semana 
                        FROM VW_Custo_Diario_Documento 
                        WHERE (@projetoCodigo = 'TODOS' OR doc1 = @projetoCodigo) 
                          AND Periodo_Semanal_Label IS NOT NULL 
                        ORDER BY Inicio_da_Semana DESC;
                        
                        DROP TABLE #TempCusto;
                    ";

                    string projetoFiltro = string.IsNullOrEmpty(projetoCodigo) ? "TODOS" : projetoCodigo;

                    using (var multi = await conn.QueryMultipleAsync(sql, new { projetoCodigo = projetoFiltro, semana, disciplina, documento }))
                    {
                        var totais = await multi.ReadFirstOrDefaultAsync();
                        var tabelaDisciplinas = await multi.ReadAsync();
                        var tabelaDocumentos = await multi.ReadAsync();
                        var graficoDiario = await multi.ReadAsync();
                        var semanasLista = await multi.ReadAsync();

                        if (totais == null || totais.Total_HH_Previsto == null) 
                            return Ok(new { sucesso = false, mensagem = "Nenhum dado encontrado." });

                        return Ok(new { 
                            sucesso = true, 
                            dados = new { 
                                cards = new { 
                                    hh_previsto = (double)totais.Total_HH_Previsto, 
                                    hh_realizado = (double)totais.Total_HH_Realizado, 
                                    hh_saldo = (double)totais.Total_HH_Previsto - (double)totais.Total_HH_Realizado, 
                                    custo_previsto = (double)totais.Total_Custo_Previsto, 
                                    custo_realizado = (double)totais.Total_Custo_Realizado, 
                                    custo_saldo = (double)totais.Total_Custo_Previsto - (double)totais.Total_Custo_Realizado, 
                                    desvio_dias = (int)totais.Desvio_Dias 
                                }, 
                                tabelas = new { disciplinas = tabelaDisciplinas, documentos = tabelaDocumentos }, 
                                grafico = graficoDiario, 
                                filtroSemanas = semanasLista 
                            } 
                        });
                    }
                }
            }
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = $"Erro SQL: {ex.Message}" }); }
        }

        // =========================================================================
        // ROTA 2: CURVA S (Agora aceita 'TODOS')
        // =========================================================================
        [HttpGet("curva-s")]
        public async Task<IActionResult> GetCurvaSEVM([FromQuery] string projetoCodigo)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string sql = @"
                        WITH Agrupado_Diario AS (
                            SELECT 
                                Data_Date, 
                                SUM(ISNULL(HH_Previsto_Diario, 0)) AS HH_Previsto_Dia, 
                                SUM(ISNULL(HH_Realizado_Diario, 0)) AS HH_Realizado_Dia, 
                                SUM(ISNULL(Custo_Previsto_Diario, 0)) AS Custo_Previsto_Dia, 
                                SUM(ISNULL(Custo_Realizado_Diario, 0)) AS Custo_Realizado_Dia 
                            FROM VW_Custo_Diario_Documento 
                            WHERE (@projetoCodigo = 'TODOS' OR doc1 = @projetoCodigo) 
                              AND Data_Date IS NOT NULL 
                            GROUP BY Data_Date
                        )
                        SELECT 
                            Data_Date, 
                            CONVERT(varchar(5), Data_Date, 103) AS DataFormatada, 
                            SUM(HH_Previsto_Dia) OVER (ORDER BY Data_Date ROWS UNBOUNDED PRECEDING) AS HH_Previsto_Acumulado, 
                            SUM(HH_Realizado_Dia) OVER (ORDER BY Data_Date ROWS UNBOUNDED PRECEDING) AS HH_Realizado_Acumulado, 
                            SUM(Custo_Previsto_Dia) OVER (ORDER BY Data_Date ROWS UNBOUNDED PRECEDING) AS Custo_Previsto_Acumulado, 
                            SUM(Custo_Realizado_Dia) OVER (ORDER BY Data_Date ROWS UNBOUNDED PRECEDING) AS Custo_Realizado_Acumulado 
                        FROM Agrupado_Diario 
                        ORDER BY Data_Date;
                    ";

                    string projetoFiltro = string.IsNullOrEmpty(projetoCodigo) ? "TODOS" : projetoCodigo;
                    var curvaS = await conn.QueryAsync(sql, new { projetoCodigo = projetoFiltro });
                    return Ok(new { sucesso = true, dados = curvaS });
                }
            }
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = $"Erro SQL: {ex.Message}" }); }
        }

        // =========================================================================
        // ROTA 3: PRODUÇÃO MAPA CALOR (Agora aceita 'TODOS')
        // =========================================================================
        [HttpGet("producao")]
        public async Task<IActionResult> GetProducaoStatus([FromQuery] string projetoCodigo)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string sql = @"
                        SELECT 
                            NA_Document_Number AS Codigo, 
                            Document_Title AS Titulo, 
                            Discipline AS Disciplina, 
                            ISNULL(Status_In_Flow, 'Não Iniciado') AS StatusInterno, 
                            ISNULL(Document_Status, 'Sem Status') AS StatusCliente, 
                            ISNULL(Percent_Physical_Completed, 0) AS AvancoFisico, 
                            Planned_Date_Baseline AS DataPlanejada, 
                            Emission_Date AS DataEmissao 
                        INTO #TempDocs 
                        FROM VW_LDP_DOCUMENTOS_ATIVOS 
                        WHERE (@projetoCodigo = 'TODOS' OR doc1 = @projetoCodigo);

                        SELECT 
                            COUNT(*) AS TotalDocumentos, 
                            SUM(CASE WHEN AvancoFisico >= 100 THEN 1 ELSE 0 END) AS TotalConcluidos, 
                            SUM(CASE WHEN AvancoFisico > 0 AND AvancoFisico < 100 THEN 1 ELSE 0 END) AS TotalEmAndamento, 
                            SUM(CASE WHEN AvancoFisico = 0 THEN 1 ELSE 0 END) AS TotalNaoIniciados 
                        FROM #TempDocs;

                        SELECT StatusInterno AS name, COUNT(*) AS value 
                        FROM #TempDocs GROUP BY StatusInterno ORDER BY value DESC;

                        SELECT StatusCliente AS name, COUNT(*) AS value 
                        FROM #TempDocs GROUP BY StatusCliente ORDER BY value DESC;

                        SELECT * FROM #TempDocs ORDER BY Disciplina, Codigo;
                        
                        DROP TABLE #TempDocs;
                    ";

                    string projetoFiltro = string.IsNullOrEmpty(projetoCodigo) ? "TODOS" : projetoCodigo;

                    using (var multi = await conn.QueryMultipleAsync(sql, new { projetoCodigo = projetoFiltro }))
                    {
                        var kpis = await multi.ReadFirstOrDefaultAsync(); 
                        var statusInterno = await multi.ReadAsync(); 
                        var statusCliente = await multi.ReadAsync(); 
                        var tabela = await multi.ReadAsync();
                        return Ok(new { sucesso = true, dados = new { kpis, statusInterno, statusCliente, tabela } });
                    }
                }
            }
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = $"Erro SQL: {ex.Message}" }); }
        }

        // =========================================================================
        // ROTA 4: AVANÇO 4D (Agora aceita 'TODOS')
        // =========================================================================
        [HttpGet("avanco-4d")]
        public async Task<IActionResult> GetAvanco4D([FromQuery] string projetoCodigo)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string sql = @"
                        SELECT * INTO #TempCrono 
                        FROM VW_Cronograma_Projetos_Ultima_Versao 
                        WHERE (@projetoCodigo = 'TODOS' OR CR_OS = @projetoCodigo OR Project_ID_ACC = @projetoCodigo);

                        SELECT 
                            COUNT(*) AS TotalTarefas, 
                            SUM(CASE WHEN ISNULL(PERCENT_PHYSICAL_COMPLETED,0) >= ISNULL(PERCENT_PREDICTED_PHYSICAL,0) THEN 1 ELSE 0 END) AS TarefasNoPrazo, 
                            SUM(CASE WHEN ISNULL(PERCENT_PHYSICAL_COMPLETED,0) < ISNULL(PERCENT_PREDICTED_PHYSICAL,0) THEN 1 ELSE 0 END) AS TarefasAtrasadas, 
                            AVG(ISNULL(PERCENT_PREDICTED_PHYSICAL, 0)) AS Media_Previsto, 
                            AVG(ISNULL(PERCENT_PHYSICAL_COMPLETED, 0)) AS Media_Realizado 
                        FROM #TempCrono 
                        WHERE ISNULL(PERCENT_PREDICTED_PHYSICAL, 0) > 0 OR ISNULL(PERCENT_PHYSICAL_COMPLETED, 0) > 0;

                        SELECT UID_Tarefa, ISNULL(EDT_WBS, '-') AS EDT_WBS, TASK_NAME AS Tarefa, CONVERT(varchar(10), PLANNED_DATE_START, 103) AS DataInicio, CONVERT(varchar(10), REPLANNED_DATE, 103) AS DataTendencia, CONVERT(varchar(10), PLANNED_DATA_BASELINE, 103) AS DataFim, CAST(ISNULL(PERCENT_PREDICTED_PHYSICAL, 0) AS decimal(5,2)) AS Previsto, CAST(ISNULL(PERCENT_PHYSICAL_COMPLETED, 0) AS decimal(5,2)) AS Realizado FROM #TempCrono ORDER BY EDT_WBS, PLANNED_DATE_START;

                        DROP TABLE #TempCrono;
                    ";

                    string projetoFiltro = string.IsNullOrEmpty(projetoCodigo) ? "TODOS" : projetoCodigo;

                    using (var multi = await conn.QueryMultipleAsync(sql, new { projetoCodigo = projetoFiltro }))
                    {
                        var kpis = await multi.ReadFirstOrDefaultAsync(); 
                        var cronograma = await multi.ReadAsync();
                        return Ok(new { sucesso = true, dados = new { kpis, cronograma } });
                    }
                }
            }
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = $"Erro SQL: {ex.Message}" }); }
        }

        // =========================================================================
        // ROTA 5: GESTÃO À VISTA (KPIs, Gauges e Programação Semanal)
        // =========================================================================
        [HttpGet("gestao-vista")]
        public async Task<IActionResult> GetGestaoAVista(
            [FromQuery] string projetoCodigo,
            [FromQuery] string disciplina = null,
            [FromQuery] string documento = null)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    string sql = @"
                        SELECT * INTO #TempGestao 
                        FROM VW_Ldp_Unificada_Teste 
                        WHERE (@projetoCodigo = 'TODOS' OR CR_OS = @projetoCodigo OR Project_ID_ACC = @projetoCodigo)
                          AND (@disciplina IS NULL OR Discipline = @disciplina)
                          AND (@documento IS NULL OR NA_Document_Number = @documento);

                        SELECT 
                            COUNT(*) AS TotalDocumentos, 
                            SUM(CASE WHEN ISNULL(Percent_Physical_Completed, 0) >= 100 THEN 1 ELSE 0 END) AS Emitidos, 
                            SUM(CASE WHEN Planned_Date_Baseline < GETDATE() AND ISNULL(Percent_Physical_Completed, 0) < 100 THEN 1 ELSE 0 END) AS Atrasados, 
                            SUM(CASE WHEN Planned_Date_Baseline >= GETDATE() AND ISNULL(Percent_Physical_Completed, 0) < 100 THEN 1 ELSE 0 END) AS NoPrazo, 
                            SUM(CASE WHEN ISNULL(Percent_Physical_Completed, 0) = 0 THEN 1 ELSE 0 END) AS NaoIniciados 
                        FROM #TempGestao;

                        SELECT 
                            SUM(ISNULL(Planned_HH, 0)) AS HH_Previsto, 
                            SUM(ISNULL(HH_TimeSheet, 0)) AS HH_Realizado 
                        FROM #TempGestao;

                        SELECT 
                            Discipline AS Disciplina, 
                            SUM(ISNULL(HH_Previsto_Diario, 0)) AS HH_Previsto, 
                            SUM(ISNULL(HH_Realizado_Diario, 0)) AS HH_Realizado,
                            SUM(ISNULL(Custo_Previsto_Diario, 0)) AS Custo_Previsto,
                            SUM(ISNULL(Custo_Realizado_Diario, 0)) AS Custo_Realizado
                        FROM VW_Custo_Diario_Documento 
                        WHERE (@projetoCodigo = 'TODOS' OR doc1 = @projetoCodigo)
                          AND (@disciplina IS NULL OR Discipline = @disciplina)
                          AND (@documento IS NULL OR NA_Document_Number = @documento)
                          AND Discipline IS NOT NULL 
                        GROUP BY Discipline 
                        ORDER BY HH_Previsto DESC;

                        SELECT TOP 15 
                            NA_Document_Number AS Codigo, 
                            Document_Title AS Titulo, 
                            Discipline AS Disciplina, 
                            CONVERT(varchar(10), Planned_Date_Baseline, 103) AS DataAlvo, 
                            CAST(ISNULL(Percent_Physical_Completed, 0) AS int) AS AvancoFisico, 
                            CASE WHEN Planned_Date_Baseline < GETDATE() THEN 1 ELSE 0 END AS IsAtrasado 
                        FROM #TempGestao 
                        WHERE ISNULL(Percent_Physical_Completed, 0) < 100 
                          AND Planned_Date_Baseline IS NOT NULL 
                          AND Planned_Date_Baseline <= DATEADD(DAY, 7, GETDATE()) 
                        ORDER BY Planned_Date_Baseline ASC;

                        DROP TABLE #TempGestao;
                    ";

                    string projetoFiltro = string.IsNullOrEmpty(projetoCodigo) ? "TODOS" : projetoCodigo;

                    using (var multi = await conn.QueryMultipleAsync(sql, new { projetoCodigo = projetoFiltro, disciplina, documento }))
                    {
                        var cardsDocs = await multi.ReadFirstOrDefaultAsync(); 
                        var cardsHH = await multi.ReadFirstOrDefaultAsync(); 
                        var graficosDisciplina = await multi.ReadAsync(); 
                        var programacaoSemanal = await multi.ReadAsync();
                        return Ok(new { sucesso = true, dados = new { cardsDocs, cardsHH, graficosDisciplina, programacaoSemanal } });
                    }
                }
            }
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = $"Erro SQL: {ex.Message}" }); }
        }

        // =========================================================================
        // ROTA 6: OFENSORES (Análise de Desvios de Custo, HH e Prazo)
        // =========================================================================
        [HttpGet("ofensores")]
        public async Task<IActionResult> GetOfensores([FromQuery] string projetoCodigo)
        {
            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    string sql = @"
                        DECLARE @FiltroProjeto VARCHAR(100) = @projetoCodigo;

                        -- 1. TOP DISCIPLINAS OFENSORAS DE HH (Quem mais estourou o orçamento de horas)
                        SELECT 
                            Discipline AS Disciplina,
                            SUM(ISNULL(HH_Previsto_Diario, 0)) AS HHPrevisto, 
                            SUM(ISNULL(HH_Realizado_Diario, 0)) AS HHRealizado,
                            (SUM(ISNULL(HH_Realizado_Diario, 0)) - SUM(ISNULL(HH_Previsto_Diario, 0))) AS DesvioHH
                        FROM VW_Custo_Diario_Documento
                        WHERE (@FiltroProjeto = 'TODOS' OR doc1 = @FiltroProjeto) AND Discipline IS NOT NULL
                        GROUP BY Discipline
                        HAVING (SUM(ISNULL(HH_Realizado_Diario, 0)) - SUM(ISNULL(HH_Previsto_Diario, 0))) > 0
                        ORDER BY DesvioHH DESC;

                        -- 2. TOP 10 DOCUMENTOS 'RALO FINANCEIRO' (Quem mais gastou acima do previsto)
                        SELECT TOP 10 
                            Document_Title AS Titulo, 
                            NA_Document_Number AS Codigo, 
                            Discipline AS Disciplina,
                            SUM(ISNULL(Custo_Previsto_Diario, 0)) AS CustoPrevisto, 
                            SUM(ISNULL(Custo_Realizado_Diario, 0)) AS CustoRealizado,
                            (SUM(ISNULL(Custo_Realizado_Diario, 0)) - SUM(ISNULL(Custo_Previsto_Diario, 0))) AS DesvioCusto
                        FROM VW_Custo_Diario_Documento
                        WHERE (@FiltroProjeto = 'TODOS' OR doc1 = @FiltroProjeto) AND Document_Title IS NOT NULL
                        GROUP BY Document_Title, NA_Document_Number, Discipline
                        HAVING (SUM(ISNULL(Custo_Realizado_Diario, 0)) - SUM(ISNULL(Custo_Previsto_Diario, 0))) > 0
                        ORDER BY DesvioCusto DESC;

                        -- 3. TOP 15 ATRASOS CRÔNICOS (Dias desde que deveria ter sido entregue)
                        SELECT TOP 15 
                            NA_Document_Number AS Codigo, 
                            Document_Title AS Titulo, 
                            Discipline AS Disciplina,
                            CONVERT(varchar(10), Planned_Date_Baseline, 103) AS DataAlvo,
                            CAST(ISNULL(Percent_Physical_Completed, 0) AS int) AS AvancoFisico,
                            DATEDIFF(DAY, Planned_Date_Baseline, GETDATE()) AS DiasAtraso
                        FROM VW_Ldp_Unificada_Teste
                        WHERE (@FiltroProjeto = 'TODOS' OR CR_OS = @FiltroProjeto OR Project_ID_ACC = @FiltroProjeto)
                          AND ISNULL(Percent_Physical_Completed, 0) < 100
                          AND Planned_Date_Baseline < GETDATE()
                        ORDER BY DiasAtraso DESC;
                    ";

                    string projetoFiltro = string.IsNullOrEmpty(projetoCodigo) ? "TODOS" : projetoCodigo;

                    using (var multi = await conn.QueryMultipleAsync(sql, new { projetoCodigo = projetoFiltro }))
                    {
                        var disciplinasOff = await multi.ReadAsync();
                        var documentosCustoOff = await multi.ReadAsync();
                        var atrasosCronicos = await multi.ReadAsync();

                        return Ok(new { 
                            sucesso = true, 
                            dados = new { disciplinasOff, documentosCustoOff, atrasosCronicos } 
                        });
                    }
                }
            }
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = $"Erro SQL: {ex.Message}" }); }
        }

        // =========================================================================
        // ROTA 7: HUB DE QUALIDADE E CONTRATOS (Lê do Banco Novo)
        // =========================================================================
        [HttpGet("qualidade-contratos")]
        public async Task<IActionResult> GetQualidadeContratos([FromQuery] string projetoCodigo)
        {
            try
            {
                using (var conn = new SqlConnection( _connectionString))
                {
                    await conn.OpenAsync();
                    string sql = @"
                        DECLARE @Proj VARCHAR(50) = @projetoCodigo;
                        SELECT Status AS name, COUNT(*) AS value FROM TB_Controle_Holds WHERE (@Proj = 'TODOS' OR ProjetoCodigo = @Proj) GROUP BY Status;
                        SELECT Disciplina AS name, COUNT(*) AS value FROM TB_Controle_Holds WHERE (@Proj = 'TODOS' OR ProjetoCodigo = @Proj) GROUP BY Disciplina;
                        SELECT * FROM TB_Controle_Holds WHERE (@Proj = 'TODOS' OR ProjetoCodigo = @Proj) ORDER BY Data_Criacao DESC;

                        SELECT Status AS name, COUNT(*) AS value FROM TB_Desvio_Escopo WHERE (@Proj = 'TODOS' OR ProjetoCodigo = @Proj) GROUP BY Status;
                        SELECT Disciplina AS name, COUNT(*) AS value FROM TB_Desvio_Escopo WHERE (@Proj = 'TODOS' OR ProjetoCodigo = @Proj) GROUP BY Disciplina;
                        SELECT * FROM TB_Desvio_Escopo WHERE (@Proj = 'TODOS' OR ProjetoCodigo = @Proj) ORDER BY Data_Registro DESC;

                        SELECT Status AS name, COUNT(*) AS value FROM TB_Nao_Conformidade WHERE (@Proj = 'TODOS' OR ProjetoCodigo = @Proj) GROUP BY Status;
                        SELECT Disciplina AS name, COUNT(*) AS value FROM TB_Nao_Conformidade WHERE (@Proj = 'TODOS' OR ProjetoCodigo = @Proj) GROUP BY Disciplina;
                        SELECT * FROM TB_Nao_Conformidade WHERE (@Proj = 'TODOS' OR ProjetoCodigo = @Proj) ORDER BY Data_Registro DESC;
                    ";
                    string projetoFiltro = string.IsNullOrEmpty(projetoCodigo) ? "TODOS" : projetoCodigo;
                    using (var multi = await conn.QueryMultipleAsync(sql, new { projetoCodigo = projetoFiltro }))
                    {
                        return Ok(new { sucesso = true, dados = new { holds = new { status = await multi.ReadAsync(), disciplinas = await multi.ReadAsync(), tabela = await multi.ReadAsync() }, desvios = new { status = await multi.ReadAsync(), disciplinas = await multi.ReadAsync(), tabela = await multi.ReadAsync() }, ncs = new { status = await multi.ReadAsync(), disciplinas = await multi.ReadAsync(), tabela = await multi.ReadAsync() } } });
                    }
                }
            }
            catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = $"Erro SQL: {ex.Message}" }); }
        }

// =========================================================================
        // ROTAS DE INSERÇÃO, ATUALIZAÇÃO E EXCLUSÃO
        // =========================================================================
        
        [HttpPost("novo-desvio")]
        public async Task<IActionResult> PostDesvio([FromBody] DesvioModel model)
        {
            try {
                using (var conn = new SqlConnection( _connectionString)) {
                    string sql = @"INSERT INTO TB_Desvio_Escopo (ProjetoCodigo, Num_Desvio, Descricao, Disciplina, Data_Registro, Quant_HH, Status, Desvio_Interno, Change_Order, Valor_CO, Tipo_Desvio, Categoria_Motivo, Impacto)
                                   VALUES (@ProjetoCodigo, @Num_Desvio, @Descricao, @Disciplina, @Data_Registro, @Quant_HH, @Status, @Desvio_Interno, @Change_Order, @Valor_CO, @Tipo_Desvio, @Categoria_Motivo, @Impacto)";
                    await conn.ExecuteAsync(sql, model);
                    return Ok(new { sucesso = true, mensagem = "Desvio cadastrado com sucesso!" });
                }
            } catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }

        [HttpPost("atualizar-desvio")]
        public async Task<IActionResult> PutDesvio([FromBody] DesvioModel model)
        {
            try {
                using (var conn = new SqlConnection( _connectionString)) {
                    string sql = @"UPDATE TB_Desvio_Escopo SET 
                                    ProjetoCodigo = @ProjetoCodigo, Num_Desvio = @Num_Desvio, Descricao = @Descricao, Disciplina = @Disciplina, 
                                    Data_Registro = @Data_Registro, Quant_HH = @Quant_HH, Status = @Status, Desvio_Interno = @Desvio_Interno, Change_Order = @Change_Order,
                                    Valor_CO = @Valor_CO, Tipo_Desvio = @Tipo_Desvio, Categoria_Motivo = @Categoria_Motivo, Impacto = @Impacto
                                   WHERE ID = @ID"; 
                    await conn.ExecuteAsync(sql, model);
                    return Ok(new { sucesso = true, mensagem = "Desvio atualizado com sucesso!" });
                }
            } catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }

        [HttpDelete("deletar-desvio/{id}")]
        public async Task<IActionResult> DeleteDesvio(int id)
        {
            try {
                using (var conn = new SqlConnection( _connectionString)) {
                    await conn.ExecuteAsync("DELETE FROM TB_Desvio_Escopo WHERE ID = @id", new { id });
                    return Ok(new { sucesso = true, mensagem = "Desvio excluído com sucesso!" });
                }
            } catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }

        [HttpPost("novo-hold")]
        public async Task<IActionResult> PostHold([FromBody] HoldModel model)
        {
            try {
                using (var conn = new SqlConnection( _connectionString)) {
                    string sql = @"INSERT INTO TB_Controle_Holds (ProjetoCodigo, Num_Hold, Disciplina, Num_NA_Documento, Titulo_Documento, Revisao, Motivo_Hold, Status, Responsavel)
                                   VALUES (@ProjetoCodigo, @Num_Hold, @Disciplina, @Num_NA_Documento, @Titulo_Documento, @Revisao, @Motivo_Hold, @Status, @Responsavel)";
                    await conn.ExecuteAsync(sql, model);
                    return Ok(new { sucesso = true, mensagem = "Hold cadastrado com sucesso!" });
                }
            } catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }

        [HttpPost("atualizar-hold")]
        public async Task<IActionResult> PutHold([FromBody] HoldModel model)
        {
            try {
                using (var conn = new SqlConnection( _connectionString)) {
                    string sql = @"UPDATE TB_Controle_Holds SET 
                                    ProjetoCodigo = @ProjetoCodigo, Num_Hold = @Num_Hold, Disciplina = @Disciplina, Num_NA_Documento = @Num_NA_Documento, 
                                    Titulo_Documento = @Titulo_Documento, Revisao = @Revisao, Motivo_Hold = @Motivo_Hold, Status = @Status, Responsavel = @Responsavel
                                   WHERE ID = @ID";
                    await conn.ExecuteAsync(sql, model);
                    return Ok(new { sucesso = true, mensagem = "Hold atualizado com sucesso!" });
                }
            } catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }

        [HttpDelete("deletar-hold/{id}")]
        public async Task<IActionResult> DeleteHold(int id)
        {
            try {
                using (var conn = new SqlConnection( _connectionString)) {
                    await conn.ExecuteAsync("DELETE FROM TB_Controle_Holds WHERE ID = @id", new { id });
                    return Ok(new { sucesso = true, mensagem = "Hold excluído com sucesso!" });
                }
            } catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }

        [HttpPost("nova-nc")]
        public async Task<IActionResult> PostNC([FromBody] NCModel model)
        {
            try {
                using (var conn = new SqlConnection( _connectionString)) {
                    string sql = @"INSERT INTO TB_Nao_Conformidade (ProjetoCodigo, Num_Doc, Responsavel_Abertura, Data_Registro, Tipo_Registro, Descricao_Projeto, Empresa, Disciplina, Descricao, Status, Categoria_Erro)
                                   VALUES (@ProjetoCodigo, @Num_Doc, @Responsavel_Abertura, @Data_Registro, @Tipo_Registro, @Descricao_Projeto, @Empresa, @Disciplina, @Descricao, @Status, @Categoria_Erro)";
                    await conn.ExecuteAsync(sql, model);
                    return Ok(new { sucesso = true, mensagem = "Não Conformidade cadastrada com sucesso!" });
                }
            } catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }

        [HttpPost("atualizar-nc")]
        public async Task<IActionResult> PutNC([FromBody] NCModel model)
        {
            try {
                using (var conn = new SqlConnection( _connectionString)) {
                    string sql = @"UPDATE TB_Nao_Conformidade SET 
                                    ProjetoCodigo = @ProjetoCodigo, Num_Doc = @Num_Doc, Responsavel_Abertura = @Responsavel_Abertura, Data_Registro = @Data_Registro, 
                                    Tipo_Registro = @Tipo_Registro, Descricao_Projeto = @Descricao_Projeto, Empresa = @Empresa, Disciplina = @Disciplina, Descricao = @Descricao, Status = @Status, Categoria_Erro = @Categoria_Erro
                                   WHERE ID = @ID";
                    await conn.ExecuteAsync(sql, model);
                    return Ok(new { sucesso = true, mensagem = "Não Conformidade atualizada com sucesso!" });
                }
            } catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }

        [HttpDelete("deletar-nc/{id}")]
        public async Task<IActionResult> DeleteNC(int id)
        {
            try {
                using (var conn = new SqlConnection( _connectionString)) {
                    await conn.ExecuteAsync("DELETE FROM TB_Nao_Conformidade WHERE ID = @id", new { id });
                    return Ok(new { sucesso = true, mensagem = "Não Conformidade excluída com sucesso!" });
                }
            } catch (Exception ex) { return StatusCode(500, new { sucesso = false, mensagem = ex.Message }); }
        }
    }

    public class DesvioModel { 
        public int? ID { get; set; } public string ProjetoCodigo { get; set; } public string Num_Desvio { get; set; } 
        public string Descricao { get; set; } public string Disciplina { get; set; } public DateTime Data_Registro { get; set; } 
        public decimal Quant_HH { get; set; } public string Status { get; set; } public string Desvio_Interno { get; set; } 
        public string Change_Order { get; set; } 
        public decimal? Valor_CO { get; set; } public string Tipo_Desvio { get; set; } public string Categoria_Motivo { get; set; } public string Impacto { get; set; }
    }
    public class HoldModel { public int? ID { get; set; } public string ProjetoCodigo { get; set; } public string Num_Hold { get; set; } public string Disciplina { get; set; } public string Num_NA_Documento { get; set; } public string Titulo_Documento { get; set; } public string Revisao { get; set; } public string Motivo_Hold { get; set; } public string Status { get; set; } public string Responsavel { get; set; } }
    public class NCModel { public int? ID { get; set; } public string ProjetoCodigo { get; set; } public string Num_Doc { get; set; } public string Responsavel_Abertura { get; set; } public DateTime Data_Registro { get; set; } public string Tipo_Registro { get; set; } public string Descricao_Projeto { get; set; } public string Empresa { get; set; } public string Disciplina { get; set; } public string Descricao { get; set; } public string Status { get; set; } public string Categoria_Erro { get; set; } }
}