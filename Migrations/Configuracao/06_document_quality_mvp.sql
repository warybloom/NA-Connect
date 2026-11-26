/* Extensão mínima das tabelas existentes para Auditoria de Qualidade de Documentos. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('config.PacoteOcrCoordenada', 'Pagina') IS NULL
    ALTER TABLE config.PacoteOcrCoordenada ADD Pagina INT NOT NULL CONSTRAINT DF_PacoteOcrCoordenada_Pagina DEFAULT (1);

IF COL_LENGTH('config.PacoteOcrCoordenada', 'ReferenceWidth') IS NULL
    ALTER TABLE config.PacoteOcrCoordenada ADD ReferenceWidth DECIMAL(18,6) NOT NULL CONSTRAINT DF_PacoteOcrCoordenada_ReferenceWidth DEFAULT (1);

IF COL_LENGTH('config.PacoteOcrCoordenada', 'ReferenceHeight') IS NULL
    ALTER TABLE config.PacoteOcrCoordenada ADD ReferenceHeight DECIMAL(18,6) NOT NULL CONSTRAINT DF_PacoteOcrCoordenada_ReferenceHeight DEFAULT (1);

IF COL_LENGTH('config.ProjetoConfiguracao', 'Folder_Id_Auditoria') IS NULL
    ALTER TABLE config.ProjetoConfiguracao ADD Folder_Id_Auditoria NVARCHAR(255) NULL;

IF COL_LENGTH('config.ProjetoConfiguracao', 'Folder_Path_Auditoria') IS NULL
    ALTER TABLE config.ProjetoConfiguracao ADD Folder_Path_Auditoria NVARCHAR(1000) NULL;

COMMIT TRANSACTION;
