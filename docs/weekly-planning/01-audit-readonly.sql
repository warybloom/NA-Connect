-- AUDITORIA SOMENTE LEITURA. Executar manualmente na conexão já existente do sistema.
-- Nenhuma migration, DDL, DML, procedimento de importação ou Worker é chamado.
SELECT s.name AS SchemaName, o.name, o.type_desc, c.column_id, c.name AS ColumnName,
       t.name AS DataType, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id=o.schema_id
LEFT JOIN sys.columns c ON c.object_id=o.object_id
LEFT JOIN sys.types t ON t.user_type_id=c.user_type_id
WHERE o.name IN ('tb_WeeklyEngineeringSchedule','Cronograma_Projetos','Ldp_Unificada',
 'VW_Ldp_Unificada_Ultima_Versao','VW_Ldp_Unificada','Projetos')
ORDER BY o.name,c.column_id;

SELECT s.name AS SchemaName, o.name, o.type_desc
FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
WHERE o.name LIKE '%Weekly%' OR o.name LIKE '%Semana%' OR o.name LIKE '%Disciplina%'
   OR o.name LIKE '%Documento%' OR o.name LIKE '%Document%' OR o.name LIKE '%Ldp%';

SELECT OBJECT_SCHEMA_NAME(i.object_id) AS SchemaName, OBJECT_NAME(i.object_id) AS ObjectName,
       i.name, i.is_unique, i.is_primary_key, c.name AS ColumnName, ic.key_ordinal
FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
WHERE i.object_id IN (OBJECT_ID('dbo.tb_WeeklyEngineeringSchedule'),OBJECT_ID('dbo.Ldp_Unificada'),OBJECT_ID('dbo.Projetos'));

SELECT OBJECT_SCHEMA_NAME(referencing_id) AS ReferencingSchema, OBJECT_NAME(referencing_id) AS ReferencingObject,
       referenced_schema_name, referenced_entity_name
FROM sys.sql_expression_dependencies
WHERE referenced_entity_name IN ('tb_WeeklyEngineeringSchedule','Ldp_Unificada','Cronograma_Projetos');

SELECT o.name,m.definition FROM sys.objects o JOIN sys.sql_modules m ON m.object_id=o.object_id
WHERE o.name IN ('VW_Ldp_Unificada','VW_Ldp_Unificada_Ultima_Versao')
   OR m.definition LIKE '%tb_WeeklyEngineeringSchedule%';

IF OBJECT_ID('dbo.tb_WeeklyEngineeringSchedule','U') IS NOT NULL
BEGIN
 SELECT COUNT_BIG(*) AS TotalRows, COUNT(DISTINCT Project_ID_ACC) AS Projects,
        MIN(Data_Sincronizacao) AS OldestSync, MAX(Data_Sincronizacao) AS LatestSync
 FROM dbo.tb_WeeklyEngineeringSchedule;
 SELECT Tipo_Registro,Status_Importacao,COUNT_BIG(*) AS Rows
 FROM dbo.tb_WeeklyEngineeringSchedule GROUP BY Tipo_Registro,Status_Importacao;
END;
-- Confirmar também jobs/consumidores externos com o DBA: sys.sql_expression_dependencies não detecta SQL dinâmico.
