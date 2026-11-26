/*
============================================================
OBJETIVO
============================================================
Criar views semânticas em [gestao] para HH/Custo Previsto x Realizado por documento e por disciplina —
espelhando a fórmula real encontrada NA PRÓPRIA LDP (aba "CUSTO", células AD/AG, ver
AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md, seção 4):
    HH_Realizado    = SUM(Timesheet.Trabalhadas WHERE Timesheet.DocumentoCodigo = documento)
    Custo_Realizado = SUM(Timesheet.CUSTO       WHERE Timesheet.DocumentoCodigo = documento)

REESCRITA nesta sessão (ver AUDITORIA_FORGESYNC_NA/MATRIZ_GESTAO_A_VISTA.md, seção "CORREÇÃO"): a versão
anterior desta migration dependia de dbo.VW_Custo_Diario_Documento, uma view NUNCA confirmada como
existente no DB_NA_CONNECT real (só encontrada hardcoded no código-fonte do ForgeSync, apontando para um
servidor remoto diferente). Essa dependência foi eliminada. A nova fonte é
gestao.TimesheetLancamento (criada em 05_criar_tabela_timesheet_lancamento.sql), alimentada pelo novo
extrator a partir das abas internas da própria LDP (Timesheet/Custo Real/Custo profissionais) — fonte
comprovada por auditoria real do arquivo .xlsx, não hipotética.

============================================================
DEPENDÊNCIAS
============================================================
01_criar_schema_gestao.sql, 05_criar_tabela_timesheet_lancamento.sql (gestao.TimesheetLancamento deve
existir). Lê de dbo.VW_Ldp_Unificada (Planned_HH/Planned_Cost/Discipline/Document_Title — fonte já
comprovada) e de gestao.TimesheetLancamento (HH/Custo realizado — nova fonte comprovada nesta sessão).

============================================================
PRÉ-CHECK + CREATE (mesmo lote, via SQL dinâmico — CREATE OR ALTER VIEW precisa ser o único statement
do seu lote, por isso vai dentro de EXEC(); nunca depender de RETURN atravessar um GO)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'gestao')
BEGIN
    RAISERROR('Schema [gestao] não existe — execute 01_criar_schema_gestao.sql primeiro.', 16, 1);
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.views WHERE name = 'VW_Ldp_Unificada')
BEGIN
    RAISERROR('dbo.VW_Ldp_Unificada não existe neste banco — esta migration depende dela. Abortando.', 16, 1);
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('gestao') AND name = 'TimesheetLancamento')
BEGIN
    RAISERROR('gestao.TimesheetLancamento não existe — execute 05_criar_tabela_timesheet_lancamento.sql primeiro.', 16, 1);
END
ELSE
BEGIN
    -- 1 linha por documento: previsto (da LDP operacional) x realizado (da nova cadeia Timesheet).
    EXEC('
        CREATE OR ALTER VIEW gestao.vw_Financeiro_Documento AS
        SELECT
            ldp.Project_ID_ACC,
            ldp.NA_Document_Number,
            ldp.Discipline,
            ldp.Document_Title,
            ldp.Planned_HH        AS HH_Previsto,
            realizado.HH_Realizado,
            ldp.Planned_Cost      AS Custo_Previsto,
            realizado.Custo_Realizado
        FROM dbo.VW_Ldp_Unificada AS ldp
        OUTER APPLY (
            SELECT
                SUM(ts.HorasTrabalhadas) AS HH_Realizado,
                SUM(ts.CustoCalculado)   AS Custo_Realizado
            FROM gestao.TimesheetLancamento ts
            WHERE ts.Project_ID_ACC = ldp.Project_ID_ACC
              AND ts.DocumentoCodigo = ldp.NA_Document_Number
        ) AS realizado;
    ');

    -- Mesma base, agregada por disciplina (substitui "graficosDisciplina" do ForgeSync).
    EXEC('
        CREATE OR ALTER VIEW gestao.vw_Financeiro_Disciplina AS
        SELECT
            Project_ID_ACC,
            Discipline,
            SUM(ISNULL(HH_Previsto, 0))    AS HH_Previsto,
            SUM(ISNULL(HH_Realizado, 0))   AS HH_Realizado,
            SUM(ISNULL(Custo_Previsto, 0)) AS Custo_Previsto,
            SUM(ISNULL(Custo_Realizado, 0)) AS Custo_Realizado
        FROM gestao.vw_Financeiro_Documento
        GROUP BY Project_ID_ACC, Discipline;
    ');

    PRINT 'Views gestao.vw_Financeiro_Documento e gestao.vw_Financeiro_Disciplina criadas/atualizadas.';
END
GO

/*
============================================================
ÍNDICES
============================================================
Não aplicável a views. gestao.TimesheetLancamento já indexa (Project_ID_ACC, DocumentoCodigo) na sua
própria migration (05) — é o índice que sustenta o OUTER APPLY acima.

============================================================
VALIDAÇÃO
============================================================
SELECT TOP 20 * FROM gestao.vw_Financeiro_Documento WHERE Project_ID_ACC = '<guid-de-teste>';
SELECT * FROM gestao.vw_Financeiro_Disciplina WHERE Project_ID_ACC = '<guid-de-teste>';

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP VIEW IF EXISTS gestao.vw_Financeiro_Disciplina;
-- DROP VIEW IF EXISTS gestao.vw_Financeiro_Documento;
*/
