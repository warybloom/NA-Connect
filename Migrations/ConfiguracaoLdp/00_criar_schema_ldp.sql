/*
============================================================
OBJETIVO
============================================================
Criar o schema [ldp] — fundação do novo motor configurável de LDP (Template/Aba/Campo, Mapeamento de
Cronograma, Modelo de Excel de saída). Ver
AUDITORIA_FORGESYNC_NA/17-CONFIGURACAO-LDP-NOVO-MOTOR-ARQUITETURA.md para a arquitetura completa.

Nenhuma tabela/coluna existente em [dbo] ou [config] é alterada por esta série. `dbo.Ldp_Unificada`,
`dbo.VW_Ldp_Unificada`, `dbo.Cronograma_Projetos`, o Worker e Tipo_LDP 1/2 continuam 100% intactos —
este schema só recebe objetos NOVOS, sem nenhuma conexão operacional nesta fase.

`config.PresetLdp` (Migrations/Configuracao/04) está SUPERSEDIDA por este schema — ver documento 17 §4.

============================================================
DEPENDÊNCIAS
============================================================
Nenhuma. Primeiro script da série (ordem: 00 → 01 → 02 → 03).

============================================================
PRÉ-CHECK + CREATE (mesmo lote — nunca depender de RETURN atravessar um GO)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'ldp')
BEGIN
    EXEC('CREATE SCHEMA ldp');
    PRINT 'Schema [ldp] criado.';
END
ELSE
BEGIN
    PRINT 'Schema [ldp] já existe — nada a fazer. Migration idempotente.';
END
GO

/*
============================================================
VALIDAÇÃO
============================================================
SELECT name FROM sys.schemas WHERE name = 'ldp'; -- deve devolver 1 linha após execução.

============================================================
ROLLBACK (NUNCA executar automaticamente — só manualmente, e só depois de remover TODOS os objetos
filhos criados dentro do schema pelos scripts 01/02/03)
============================================================
-- DROP SCHEMA ldp;
*/
