/*
============================================================
OBJETIVO
============================================================
Criar config.ProjetoConfiguracao — vínculo 1:1 por projeto (chave = dbo.Projetos.Project_ID_ACC) às
entidades reutilizáveis criadas nas migrations anteriores (Norma, PacoteOcr) mais as pastas ACC
específicas deste projeto (Folder_Id_Ldp/Folder_Id_Cronograma/Folder_Id_MasterList) e o flag
Ldp_Config_Ativa (ver nota abaixo) observado no payload real de LDPConfigPage.tsx.

⚠️ AJUSTE (checkpoint 2026-09-18, ver AUDITORIA_FORGESYNC_NA/17-*.md §4/§5): a coluna/FK `PresetLdpId`
foi REMOVIDA deste desenho. `config.PresetLdp` foi supersedida pelo novo modelo `ldp.Template` (Novo
Motor de LDP) antes de qualquer uma das duas migrations ser executada — como nada rodou ainda, não há
dado a migrar nem FK morta a arrastar. O vínculo Projeto→Template LDP, quando autorizado, não entra
nesta tabela — ver §6 do documento 17 (proposta de `dbo.Projetos.LdpTemplateId`, ainda não executada).

DECISÃO DE MODELAGEM (Opção B, ver AUDITORIA_FORGESYNC_NA/16-*.md §8): optou-se por uma tabela de
vínculo separada em vez de adicionar 3+ colunas nullable (NormaId/PacoteOcrId/PresetLdpId) direto em
dbo.Projetos (Opção A). Motivo: manter dbo.Projetos simples e sem crescer a cada novo conceito
opcional da tela de Configuração; nenhuma dessas 3 associações é lida pelo Worker/robô hoje (só a UI de
Configuração as usaria), então não há motivo operacional para colocá-las na tabela operacional.

Project_ID_ACC é chave primária E chave estrangeira ao mesmo tempo (relação 1:1 real — um projeto tem no
máximo uma linha de configuração).

RENOMEIO Status_Ativo → Ldp_Config_Ativa — reaudidato em 18/09/2026 (checkpoint de revisão): rastreado o
campo `statusAtivo` até sua ÚNICA ocorrência no código-fonte, LDPConfigPage.tsx linha ~276
(`handleSaveConfig`), dentro do MESMO payload POST que já envia `projetoId` + `folderIdLdp` +
`folderIdCronograma` + `folderIdMasterList` + `sheetName` + `headerRow` + `mappingJSON` — ou seja, é
parte da configuração LDP DESTE projeto, não um espelho de dbo.Projetos.Is_Ativo (conceito totalmente
diferente, tela diferente). Hoje é hardcoded `true` em toda chamada, nunca lido de volta em nenhum lugar
do código, e LDPConfigModal.tsx (o outro caminho de salvar, payload nº2 dos 3 identificados na
auditoria) nem chega a enviar esse campo. Resposta às 4 opções do pedido de revisão: é a opção B
("configuração LDP ativa/inativa"), não a A — por isso NÃO foi removida, mas RENOMEADA para
`Ldp_Config_Ativa` para nunca ser confundida com `dbo.Projetos.Is_Ativo`. Mantido como campo vestigial/
preparado para uma futura função de "desativar temporariamente esta configuração sem apagar o vínculo",
hoje sem nenhum consumidor real.

ON DELETE de NormaId/PacoteOcrId — reaudidato: aplicada a MESMA lógica do FK_Template_Norma
(02_criar_config_template.sql): excluir uma Norma/Pacote OCR que ainda está vinculado a um projeto via
SET NULL apagaria silenciosamente essa associação sem o usuário nunca ver um aviso (nenhum dos fluxos de
exclusão na UI verifica ou avisa sobre projetos vinculados). Trocado para NO ACTION nas 2 FKs — a
exclusão de qualquer uma dessas entidades reutilizáveis, enquanto ainda vinculada a algum projeto, falha
explicitamente, obrigando a desvincular primeiro. `Project_ID_ACC` → `dbo.Projetos` continua CASCADE
porque esta linha de configuração não tem nenhum significado independente do projeto (1:1 de
dependência real, não uma associação a uma entidade reutilizável).

============================================================
DEPENDÊNCIAS
============================================================
00_criar_schema_config.sql, 01_criar_config_norma.sql, 03_criar_config_pacote_ocr.sql.
04_criar_config_preset_ldp.sql NÃO é mais dependência (PresetLdpId removido, ver nota acima).
dbo.Projetos.Project_ID_ACC já tem UNIQUE constraint
(UQ_Project_ID_ACC, confirmado por leitura de sys.indexes em 18/09/2026) — nenhuma alteração necessária
em dbo.Projetos para esta FK funcionar.

============================================================
PRÉ-CHECK + CREATE + ÍNDICES (mesmo lote — nunca depender de RETURN atravessar um GO)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'config')
BEGIN
    RAISERROR('Schema [config] não existe — execute 00_criar_schema_config.sql primeiro.', 16, 1);
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'Norma')
BEGIN
    RAISERROR('Tabela [config].Norma não existe — execute 01_criar_config_norma.sql primeiro.', 16, 1);
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'PacoteOcr')
BEGIN
    RAISERROR('Tabela [config].PacoteOcr não existe — execute 03_criar_config_pacote_ocr.sql primeiro.', 16, 1);
END
ELSE IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Projetos') AND name = 'UQ_Project_ID_ACC')
BEGIN
    RAISERROR('dbo.Projetos.Project_ID_ACC não tem UNIQUE constraint (UQ_Project_ID_ACC) — abortando, esta migration NUNCA altera dbo.Projetos.', 16, 1);
END
ELSE IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'ProjetoConfiguracao')
BEGIN
    PRINT 'Tabela [config].ProjetoConfiguracao já existe — nada a fazer. Migration idempotente.';
END
ELSE
BEGIN
    CREATE TABLE config.ProjetoConfiguracao
    (
        Project_ID_ACC        NVARCHAR(255)  NOT NULL PRIMARY KEY,
        NormaId                 INT            NULL,
        PacoteOcrId             INT            NULL,
        Folder_Id_Ldp           NVARCHAR(255)  NULL,
        Folder_Id_Cronograma    NVARCHAR(255)  NULL,
        Folder_Id_MasterList    NVARCHAR(255)  NULL,
        Ldp_Config_Ativa        BIT            NOT NULL CONSTRAINT DF_ProjetoConfiguracao_LdpConfigAtiva DEFAULT 1,
        Data_Atualizacao        DATETIME2      NOT NULL CONSTRAINT DF_ProjetoConfiguracao_DataAtualizacao DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_ProjetoConfiguracao_Projeto FOREIGN KEY (Project_ID_ACC)
            REFERENCES dbo.Projetos (Project_ID_ACC) ON DELETE CASCADE,
        CONSTRAINT FK_ProjetoConfiguracao_Norma FOREIGN KEY (NormaId)
            REFERENCES config.Norma (NormaId) ON DELETE NO ACTION,
        CONSTRAINT FK_ProjetoConfiguracao_PacoteOcr FOREIGN KEY (PacoteOcrId)
            REFERENCES config.PacoteOcr (PacoteOcrId) ON DELETE NO ACTION
    );

    CREATE INDEX IX_ProjetoConfiguracao_NormaId ON config.ProjetoConfiguracao (NormaId);
    CREATE INDEX IX_ProjetoConfiguracao_PacoteOcrId ON config.ProjetoConfiguracao (PacoteOcrId);

    PRINT 'Tabela [config].ProjetoConfiguracao e índices criados.';
END
GO

/*
============================================================
VALIDAÇÃO
============================================================
SELECT COUNT(*) FROM config.ProjetoConfiguracao; -- deve devolver 0 logo após a criação.
SELECT * FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('config.ProjetoConfiguracao');

============================================================
CONTRATO DE APLICAÇÃO (não impõe por trigger — decisão deliberada, ver documento 16/README)
============================================================
Data_Atualizacao só tem DEFAULT na inserção. O backend que vier a implementar o PATCH/UPDATE desta
tabela é responsável por atualizar Data_Atualizacao explicitamente em toda escrita (`SET
Data_Atualizacao = SYSUTCDATETIME()` no próprio UPDATE). Nenhum trigger foi criado para isso — não há
necessidade comprovada de impor isso no nível do banco.

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE config.ProjetoConfiguracao;
*/
