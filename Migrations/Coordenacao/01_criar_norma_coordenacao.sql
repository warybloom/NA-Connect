/*
============================================================
OBJETIVO
============================================================
Corrigir BUG 1 (HH Previsto perde precisão antes da agregação — auditoria 2026-09-22, BML011.013):
dbo.Ldp_Unificada.Planned_HH e Planned_Cost são hoje DECIMAL(18,2) — só 2 casas decimais. Toda linha da
LDP é arredondada para 2 casas NO MOMENTO DO INSERT (Services/DatabaseService.cs
SalvarDadosLdpUnificada), antes de qualquer SUM em relatório. Isso é uma perda de precisão FÍSICA da
coluna, não um bug de código de agregação — confirmado por auditoria:
SELECT SUM(CAST(Planned_HH AS DECIMAL(18,6))) para BML011.013 na carga atual = 6.890,840000, idêntico ao
valor de 2 casas (6.890,84) — a única forma de recuperar a precisão original (Excel tinha valores com
mais casas, cuja soma correta é 6.891,00) é armazenar com mais casas ANTES do arredondamento acontecer.

============================================================
IMPORTANTE — ISTO NÃO RECUPERA RETROATIVAMENTE VALORES JÁ ARREDONDADOS
============================================================
Alargar a coluna preserva os valores JÁ GRAVADOS exatamente como estão hoje (8.00 continua 8.000000,
nunca "recupera" dígitos perdidos em cargas antigas). Só as PRÓXIMAS cargas da LDP (próxima execução do
Worker, Etapa 11) vão gravar com a precisão nova. Para BML011.013 voltar a somar 6.891,00 exatamente, é
necessário1) esta migration, 2) uma nova sincronização da LDP do projeto (ciclo normal do Worker, não
acionado por este processo).

============================================================
ESCOPO
============================================================
Só Planned_HH e Planned_Cost (os dois campos que os KPIs oficiais de Previsto usam —
Services/Planning/LdpPlanningRules.cs). Actual_HH/Actual_Cost/Remaining_HH/Remaining_Cost NÃO são tocados
(fora do escopo desta correção; não são fonte de nenhum KPI oficial hoje).

Nenhum default constraint, índice ou coluna computada depende dessas colunas (confirmado via
sys.default_constraints/sys.index_columns antes de escrever esta migration) — ALTER COLUMN é uma
operação simples de alargamento de escala, sem necessidade de dropar/recriar a tabela.

============================================================
PRÉ-CHECK + ALTER (mesmo lote)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID('dbo.Ldp_Unificada'))
BEGIN
    RAISERROR('dbo.Ldp_Unificada não existe — abortando.', 16, 1);
    RETURN;
END

DECLARE @scalePlannedHh INT = (SELECT scale FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Ldp_Unificada') AND name = 'Planned_HH');
DECLARE @scalePlannedCost INT = (SELECT scale FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Ldp_Unificada') AND name = 'Planned_Cost');

IF @scalePlannedHh IS NULL OR @scalePlannedCost IS NULL
BEGIN
    RAISERROR('Colunas Planned_HH/Planned_Cost não encontradas em dbo.Ldp_Unificada — abortando.', 16, 1);
    RETURN;
END

IF @scalePlannedHh >= 6 AND @scalePlannedCost >= 6
BEGIN
    PRINT 'Planned_HH e Planned_Cost já têm escala >= 6 — nada a fazer. Migration idempotente.';
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    IF @scalePlannedHh < 6
    BEGIN
        ALTER TABLE dbo.Ldp_Unificada ALTER COLUMN Planned_HH DECIMAL(18,6) NULL;
        PRINT 'dbo.Ldp_Unificada.Planned_HH alterado para DECIMAL(18,6).';
    END

    IF @scalePlannedCost < 6
    BEGIN
        ALTER TABLE dbo.Ldp_Unificada ALTER COLUMN Planned_Cost DECIMAL(18,6) NULL;
        PRINT 'dbo.Ldp_Unificada.Planned_Cost alterado para DECIMAL(18,6).';
    END

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT 'ERRO — nenhuma alteração foi mantida (rollback completo). Detalhe: ' + ERROR_MESSAGE();
    THROW;
END CATCH
GO

/*
============================================================
VALIDAÇÃO
============================================================
SELECT c.name, c.precision, c.scale FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.Ldp_Unificada') AND c.name IN ('Planned_HH','Planned_Cost');
-- Esperado: precision=18, scale=6 para as duas.

-- Depois de uma NOVA sincronização do BML011.013 (ciclo normal do Worker):
SELECT SUM(Planned_HH) FROM dbo.Ldp_Unificada
WHERE Project_ID_ACC='b.c3e20d9d-779e-423c-9381-abec38cf11cb'
  AND Data_Sincronizacao >= (SELECT MAX(Data_Sincronizacao) FROM dbo.Ldp_Unificada WHERE Project_ID_ACC='b.c3e20d9d-779e-423c-9381-abec38cf11cb');
-- Esperado, após o resync: 6.891,00 (não 6.890,84).

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- ALTER TABLE dbo.Ldp_Unificada ALTER COLUMN Planned_HH DECIMAL(18,2) NULL;
-- ALTER TABLE dbo.Ldp_Unificada ALTER COLUMN Planned_Cost DECIMAL(18,2) NULL;
*/
