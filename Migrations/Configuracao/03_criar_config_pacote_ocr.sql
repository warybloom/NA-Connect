/*
============================================================
OBJETIVO
============================================================
Criar config.PacoteOcr (pacote de calibração OCR, reutilizável entre projetos) e
config.PacoteOcrCoordenada (coordenadas de calibração por combinação folha x campo, 1:N por pacote).
Fonte: OCRManager.tsx (CRUD do pacote) + OCRCalibrator.tsx/OCRCalibratorPro.tsx (coordenadas X/Y/largura/
altura por TipoFolha x campo). Confirmado: um pacote é cadastrado independente de projeto e depois
associado a projetos via config.ProjetoConfiguracao.PacoteOcrId (05_criar_config_projeto_configuracao.sql).

CAMPO x CAMPO DESTINO — reaudidato em 18/09/2026 lendo os dois componentes linha a linha:
- OCRCalibrator.tsx (saveAllZones, ~linha 177-184): payload = { X, Y, Width, Height, Campo: campo,
  CampoDestino: campo } — as DUAS chaves recebem literalmente o MESMO valor (`campo`, a chave do objeto
  `zones`, que vem de `camposDisponiveis` = ["Número do Documento","Revisão","Título","Data",
  "Projetista"]).
- OCRCalibratorPro.tsx (saveAllZones, ~linha 90-96): payload = { x, y, width, height,
  campoDestino: campo } — envia SÓ campoDestino, com o mesmo valor.
CONCLUSÃO: não são dois conceitos distintos (não há "região" separada de "atributo de destino") — é o
MESMO conceito (qual campo do carimbo esta coordenada extrai), duplicado com dois nomes no componente
mais antigo (OCRCalibrator.tsx), provavelmente resíduo de um DTO do ForgeSync que exigia ambos os nomes
antes de uma renomeação incompleta. Nome canônico escolhido: CampoDestino (mais explícito — "campo de
destino do dado extraído" — e já é o único nome usado pelo componente mais novo, OCRCalibratorPro.tsx).
A coluna `Campo` NÃO foi criada. Quando o backend for implementado, deve aceitar ambos os nomes de
payload (`Campo` e `CampoDestino`) e gravar sempre em CampoDestino, ignorando `Campo` quando presente —
documentado em §6 do documento 16 (atualizado neste checkpoint).

============================================================
DEPENDÊNCIAS
============================================================
00_criar_schema_config.sql.

============================================================
IDEMPOTÊNCIA / ATOMICIDADE
============================================================
Cada tabela é checada INDIVIDUALMENTE — uma execução anterior pode ter criado só config.PacoteOcr e
falhado antes de criar config.PacoteOcrCoordenada; esta migration completa o que faltar. Toda a criação
(ambas as tabelas + índice) roda dentro de UMA transação com TRY/CATCH: se qualquer CREATE falhar, a
transação inteira é desfeita — nunca fica config.PacoteOcr criada sem config.PacoteOcrCoordenada (ou
vice-versa) por causa de uma falha no meio do script.
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'config')
BEGIN
    RAISERROR('Schema [config] não existe — execute 00_criar_schema_config.sql primeiro.', 16, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'PacoteOcr')
   AND EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'PacoteOcrCoordenada')
BEGIN
    PRINT 'Tabelas [config].PacoteOcr e [config].PacoteOcrCoordenada já existem — nada a fazer. Migration idempotente.';
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'PacoteOcr')
    BEGIN
        CREATE TABLE config.PacoteOcr
        (
            PacoteOcrId          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            Nome_Pacote           NVARCHAR(200)  NOT NULL,
            Data_Criacao          DATETIME2      NOT NULL CONSTRAINT DF_PacoteOcr_Data_Criacao DEFAULT SYSUTCDATETIME(),

            CONSTRAINT UQ_PacoteOcr_Nome UNIQUE (Nome_Pacote)
        );
        PRINT 'Tabela [config].PacoteOcr criada.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [config].PacoteOcr já existia — completando apenas o que faltava.';
    END

    IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'PacoteOcrCoordenada')
    BEGIN
        CREATE TABLE config.PacoteOcrCoordenada
        (
            PacoteOcrCoordenadaId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            PacoteOcrId            INT            NOT NULL,
            TipoFolha              NVARCHAR(50)   NOT NULL,
            CampoDestino           NVARCHAR(100)  NOT NULL,   -- ver nota CAMPO x CAMPO DESTINO em OBJETIVO
            X                      DECIMAL(18,6)  NOT NULL,
            Y                      DECIMAL(18,6)  NOT NULL,
            Width                  DECIMAL(18,6)  NOT NULL,
            Height                 DECIMAL(18,6)  NOT NULL,

            CONSTRAINT FK_PacoteOcrCoordenada_Pacote FOREIGN KEY (PacoteOcrId)
                REFERENCES config.PacoteOcr (PacoteOcrId) ON DELETE CASCADE,
            CONSTRAINT UQ_PacoteOcrCoordenada_Campo UNIQUE (PacoteOcrId, TipoFolha, CampoDestino)
        );

        CREATE INDEX IX_PacoteOcrCoordenada_PacoteOcrId ON config.PacoteOcrCoordenada (PacoteOcrId);

        PRINT 'Tabela [config].PacoteOcrCoordenada e índice criados.';
    END
    ELSE
    BEGIN
        PRINT 'Tabela [config].PacoteOcrCoordenada já existia — nada a fazer.';
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
SELECT COUNT(*) FROM config.PacoteOcr;             -- deve devolver 0 logo após a criação.
SELECT COUNT(*) FROM config.PacoteOcrCoordenada;    -- deve devolver 0 logo após a criação.
SELECT * FROM sys.foreign_keys WHERE name = 'FK_PacoteOcrCoordenada_Pacote';
SELECT * FROM sys.key_constraints WHERE name = 'UQ_PacoteOcrCoordenada_Campo';

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE config.PacoteOcrCoordenada;
-- DROP TABLE config.PacoteOcr;
*/
