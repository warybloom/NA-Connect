/*
============================================================
OBJETIVO
============================================================
Criar ldp.ProjetoConfiguracao (vínculo de um projeto real, por Project_ID_ACC, ao Template/Mapeamento/
Espelho/Controle do Novo Motor de LDP + Estrutura do Projeto), ldp.ProjetoFase (fases do projeto,
quando Estrutura = PROJETO_POR_FASE) e ldp.ProjetoFaseRegra (regras de identificação de cada fase,
1:N). Fonte: AUDITORIA_FORGESYNC_NA/17-*.md (rodada 3) — "Suporte a Projetos Únicos e Projetos por
Fase".

ISOLAMENTO — este `ldp.ProjetoConfiguracao` é uma tabela NOVA e DISTINTA de `config.ProjetoConfiguracao`
(schema `config`, criada na fase 1 para Norma/PacoteOcr — legado, NÃO tocado aqui). Os nomes são
parecidos de propósito (mesmo conceito — "vínculo projeto→configuração" — mas em domínios
completamente diferentes e schemas diferentes). `dbo.Projetos` não é alterada — nenhuma coluna nova,
nenhum ALTER TABLE. `dbo.Projetos.Tipo_LDP` continua sendo o único conceito legado de tipo de LDP;
ModoEstrutura (PROJETO_UNICO/PROJETO_POR_FASE) é um conceito NOVO e INDEPENDENTE — nunca inferir um a
partir do outro.

REGRAS (ProjetoFaseRegra.CampoOrigem) — NVARCHAR livre (nome do campo do dataset importado/Template),
não FK: mesma decisão de modelagem já usada em CronogramaMapeamentoCampo.Coluna_Cronograma_Origem — a
regra opera sobre o NOME de um campo do dataset de entrada (ex.: "WBS" de um MPP/Excel importado), que
é um universo diferente (e mais amplo) do que os campos já formalizados em ldp.TemplateCampo.

OVERRIDE POR FASE — colunas *IdOverride em ProjetoFase são NULLABLE e não usadas por nenhuma tela
ainda (UI mostra só "Herdar Configuração do Projeto"). Criadas agora deliberadamente para que a
funcionalidade de override futura não exija ALTER TABLE — comportamento pedido explicitamente
("preparar arquitetura... sem exigir remodelagem do banco").

DOCUMENTO x FASE — nenhuma tabela de vínculo Documento↔Fase é criada aqui (não existe ainda nenhuma
tabela de dados/documento no motor novo — ver checkpoint anterior, §12). A classificação é calculada
dinamicamente pelo motor de regras (ver ClientApp/src/pages/ldp/faseRuleEngine.ts) a partir do dataset
de preview, nunca persistida por linha nesta fase. Nada aqui impede uma futura tabela de junção N:N
Documento↔Fase — é só não implementada agora, por não haver ainda uma tabela de Documento para
referenciar.

============================================================
DEPENDÊNCIAS
============================================================
00_criar_schema_ldp.sql, 01_criar_ldp_template.sql, 02_criar_ldp_cronograma_mapeamento.sql,
03_criar_ldp_excel_espelho.sql, 04_criar_ldp_excel_controle.sql. dbo.Projetos.Project_ID_ACC já tem
UNIQUE constraint (UQ_Project_ID_ACC, confirmado nas fases anteriores) — nenhuma alteração necessária
em dbo.Projetos.

============================================================
IDEMPOTÊNCIA / ATOMICIDADE
============================================================
Cada tabela é checada INDIVIDUALMENTE, criação dentro de UMA transação com TRY/CATCH (mesmo padrão de
01/02/03/04).
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'ldp')
BEGIN
    RAISERROR('Schema [ldp] não existe — execute 00_criar_schema_ldp.sql primeiro.', 16, 1);
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'Template')
   OR NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'CronogramaMapeamento')
   OR NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelEspelho')
   OR NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ExcelControle')
BEGIN
    RAISERROR('Tabelas [ldp].Template/CronogramaMapeamento/ExcelEspelho/ExcelControle não existem — execute 01/02/03/04 primeiro.', 16, 1);
    RETURN;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Projetos') AND name = 'UQ_Project_ID_ACC')
BEGIN
    RAISERROR('dbo.Projetos.Project_ID_ACC não tem UNIQUE constraint (UQ_Project_ID_ACC) — abortando, esta migration NUNCA altera dbo.Projetos.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ProjetoConfiguracao')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ProjetoFase')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ProjetoFaseRegra')
BEGIN
    PRINT 'Tabelas [ldp].ProjetoConfiguracao/ProjetoFase/ProjetoFaseRegra já existem — nada a fazer. Migration idempotente.';
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ProjetoConfiguracao')
    BEGIN
        CREATE TABLE ldp.ProjetoConfiguracao
        (
            Project_ID_ACC        NVARCHAR(255)  NOT NULL PRIMARY KEY,
            ModoEstrutura           NVARCHAR(20)   NOT NULL CONSTRAINT DF_LdpProjetoConfiguracao_ModoEstrutura DEFAULT 'PROJETO_UNICO',
            ModoGeracaoFase         NVARCHAR(20)   NULL,
            TemplateId              INT            NULL,
            CronogramaMapeamentoId  INT            NULL,
            ExcelEspelhoId          INT            NULL,
            ExcelControleId         INT            NULL,
            Data_Atualizacao        DATETIME2      NOT NULL CONSTRAINT DF_LdpProjetoConfiguracao_DataAtualizacao DEFAULT SYSUTCDATETIME(),

            CONSTRAINT FK_LdpProjetoConfiguracao_Projeto FOREIGN KEY (Project_ID_ACC)
                REFERENCES dbo.Projetos (Project_ID_ACC) ON DELETE CASCADE,
            CONSTRAINT FK_LdpProjetoConfiguracao_Template FOREIGN KEY (TemplateId)
                REFERENCES ldp.Template (TemplateId) ON DELETE NO ACTION,
            CONSTRAINT FK_LdpProjetoConfiguracao_Mapeamento FOREIGN KEY (CronogramaMapeamentoId)
                REFERENCES ldp.CronogramaMapeamento (CronogramaMapeamentoId) ON DELETE NO ACTION,
            CONSTRAINT FK_LdpProjetoConfiguracao_Espelho FOREIGN KEY (ExcelEspelhoId)
                REFERENCES ldp.ExcelEspelho (ExcelEspelhoId) ON DELETE NO ACTION,
            CONSTRAINT FK_LdpProjetoConfiguracao_Controle FOREIGN KEY (ExcelControleId)
                REFERENCES ldp.ExcelControle (ExcelControleId) ON DELETE NO ACTION,
            CONSTRAINT CK_LdpProjetoConfiguracao_ModoEstrutura CHECK (ModoEstrutura IN ('PROJETO_UNICO', 'PROJETO_POR_FASE')),
            CONSTRAINT CK_LdpProjetoConfiguracao_ModoGeracaoFase CHECK (ModoGeracaoFase IS NULL OR ModoGeracaoFase IN ('ARQUIVO_POR_FASE', 'ABA_POR_FASE', 'CONSOLIDADO'))
        );

        CREATE INDEX IX_LdpProjetoConfiguracao_TemplateId ON ldp.ProjetoConfiguracao (TemplateId);

        PRINT 'Tabela [ldp].ProjetoConfiguracao e índice criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].ProjetoConfiguracao já existia — completando apenas o que faltava.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ProjetoFase')
    BEGIN
        CREATE TABLE ldp.ProjetoFase
        (
            ProjetoFaseId          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            Project_ID_ACC          NVARCHAR(255)  NOT NULL,
            Codigo                  NVARCHAR(50)   NOT NULL,
            Nome                    NVARCHAR(200)  NOT NULL,
            Ordem                   INT            NOT NULL CONSTRAINT DF_LdpProjetoFase_Ordem DEFAULT 0,
            Ativa                   BIT            NOT NULL CONSTRAINT DF_LdpProjetoFase_Ativa DEFAULT 1,
            OperadorLogico          NVARCHAR(5)    NOT NULL CONSTRAINT DF_LdpProjetoFase_OperadorLogico DEFAULT 'ALL',
            -- Override futuro (ver OBJETIVO) — nullable, nenhuma UI usa ainda. NULL = herda do projeto.
            TemplateIdOverride              INT NULL,
            CronogramaMapeamentoIdOverride   INT NULL,
            ExcelEspelhoIdOverride           INT NULL,
            ExcelControleIdOverride          INT NULL,

            CONSTRAINT FK_LdpProjetoFase_ProjetoConfiguracao FOREIGN KEY (Project_ID_ACC)
                REFERENCES ldp.ProjetoConfiguracao (Project_ID_ACC) ON DELETE CASCADE,
            CONSTRAINT FK_LdpProjetoFase_TemplateOverride FOREIGN KEY (TemplateIdOverride)
                REFERENCES ldp.Template (TemplateId) ON DELETE NO ACTION,
            CONSTRAINT FK_LdpProjetoFase_MapeamentoOverride FOREIGN KEY (CronogramaMapeamentoIdOverride)
                REFERENCES ldp.CronogramaMapeamento (CronogramaMapeamentoId) ON DELETE NO ACTION,
            CONSTRAINT FK_LdpProjetoFase_EspelhoOverride FOREIGN KEY (ExcelEspelhoIdOverride)
                REFERENCES ldp.ExcelEspelho (ExcelEspelhoId) ON DELETE NO ACTION,
            CONSTRAINT FK_LdpProjetoFase_ControleOverride FOREIGN KEY (ExcelControleIdOverride)
                REFERENCES ldp.ExcelControle (ExcelControleId) ON DELETE NO ACTION,
            CONSTRAINT UQ_LdpProjetoFase_Codigo UNIQUE (Project_ID_ACC, Codigo),
            CONSTRAINT CK_LdpProjetoFase_OperadorLogico CHECK (OperadorLogico IN ('ALL', 'ANY'))
        );

        CREATE INDEX IX_LdpProjetoFase_ProjectIdAcc ON ldp.ProjetoFase (Project_ID_ACC);

        PRINT 'Tabela [ldp].ProjetoFase e índice criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].ProjetoFase já existia — nada a fazer.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('ldp') AND name = 'ProjetoFaseRegra')
    BEGIN
        CREATE TABLE ldp.ProjetoFaseRegra
        (
            ProjetoFaseRegraId     INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            ProjetoFaseId           INT            NOT NULL,
            CampoOrigem             NVARCHAR(200)  NOT NULL,
            Operador                NVARCHAR(20)   NOT NULL,
            ValorComparacao         NVARCHAR(500)  NOT NULL,
            Ordem                   INT            NOT NULL CONSTRAINT DF_LdpProjetoFaseRegra_Ordem DEFAULT 0,
            Ativa                   BIT            NOT NULL CONSTRAINT DF_LdpProjetoFaseRegra_Ativa DEFAULT 1,

            CONSTRAINT FK_LdpProjetoFaseRegra_ProjetoFase FOREIGN KEY (ProjetoFaseId)
                REFERENCES ldp.ProjetoFase (ProjetoFaseId) ON DELETE CASCADE,
            CONSTRAINT CK_LdpProjetoFaseRegra_Operador CHECK (Operador IN ('IGUAL_A', 'CONTEM', 'COMECA_COM', 'TERMINA_COM'))
        );

        CREATE INDEX IX_LdpProjetoFaseRegra_ProjetoFaseId ON ldp.ProjetoFaseRegra (ProjetoFaseId);

        PRINT 'Tabela [ldp].ProjetoFaseRegra e índice criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [ldp].ProjetoFaseRegra já existia — nada a fazer.';
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
SELECT COUNT(*) FROM ldp.ProjetoConfiguracao;   -- deve devolver 0 logo após a criação.
SELECT COUNT(*) FROM ldp.ProjetoFase;             -- idem.
SELECT COUNT(*) FROM ldp.ProjetoFaseRegra;        -- idem.
SELECT * FROM sys.check_constraints WHERE parent_object_id IN
    (OBJECT_ID('ldp.ProjetoConfiguracao'), OBJECT_ID('ldp.ProjetoFase'), OBJECT_ID('ldp.ProjetoFaseRegra'));

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE ldp.ProjetoFaseRegra;
-- DROP TABLE ldp.ProjetoFase;
-- DROP TABLE ldp.ProjetoConfiguracao;
*/
