/*
    ============================================================================================
    QUALIDADE — PASSO 1 de 2: VERIFICAÇÃO DE DEPENDÊNCIAS (SOMENTE LEITURA)
    ============================================================================================
    Rode este script ANTES de 02_migracao_tabelas.sql. Ele não altera nada — apenas lista Views,
    Stored Procedures e Functions que referenciam [dbo].[NCR NONCONFORMITY REPORT] hoje (antes do
    rename). Depois do rename (passo 2), essas dependências continuarão apontando para o NOME
    ANTIGO fisicamente — mas semanticamente a tabela para a qual elas foram escritas passa a se
    chamar [dbo].[NCR BASE LANCAMENTO]. Cada dependência listada aqui precisa ser avaliada
    manualmente (seção 14 do pedido):
        - Se o objeto foi escrito para consultar dados de BASE LANÇAMENTO (Cliente, CR, CodigoNA,
          CodigoCliente, Descricao, Disciplina, Revisao, NumeroVias, Mes, DataEnvio, Emitente,
          GRD) -> deve passar a apontar para [dbo].[NCR BASE LANCAMENTO].
        - Se o objeto foi escrito assumindo o schema NOVO de RNC (DataRegistro, CodigoFormulario,
          Projeto, DescricaoSituacao, ...) -> não existia schema assim antes da migração, então
          nenhuma dependência legítima deveria estar nesse caso; investigar antes de assumir.
    NENHUMA dependência é alterada automaticamente por este script.
    ============================================================================================
*/

SELECT
    referencing_schema.name  AS SchemaObjetoReferenciador,
    referencing_object.name  AS ObjetoReferenciador,
    referencing_object.type_desc AS TipoObjetoReferenciador,
    dep.referenced_schema_name AS SchemaReferenciado,
    dep.referenced_entity_name AS TabelaReferenciada,
    dep.is_ambiguous AS ReferenciaAmbigua
FROM sys.sql_expression_dependencies AS dep
INNER JOIN sys.objects AS referencing_object
    ON referencing_object.object_id = dep.referencing_id
INNER JOIN sys.schemas AS referencing_schema
    ON referencing_schema.schema_id = referencing_object.schema_id
WHERE dep.referenced_entity_name = N'NCR NONCONFORMITY REPORT'
ORDER BY TipoObjetoReferenciador, ObjetoReferenciador;

-- Se a consulta acima não retornar linhas, não há Views/Procedures/Functions no banco referenciando
-- a tabela por nome resolvível estaticamente (dependências construídas via SQL dinâmico/EXEC não
-- aparecem aqui — se houver stored procedures que montam SQL dinamicamente, precisam ser revisadas
-- manualmente, pesquisando o texto "NCR NONCONFORMITY REPORT" em sys.sql_modules.definition).

SELECT
    referencing_schema.name AS SchemaObjeto,
    referencing_object.name AS Objeto,
    referencing_object.type_desc AS Tipo
FROM sys.sql_modules AS modules
INNER JOIN sys.objects AS referencing_object
    ON referencing_object.object_id = modules.object_id
INNER JOIN sys.schemas AS referencing_schema
    ON referencing_schema.schema_id = referencing_object.schema_id
WHERE modules.definition LIKE N'%NCR NONCONFORMITY REPORT%'
ORDER BY Tipo, Objeto;
