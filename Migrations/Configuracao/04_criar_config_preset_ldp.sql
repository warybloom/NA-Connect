/*
============================================================
⚠️ SUPERSEDIDA (checkpoint 2026-09-18, decisão B) — NÃO EXECUTAR
============================================================
Esta migration NUNCA foi executada e não deve ser executada. O conceito de "Template LDP" evoluiu
(ver AUDITORIA_FORGESYNC_NA/17-CONFIGURACAO-LDP-NOVO-MOTOR-ARQUITETURA.md §4) para algo estruturalmente
mais amplo do que uma tabela cabe representar como está desenhada aqui: múltiplas abas lógicas por
template e campos dinâmicos (não um conjunto fixo de ~6 campos num JSON). "Evoluir" esta tabela
significaria recriar sua forma inteira — por isso a decisão foi criar um modelo novo
(Migrations/ConfiguracaoLdp/01_criar_ldp_template.sql: ldp.Template/TemplateAba/TemplateCampo) em vez de
seguir adiante com esta. Como nunca foi aplicada, não há dado a migrar nem tabela real a remover —
apenas não aplicar este arquivo. Mantido no repositório só como rastro histórico da decisão.
============================================================
OBJETIVO ORIGINAL (não mais válido — ver aviso acima)
============================================================
Criar config.PresetLdp — preset nomeado e reutilizável de como ler um workbook LDP (aba, linha de
cabeçalho, mapeamento de colunas). Fonte: LDPConfigPage.tsx/LDPConfigModal.tsx, onde "Template LDP" é
selecionado de uma lista e aplicado a um projeto (não é 1:1 com projeto — por isso fica separado de
config.ProjetoConfiguracao, que guarda o VÍNCULO projeto→preset e as pastas ACC específicas do projeto).
Mapping_Json guarda o conjunto fixo e pequeno de campos observado no contrato real (Document_Code,
Document_Title, Discipline, Revision, Planned_Date, Current_Status) como JSON — não foi normalizado em
tabela filha porque não tem identidade/ciclo de vida próprio por linha (é config-de-parsing, não dado
transacional), ao contrário de config.NormaMapeamento (que é genuinamente 1:N com N variável de siglas).

CHECK ISJSON — reaudidato em 18/09/2026: confirmado padrão já existente no próprio banco
(dbo.Cronograma_Projetos.CK_Cronograma_Projetos_CustomFields_JSON, lido via sys.check_constraints).
Reaplicado aqui com o mesmo formato (`IS NULL OR ISJSON(...)=1`) para manter consistência.

CHECK Header_Row > 0 — reaudidato: LDPConfigPage.tsx declara `const [headerRow, setHeaderRow] =
useState(1)` (padrão 1, sem `min` no `<input type="number">`, mas o conceito é "número da linha do
cabeçalho na planilha", que não tem sentido em 0 ou negativo). Contrato real do frontend sempre envia um
inteiro >= 1 em uso normal; CHECK adicionado para impedir dado inválido, sem contradizer nenhum caminho
observado no código.

============================================================
DEPENDÊNCIAS
============================================================
00_criar_schema_config.sql.

============================================================
PRÉ-CHECK + CREATE (mesmo lote — nunca depender de RETURN atravessar um GO)
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'config')
BEGIN
    RAISERROR('Schema [config] não existe — execute 00_criar_schema_config.sql primeiro.', 16, 1);
END
ELSE IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID('config') AND name = 'PresetLdp')
BEGIN
    PRINT 'Tabela [config].PresetLdp já existe — nada a fazer. Migration idempotente.';
END
ELSE
BEGIN
    CREATE TABLE config.PresetLdp
    (
        PresetLdpId          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Nome_Config           NVARCHAR(200)  NOT NULL,
        Sheet_Name             NVARCHAR(200)  NULL,
        Header_Row             INT            NULL,
        Mapping_Json           NVARCHAR(MAX)  NULL,   -- JSON simples {Document_Code, Document_Title, Discipline, Revision, Planned_Date, Current_Status}
        Data_Criacao           DATETIME2      NOT NULL CONSTRAINT DF_PresetLdp_Data_Criacao DEFAULT SYSUTCDATETIME(),

        CONSTRAINT UQ_PresetLdp_Nome UNIQUE (Nome_Config),
        CONSTRAINT CK_PresetLdp_MappingJson_JSON CHECK (Mapping_Json IS NULL OR ISJSON(Mapping_Json) = 1),
        CONSTRAINT CK_PresetLdp_HeaderRow_Positivo CHECK (Header_Row IS NULL OR Header_Row > 0)
    );

    PRINT 'Tabela [config].PresetLdp criada.';
END
GO

/*
============================================================
VALIDAÇÃO
============================================================
SELECT COUNT(*) FROM config.PresetLdp; -- deve devolver 0 logo após a criação.
SELECT * FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID('config.PresetLdp');

============================================================
ROLLBACK (NUNCA executar automaticamente)
============================================================
-- DROP TABLE config.PresetLdp;
*/
