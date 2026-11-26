/* =====================================================================================================================
   BI CHS001 (Original) — migração ISOLADA 001 (UP) — 15/10/2026
   Cria SOMENTE objetos novos no schema bi_chs001. Não executa ALTER/UPDATE/DELETE em dbo nem em nenhum objeto existente.
   Idempotente: pode rodar várias vezes (IF NOT EXISTS / CREATE OR ALTER). Reversão: 001_bi_chs001_DOWN.sql.
   Fonte dos dados: cópia do .mpp oficial lida por Services/BiChs001Schedule (MPXJ), gravada numa transação por versão.
   ===================================================================================================================== */
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF SCHEMA_ID(N'bi_chs001') IS NULL EXEC(N'CREATE SCHEMA bi_chs001 AUTHORIZATION dbo');

/* Registro das migrações deste schema. */
IF OBJECT_ID(N'bi_chs001.Migracao', N'U') IS NULL
    CREATE TABLE bi_chs001.Migracao
    (
        Versao      nvarchar(50)  NOT NULL CONSTRAINT PK_bi_chs001_Migracao PRIMARY KEY,
        Descricao   nvarchar(400) NOT NULL,
        Aplicada_Em datetime2(0)  NOT NULL CONSTRAINT DF_bi_chs001_Migracao_Em DEFAULT SYSDATETIME(),
        Aplicada_Por nvarchar(256) NOT NULL CONSTRAINT DF_bi_chs001_Migracao_Por DEFAULT SUSER_SNAME()
    );

/* Uma linha por leitura do .mpp (versão). Gravada na MESMA transação das tarefas e da série. */
IF OBJECT_ID(N'bi_chs001.Versao_Cronograma', N'U') IS NULL
    CREATE TABLE bi_chs001.Versao_Cronograma
    (
        Versao_Id              int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_bi_chs001_Versao PRIMARY KEY,
        Project_ID_ACC         nvarchar(100)  NOT NULL,
        Status                 nvarchar(20)   NOT NULL CONSTRAINT DF_bi_chs001_Versao_Status DEFAULT N'COMPLETA',
        Origem                 nvarchar(20)   NOT NULL,            -- ACC | ARQUIVO
        Arquivo_Nome           nvarchar(400)  NOT NULL,
        Arquivo_Item_Id        nvarchar(400)  NULL,
        Arquivo_Versao_Urn     nvarchar(600)  NULL,
        Arquivo_Sha256         char(64)       NOT NULL,
        Arquivo_Bytes          bigint         NOT NULL,
        Data_Status_Projeto    datetime2(0)   NULL,                -- ProjectProperties.StatusDate
        Calendario_Padrao      nvarchar(200)  NULL,
        Minutos_Por_Dia        decimal(18, 6) NULL,
        Leitor                 nvarchar(100)  NOT NULL,
        Lido_Em_Utc            datetime2(3)   NOT NULL,
        Gravado_Em_Utc         datetime2(3)   NOT NULL CONSTRAINT DF_bi_chs001_Versao_Gravado DEFAULT SYSUTCDATETIME(),
        Executado_Por          nvarchar(256)  NULL,
        Total_Tarefas          int            NOT NULL,
        Total_Folhas           int            NOT NULL,
        Total_Linhas_Diarias   int            NOT NULL,
        Total_Atribuicoes      int            NOT NULL,
        Diagnostico_JSON       nvarchar(max)  NULL,
        CONSTRAINT CK_bi_chs001_Versao_Status CHECK (Status IN (N'COMPLETA')),
        CONSTRAINT CK_bi_chs001_Versao_Origem CHECK (Origem IN (N'ACC', N'ARQUIVO'))
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_bi_chs001_Versao_Projeto_Sha' AND object_id = OBJECT_ID(N'bi_chs001.Versao_Cronograma'))
    CREATE UNIQUE INDEX UX_bi_chs001_Versao_Projeto_Sha ON bi_chs001.Versao_Cronograma (Project_ID_ACC, Arquivo_Sha256);

/* Tarefas da versão (resumo + folha), campos nativos do MPXJ. */
IF OBJECT_ID(N'bi_chs001.Tarefa_Snapshot', N'U') IS NULL
    CREATE TABLE bi_chs001.Tarefa_Snapshot
    (
        Versao_Id                 int            NOT NULL,
        Project_ID_ACC            nvarchar(100)  NOT NULL,
        UID_Tarefa                int            NOT NULL,
        Ordem                     int            NOT NULL,
        GUID_Tarefa               nvarchar(64)   NULL,
        ID_Tarefa                 int            NULL,
        UID_Tarefa_Pai            int            NULL,
        Nivel_Estrutura           int            NULL,
        Eh_Resumo                 bit            NOT NULL,
        Eh_Marco                  bit            NOT NULL,
        Nome                      nvarchar(1000) NOT NULL,
        EDT                       nvarchar(200)  NULL,
        Inicio                    datetime2(0)   NULL,
        Termino                   datetime2(0)   NULL,
        Inicio_Linha_Base         datetime2(0)   NULL,
        Termino_Linha_Base        datetime2(0)   NULL,
        Trabalho_Horas            decimal(18, 6) NULL,
        Trabalho_Real_Horas       decimal(18, 6) NULL,
        Trabalho_Linha_Base_Horas decimal(18, 6) NULL,
        Trabalho_Restante_Horas   decimal(18, 6) NULL,
        Percentual_Concluido      decimal(18, 6) NULL,
        Qtde_Atribuicoes          int            NOT NULL,
        Calendario                nvarchar(200)  NULL,
        Campos_Customizados_JSON  nvarchar(max)  NULL,
        CONSTRAINT PK_bi_chs001_Tarefa PRIMARY KEY (Versao_Id, UID_Tarefa),
        CONSTRAINT FK_bi_chs001_Tarefa_Versao FOREIGN KEY (Versao_Id) REFERENCES bi_chs001.Versao_Cronograma (Versao_Id) ON DELETE CASCADE
    );

/* Trabalho por tarefa × dia (soma das atribuições), série timephased do .mpp. */
IF OBJECT_ID(N'bi_chs001.Trabalho_Tarefa_Dia', N'U') IS NULL
    CREATE TABLE bi_chs001.Trabalho_Tarefa_Dia
    (
        Versao_Id                 int            NOT NULL,
        Project_ID_ACC            nvarchar(100)  NOT NULL,
        UID_Tarefa                int            NOT NULL,
        Data                      date           NOT NULL,
        Trabalho_Real_Horas       decimal(18, 6) NULL,   -- ResourceAssignment.getTimephasedActualWork()
        Trabalho_Linha_Base_Horas decimal(18, 6) NULL,   -- getTimephasedBaselineWork(0)
        Trabalho_Planejado_Horas  decimal(18, 6) NULL,   -- getTimephasedWork()
        CONSTRAINT PK_bi_chs001_Trabalho_Dia PRIMARY KEY (Versao_Id, UID_Tarefa, Data),
        CONSTRAINT FK_bi_chs001_Trabalho_Dia_Versao FOREIGN KEY (Versao_Id) REFERENCES bi_chs001.Versao_Cronograma (Versao_Id) ON DELETE CASCADE
    );

/* Correspondência EXPLÍCITA e auditável de rótulo, restrita a um projeto — aplicada SÓ na camada do BI (valor bruto preservado). */
IF OBJECT_ID(N'bi_chs001.Alias_Rotulo', N'U') IS NULL
    CREATE TABLE bi_chs001.Alias_Rotulo
    (
        Alias_Id        int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_bi_chs001_Alias PRIMARY KEY,
        Project_ID_ACC  nvarchar(100)  NOT NULL,
        Campo           nvarchar(50)   NOT NULL,      -- Nome (tarefa de resumo)
        Valor_Bruto     nvarchar(400)  NOT NULL,
        Valor_Canonico  nvarchar(400)  NOT NULL,
        Somente_Resumo  bit            NOT NULL CONSTRAINT DF_bi_chs001_Alias_Resumo DEFAULT 1,
        Ativo           bit            NOT NULL CONSTRAINT DF_bi_chs001_Alias_Ativo DEFAULT 1,
        Motivo          nvarchar(1000) NOT NULL,
        Registrado_Em   datetime2(0)   NOT NULL CONSTRAINT DF_bi_chs001_Alias_Em DEFAULT SYSDATETIME(),
        Registrado_Por  nvarchar(256)  NOT NULL CONSTRAINT DF_bi_chs001_Alias_Por DEFAULT SUSER_SNAME(),
        CONSTRAINT CK_bi_chs001_Alias_Campo CHECK (Campo IN (N'Nome'))
    );
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_bi_chs001_Alias' AND object_id = OBJECT_ID(N'bi_chs001.Alias_Rotulo'))
    CREATE UNIQUE INDEX UX_bi_chs001_Alias ON bi_chs001.Alias_Rotulo (Project_ID_ACC, Campo, Valor_Bruto);

IF NOT EXISTS (SELECT 1 FROM bi_chs001.Alias_Rotulo WHERE Project_ID_ACC = N'b.fcdb7ebd-1b23-440d-a04b-90391098522e' AND Campo = N'Nome' AND Valor_Bruto = N'Emgineering - 30%')
    INSERT INTO bi_chs001.Alias_Rotulo (Project_ID_ACC, Campo, Valor_Bruto, Valor_Canonico, Somente_Resumo, Ativo, Motivo)
    VALUES (N'b.fcdb7ebd-1b23-440d-a04b-90391098522e', N'Nome', N'Emgineering - 30%', N'Engineering - 30%', 1, 1,
            N'CHS001: EDT 1.5 do .mpp oficial grafada "Emgineering - 30%"; o PBIX filtra WBX[Level 4] = "Engineering - 30%" (Resumo 30%) e exclui esse valor nos Resumos 60%/90%. Correspondência só no BI CHS001; .mpp e importador Tipo 2 inalterados. Autorizado pelo usuário em 15/10/2026 (DIRETRIZ_ISOLAMENTO_OBRIGATORIA_20261015.md).');

COMMIT TRANSACTION;
GO

/* Tarefas de qualquer versão com o rótulo do BI (alias aplicado) ao lado do valor bruto. */
CREATE OR ALTER VIEW bi_chs001.VW_Tarefa
AS
SELECT T.Versao_Id, T.Project_ID_ACC, T.UID_Tarefa, T.Ordem, T.GUID_Tarefa, T.ID_Tarefa, T.UID_Tarefa_Pai, T.Nivel_Estrutura,
       T.Eh_Resumo, T.Eh_Marco, T.Nome AS Nome_Original,
       COALESCE(A.Valor_Canonico, T.Nome) AS Nome_BI,
       A.Alias_Id,
       T.EDT, T.Inicio, T.Termino, T.Inicio_Linha_Base, T.Termino_Linha_Base,
       T.Trabalho_Horas, T.Trabalho_Real_Horas, T.Trabalho_Linha_Base_Horas, T.Trabalho_Restante_Horas, T.Percentual_Concluido,
       T.Qtde_Atribuicoes, T.Calendario, T.Campos_Customizados_JSON
FROM bi_chs001.Tarefa_Snapshot AS T
LEFT JOIN bi_chs001.Alias_Rotulo AS A
       ON A.Project_ID_ACC = T.Project_ID_ACC AND A.Campo = N'Nome' AND A.Ativo = 1
      AND A.Valor_Bruto = T.Nome COLLATE Latin1_General_100_CS_AS
      AND (A.Somente_Resumo = 0 OR T.Eh_Resumo = 1);
GO

/* Última versão COMPLETA por projeto. */
CREATE OR ALTER VIEW bi_chs001.VW_Versao_Atual
AS
SELECT V.*
FROM bi_chs001.Versao_Cronograma AS V
WHERE V.Status = N'COMPLETA'
  AND V.Versao_Id = (SELECT MAX(X.Versao_Id) FROM bi_chs001.Versao_Cronograma AS X WHERE X.Project_ID_ACC = V.Project_ID_ACC AND X.Status = N'COMPLETA');
GO

/* Série semanal no grão do AssignmentByDay do PBIX: semana iniciada no DOMINGO (1900-01-07 é domingo; independe de DATEFIRST). */
CREATE OR ALTER VIEW bi_chs001.VW_Trabalho_Semana
AS
SELECT D.Versao_Id, D.Project_ID_ACC, D.UID_Tarefa,
       CONVERT(date, DATEADD(DAY, (DATEDIFF(DAY, '19000107', D.Data) / 7) * 7, '19000107')) AS Domingo,
       SUM(D.Trabalho_Real_Horas) AS Trabalho_Real_Horas,
       SUM(D.Trabalho_Linha_Base_Horas) AS Trabalho_Linha_Base_Horas,
       SUM(D.Trabalho_Planejado_Horas) AS Trabalho_Planejado_Horas,
       COUNT(*) AS Dias
FROM bi_chs001.Trabalho_Tarefa_Dia AS D
GROUP BY D.Versao_Id, D.Project_ID_ACC, D.UID_Tarefa, CONVERT(date, DATEADD(DAY, (DATEDIFF(DAY, '19000107', D.Data) / 7) * 7, '19000107'));
GO

/* Registro da migração SÓ depois de tabelas E views existirem: a API (SchemaExisteAsync) exige este registro, então uma
   instalação interrompida no meio nunca é considerada pronta. */
IF OBJECT_ID(N'bi_chs001.VW_Tarefa', N'V') IS NOT NULL AND OBJECT_ID(N'bi_chs001.VW_Versao_Atual', N'V') IS NOT NULL
   AND OBJECT_ID(N'bi_chs001.VW_Trabalho_Semana', N'V') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM bi_chs001.Migracao WHERE Versao = N'001')
    INSERT INTO bi_chs001.Migracao (Versao, Descricao) VALUES (N'001', N'Schema isolado do BI CHS001: versões, tarefas, trabalho diário, alias de rótulo e views.');
GO
