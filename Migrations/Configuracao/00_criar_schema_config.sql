/*
============================================================
OBJETIVO
============================================================
Criar o schema [config] — camada para as estruturas novas da tela Configuração (Normas, Templates,
Pacotes OCR, Presets LDP e o vínculo por projeto), separada de [dbo] operacional. Nenhuma tabela/coluna
existente em [dbo] é alterada, movida ou removida por esta série de migrations; este schema só recebe
objetos NOVOS.

Ver AUDITORIA_FORGESYNC_NA/16-CONFIGURACAO-NORMAS-TEMPLATES-OCR-PRESETS-AUDITORIA.md para a auditoria
completa (frontend x banco) que fundamenta este e os próximos scripts.

============================================================
DEPENDÊNCIAS
============================================================
Nenhuma. Primeiro script da série (ordem: 00 → 01 → 02 → 03 → 04 → 05).

============================================================
PRÉ-CHECK + CREATE (mesmo lote — nunca depender de RETURN atravessar um GO)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'config')
BEGIN
    EXEC('CREATE SCHEMA config');
    PRINT 'Schema [config] criado.';
END
ELSE
BEGIN
    PRINT 'Schema [config] já existe — nada a fazer. Migration idempotente.';
END
GO

/*
============================================================
VALIDAÇÃO
============================================================
SELECT name FROM sys.schemas WHERE name = 'config'; -- deve devolver 1 linha após execução.

============================================================
ROLLBACK (NUNCA executar automaticamente — só manualmente, e só depois de remover TODOS os objetos
filhos criados dentro do schema pelos scripts 01/02/03/04/05)
============================================================
-- DROP SCHEMA config;
*/
