/*
============================================================
OBJETIVO
============================================================
Criar config.Norma (norma de nomenclatura cadastrável, ex.: "ISO 19650") e config.NormaMapeamento
(regras sigla → pasta destino → tipo de documento, 1:N por norma). Conceito confirmado pela aba
"Configuração de Normas" (ClientApp/src/pages/ControlPanel.tsx) e pelo import de Excel que produz
{sigla, pasta_Destino, tipo_Documento, ignorar}. É DISTINTO de dbo.Projetos.Naming_Standard (enum
operacional "NA"/"CLIENT" usado pelo Worker) — ver AUDITORIA_FORGESYNC_NA/16-*.md §Parte 2/9. Nenhuma
tabela equivalente existe hoje (confirmado por inventário completo do banco).

UNIQUE (NormaId, Sigla) em NormaMapeamento — ver checkpoint de revisão no documento 16: o import de
Excel (ControlPanel.tsx, handleFileUpload) NÃO deduplica por sigla, e a tabela de mapeamentos é semântica
e visualmente uma tabela de roteamento (Sigla → 1 Pasta de Destino → 1 Tipo); nenhuma evidência no
frontend comprova a necessidade de múltiplas regras para a mesma sigla dentro de uma Norma. A ausência de
dedupe no import é exatamente o risco que este UNIQUE previne (Excel reimportado, ou chamada de API
duplicada, criando linhas conflitantes para a mesma sigla). Se uma necessidade real de 1:N por sigla for
comprovada depois, este constraint deve ser revisto — não foi comprovada até 18/09/2026.

============================================================
DEPENDÊNCIAS
============================================================
00_criar_schema_config.sql.

============================================================
IDEMPOTÊNCIA / ATOMICIDADE
============================================================
Cada tabela é checada INDIVIDUALMENTE (não só a primeira) — uma execução anterior pode ter criado só
config.Norma e falhado antes de criar config.NormaMapeamento; esta migration completa o que faltar.
Toda a criação (ambas as tabelas + índice) roda dentro de UMA transação com TRY/CATCH: se qualquer
CREATE falhar, a transação inteira é desfeita — nunca fica config.Norma criada sem
config.NormaMapeamento (ou vice-versa) por causa de uma falha no meio do script.
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'config')
BEGIN
    RAISERROR('Schema [config] não existe — execute 00_criar_schema_config.sql primeiro.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'Norma')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'NormaMapeamento')
BEGIN
    PRINT 'Tabelas [config].Norma e [config].NormaMapeamento já existem — nada a fazer. Migration idempotente.';
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'Norma')
    BEGIN
        CREATE TABLE config.Norma
        (
            NormaId             INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            Nome_Norma           NVARCHAR(200)  NOT NULL,
            Descricao            NVARCHAR(1000) NULL,
            Data_Criacao         DATETIME2      NOT NULL CONSTRAINT DF_Norma_Data_Criacao DEFAULT SYSUTCDATETIME(),

            CONSTRAINT UQ_Norma_Nome UNIQUE (Nome_Norma)
        );
        PRINT 'Tabela [config].Norma criada.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [config].Norma já existia — completando apenas o que faltava.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'NormaMapeamento')
    BEGIN
        CREATE TABLE config.NormaMapeamento
        (
            NormaMapeamentoId   INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            NormaId              INT            NOT NULL,
            Sigla                NVARCHAR(50)   NOT NULL,
            Pasta_Destino         NVARCHAR(300)  NULL,
            Tipo_Documento        NVARCHAR(200)  NULL,
            Ignorar               BIT            NOT NULL CONSTRAINT DF_NormaMapeamento_Ignorar DEFAULT 0,

            CONSTRAINT FK_NormaMapeamento_Norma FOREIGN KEY (NormaId)
                REFERENCES config.Norma (NormaId) ON DELETE CASCADE,
            CONSTRAINT UQ_NormaMapeamento_Sigla UNIQUE (NormaId, Sigla)
        );

        CREATE INDEX IX_NormaMapeamento_NormaId ON config.NormaMapeamento (NormaId);

        PRINT 'Tabela [config].NormaMapeamento e índice criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [config].NormaMapeamento já existia — nada a fazer.';
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
SELECT COUNT(*) FROM config.Norma;                 -- deve devolver 0 logo após a criação.
SELECT COUNT(*) FROM config.NormaMapeamento;        -- deve devolver 0 logo após a criação.
SELECT * FROM sys.foreign_keys WHERE name = 'FK_NormaMapeamento_Norma';
SELECT * FROM sys.key_constraints WHERE name = 'UQ_NormaMapeamento_Sigla';

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE config.NormaMapeamento;
-- DROP TABLE config.Norma;
*/
