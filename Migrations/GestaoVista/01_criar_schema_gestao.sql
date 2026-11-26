/*
============================================================
OBJETIVO
============================================================
Criar o schema [gestao] — camada semântica/analítica para os indicadores de Gestão à Vista (Painel PMO),
separada do schema [dbo] operacional. Nenhuma tabela/view operacional existente é movida ou alterada;
este schema só recebe objetos NOVOS, construídos em cima de fontes já existentes em [dbo].

============================================================
DEPENDÊNCIAS
============================================================
Nenhuma. Primeiro script da série (Migrations/GestaoVista/README.md define a ordem completa).

============================================================
PRÉ-CHECK + CREATE (mesmo lote — nunca depender de RETURN atravessar um GO: lotes posteriores
executam de qualquer forma mesmo depois de um RETURN/RAISERROR num lote anterior)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'gestao')
BEGIN
    EXEC('CREATE SCHEMA gestao');
    PRINT 'Schema [gestao] criado.';
END
ELSE
BEGIN
    PRINT 'Schema [gestao] já existe — nada a fazer. Migration idempotente.';
END
GO

/*
============================================================
ÍNDICES
============================================================
Não aplicável — schema não tem estrutura própria de índice.

============================================================
VALIDAÇÃO
============================================================
SELECT name FROM sys.schemas WHERE name = 'gestao'; -- deve devolver 1 linha após execução.

============================================================
ROLLBACK (NUNCA executar automaticamente — só manualmente, e só depois de remover TODOS os objetos
filhos criados dentro do schema pelos scripts 02/03/04)
============================================================
-- DROP SCHEMA gestao;
*/
