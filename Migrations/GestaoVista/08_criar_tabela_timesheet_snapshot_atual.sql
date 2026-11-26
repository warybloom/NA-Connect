/*
============================================================
OBJETIVO
============================================================
Criar a tabela física gestao.TimesheetSnapshotAtual — grão: 1 lançamento ATUAL (pessoa x documento) da
aba TIMESHEET da LDP, para o PROJETO. Distinta, deliberadamente, de gestao.TimesheetLancamento
(05_criar_tabela_timesheet_lancamento.sql).

============================================================
POR QUE UMA TABELA NOVA (auditoria 17/09/2026)
============================================================
gestao.TimesheetLancamento foi construída como um LEDGER HISTÓRICO, apend-only, idempotente por HASH DE
CONTEÚDO (UNIQUE (Project_ID_ACC, RowHash), hash inclui HorasTrabalhadas — ver GestaoVistaRowHasher.
Timesheet e o comentário de 05_criar_tabela_timesheet_lancamento.sql). Isso é INTENCIONAL: uma célula de
horas corrigida entre sincronizações gera uma NOVA linha, a antiga nunca é apagada nem atualizada — nunca
existiu um conceito de "linha atual" nessa tabela, e ela não tem PK natural (pessoa+documento) nem
qualquer marca de vigência (sem VigenciaInicio/Fim como CustoProfissionalRealHistorico).

Consequência prática, confirmada em produção (CHS001, 17/09/2026): SUM(HorasTrabalhadas) sobre
gestao.TimesheetLancamento devolvia 4.737,52h, enquanto a aba TIMESHEET real (fonte da Etapa 01,
tbTimeSheetRealizadoDocumento) somava 5.447,68h — a tabela histórica não reflete "o estado atual da
planilha", e não HÁ como fazer isso ler apenas rodando um WHERE/GROUP BY em cima dela sem destruir sua
semântica de histórico (que se pretende preservar, nunca apagada por este projeto).

Por isso: NOVA tabela, semântica de SNAPSHOT (delete-and-replace por Project_ID_ACC, nunca histórico,
nunca hash de idempotência) — grão idêntico a TimesheetLancamento, MAIS as colunas Doc1..Doc5 (que
TimesheetLancamento nunca teve — ver comentário em Models/GestaoVistaTimesheetLancamentoExtraido.cs,
"candidato a uma migration futura"). Alimentada pela ETAPA 03
(Services/GestaoVista/GestaoVistaTimesheetSnapshotRepository.cs).

REGRA ARQUITETURAL DEFINITIVA (correção 17/09/2026, revisão): a ETAPA 03 é ADITIVA — ela NUNCA substitui
LdpGestaoVistaSyncService.PersistirTimesheetAsync, que CONTINUA sendo chamado normalmente e CONTINUA
alimentando gestao.TimesheetLancamento (ledger histórico) exatamente como sempre alimentou. As duas
tabelas coexistem, com finalidades diferentes (histórico vs. estado atual) — isto NUNCA é "dupla
persistência problemática". Só o CONSUMO para o KPI de HH atual (PmoActualFactsProvider/
PmoExecutiveData) passou a ler gestao.TimesheetSnapshotAtual em vez do ledger — a escrita do ledger
continua intacta e é responsabilidade exclusiva do fluxo antigo (Custo Real/Custo Previsto/Timesheet,
todos os três, como sempre foi).

Esta migration cria SÓ A ESTRUTURA. A alimentação é responsabilidade do repositório
(Services/GestaoVista/GestaoVistaTimesheetSnapshotRepository.cs) — nunca escreve na LDP.

============================================================
DEPENDÊNCIAS
============================================================
01_criar_schema_gestao.sql.

============================================================
PRÉ-CHECK + CREATE + ÍNDICES (mesmo lote — nunca depender de RETURN atravessar um GO)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'gestao')
BEGIN
    RAISERROR('Schema [gestao] não existe — execute 01_criar_schema_gestao.sql primeiro.', 16, 1);
END
ELSE IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('gestao') AND name = 'TimesheetSnapshotAtual')
BEGIN
    PRINT 'Tabela [gestao].TimesheetSnapshotAtual já existe — nada a fazer. Migration idempotente.';
END
ELSE
BEGIN
    CREATE TABLE gestao.TimesheetSnapshotAtual
    (
        Id                  BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Project_ID_ACC      NVARCHAR(100)  NOT NULL,
        NomeProfissional    NVARCHAR(300)  NOT NULL,
        HorasPlanejadas     DECIMAL(18,6)  NULL,
        HorasTrabalhadas    DECIMAL(18,6)  NULL,
        Projeto             NVARCHAR(300)  NULL,
        Funcao              NVARCHAR(200)  NULL,
        Disciplina          NVARCHAR(200)  NULL,
        DocumentoCodigo     NVARCHAR(300)  NULL,
        Documento           NVARCHAR(500)  NULL,
        Descricao           NVARCHAR(1000) NULL,
        Doc1                NVARCHAR(200)  NULL,
        Doc2                NVARCHAR(200)  NULL,
        Doc3                NVARCHAR(200)  NULL,
        Doc4                NVARCHAR(200)  NULL,
        Doc5                NVARCHAR(200)  NULL,
        DataLancamento      DATE           NULL,   -- nunca inventada; NULL quando a aba não tem coluna de data
        CustoCalculado      DECIMAL(18,6)  NULL,    -- HorasTrabalhadas x CustoHora vigente, mesma regra da Gestão à Vista
        Data_Sincronizacao  DATETIME2      NOT NULL CONSTRAINT DF_TimesheetSnapshotAtual_Data_Sincronizacao DEFAULT SYSUTCDATETIME()

        -- Deliberadamente SEM RowHash/UNIQUE por hash: esta tabela é um SNAPSHOT (delete-and-replace por
        -- Project_ID_ACC a cada execução da Etapa 03), nunca um ledger idempotente por conteúdo.
    );

    CREATE INDEX IX_TimesheetSnapshotAtual_Projeto
        ON gestao.TimesheetSnapshotAtual (Project_ID_ACC);

    CREATE INDEX IX_TimesheetSnapshotAtual_Documento
        ON gestao.TimesheetSnapshotAtual (Project_ID_ACC, DocumentoCodigo);

    PRINT 'Tabela [gestao].TimesheetSnapshotAtual e índices criados.';
END
GO

/*
============================================================
VALIDAÇÃO
============================================================
SELECT COUNT(*) FROM gestao.TimesheetSnapshotAtual; -- deve devolver 0 logo após a criação.
SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('gestao.TimesheetSnapshotAtual');

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE gestao.TimesheetSnapshotAtual;
*/
