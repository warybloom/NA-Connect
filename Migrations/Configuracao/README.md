# Migrations — Configuração: Normas / Templates / Pacotes OCR / Presets LDP

**NÃO EXECUTADO NESTA SESSÃO.** Estes scripts foram propostos a partir da auditoria em
`AUDITORIA_FORGESYNC_NA/16-CONFIGURACAO-NORMAS-TEMPLATES-OCR-PRESETS-AUDITORIA.md` — leia esse documento
antes de decidir executar qualquer coisa aqui. Ele documenta, com rastreamento real de código (não
suposição), por que `dbo.Projetos`/`dbo.Metadados_Projetos` não servem para estes conceitos, e por que
cada tabela nova foi desenhada como está.

Nenhum destes scripts foi executado contra `DB_NA_CONNECT`. Nenhuma tabela/coluna existente em `dbo` é
alterada — todas as tabelas propostas são NOVAS, no schema novo `[config]`.

## Ordem de execução manual (se e quando o usuário decidir aplicar)

1. `00_criar_schema_config.sql` — cria o schema `[config]`. Pré-requisito de todos os demais.
2. `01_criar_config_norma.sql` — `config.Norma` + `config.NormaMapeamento` (regra sigla→pasta→tipo,
   1:N). Distinto de `dbo.Projetos.Naming_Standard` (enum operacional, não tocado).
3. `02_criar_config_template.sql` — `config.Template`, N:1 opcional com `config.Norma`. Guarda só
   metadados; local de armazenamento do arquivo físico do template ainda não decidido (ver nota no
   próprio script e §6 da auditoria).
4. `03_criar_config_pacote_ocr.sql` — `config.PacoteOcr` + `config.PacoteOcrCoordenada` (calibração por
   folha/campo, 1:N). Independente de projeto.
5. `04_criar_config_preset_ldp.sql` — **⚠️ SUPERSEDIDA (checkpoint 2026-09-18) — NÃO EXECUTAR.** Ver
   `AUDITORIA_FORGESYNC_NA/17-CONFIGURACAO-LDP-NOVO-MOTOR-ARQUITETURA.md` §4: o conceito evoluiu para
   `Migrations/ConfiguracaoLdp/01_criar_ldp_template.sql` (`ldp.Template`/`TemplateAba`/`TemplateCampo`),
   estruturalmente mais amplo (múltiplas abas, campos dinâmicos) do que esta tabela cabe representar.
   Mantida no repositório só como rastro histórico.
6. `05_criar_config_projeto_configuracao.sql` — `config.ProjetoConfiguracao`, vínculo 1:1 com
   `dbo.Projetos` (via `Project_ID_ACC`, que já tem `UNIQUE` — `UQ_Project_ID_ACC` — confirmado por
   leitura direta do banco em 18/09/2026, nenhuma alteração precisa ser feita em `dbo.Projetos`).
   Depende de `01` e `03` já terem sido executados (`PresetLdpId` removido deste desenho — ver nota no
   próprio script).

## Decisão de modelagem: vínculo separado em vez de FKs direto em `dbo.Projetos`

Avaliadas duas opções (detalhe completo na auditoria, §8):

- **Opção A** — adicionar `NormaId`/`PacoteOcrId`/`PresetLdpId` nullable direto em `dbo.Projetos`.
- **Opção B (escolhida)** — tabela separada `config.ProjetoConfiguracao`, 1:1 com `dbo.Projetos`.

Escolhida a Opção B porque nenhuma dessas 3 associações é lida pelo Worker/robô — são consumidas só pela
UI de Configuração — e `dbo.Projetos` deve continuar sendo a tabela operacional simples que o Worker já
lê a cada ciclo, sem crescer para cada conceito novo e opcional da tela de administração.

## O que NÃO foi criado (e por quê)

- **Nenhuma tabela de Plant 3D.** A própria UI da aba (`PlantMappingPanel.tsx`) ainda não tem selects
  reais de mapeamento — o domínio (BOM, regras de tag, colunas P&ID) é grande demais para entrar nesta
  rodada. Marcado como FASE POSTERIOR na auditoria, §7 do pedido original.
- **Nenhuma coluna nova em `dbo.Projetos` ou `dbo.Metadados_Projetos`.** Confirmado por rastreamento de
  código que `dbo.Projetos` já é a única autoridade operacional para os 3 conceitos que pareciam se
  sobrepor (`File_ID_LDP_ACC`/`Naming_Standard`/`Project_Language`); `dbo.Metadados_Projetos` é um
  espelho write-only sem consumidor — não precisa e não deve receber nada novo desta fase.
- **Nenhuma normalização do `Mapping_Json` de `config.PresetLdp` em tabela filha.** O conjunto de campos
  é fixo e pequeno (6 campos conhecidos), sem identidade própria por linha — ao contrário de
  `config.NormaMapeamento`, que é genuinamente 1:N variável.
- **Nenhum backend/frontend novo.** Só a estrutura de banco proposta — implementação de endpoints e UI
  fica para uma fase posterior, fora desta auditoria.

## Checkpoint de revisão (18/09/2026) — correções ao modelo físico antes da aplicação manual

Revisão pedida pelo usuário sobre a primeira versão destes scripts. Nenhum SQL foi executado em nenhuma
das duas rodadas. Resumo (detalhe completo, com citação de código, no documento 16 §12):

- **Idempotência/atomicidade** (`01`, `03`): cada tabela agora é checada individualmente, e toda a
  criação de cada arquivo roda dentro de `BEGIN TRY/TRANSACTION ... COMMIT / CATCH ... ROLLBACK` — uma
  falha no meio do script nunca deixa uma tabela criada sem a outra.
- **OCR `Campo` x `CampoDestino`** (`03`): reauditados OCRCalibrator.tsx e OCRCalibratorPro.tsx linha a
  linha — as duas chaves sempre recebem o MESMO valor; não são conceitos distintos. Coluna canônica:
  `CampoDestino`. `Campo` não foi criada (era duplicação do componente mais antigo).
- **`config.PresetLdp` JSON/Header_Row** (`04`): adicionado `CHECK ISJSON(Mapping_Json)=1` (mesmo padrão
  já usado em `dbo.Cronograma_Projetos.CK_Cronograma_Projetos_CustomFields_JSON`) e
  `CHECK Header_Row > 0` (contrato real do frontend: sempre inteiro >= 1).
- **`FK_Template_Norma`** (`02`): `SET NULL` → `NO ACTION`. A UI nunca avisa sobre templates associados
  ao excluir uma Norma; `SET NULL` os tornaria "globais" silenciosamente.
- **`config.NormaMapeamento`** (`01`): adicionado `UNIQUE (NormaId, Sigla)` — o import de Excel não
  deduplica por sigla e a tabela é semanticamente uma tabela de roteamento 1 sigla → 1 regra; nenhuma
  evidência de necessidade de múltiplas regras por sigla foi encontrada.
- **`Status_Ativo` → `Ldp_Config_Ativa`** (`05`): rastreado até LDPConfigPage.tsx — é a opção B do
  pedido de revisão ("configuração LDP ativa/inativa"), não "projeto ativo" (`dbo.Projetos.Is_Ativo`
  continua sendo a única autoridade para isso). Renomeado para nunca confundir os dois; hoje é um campo
  vestigial (sempre `true`, nunca lido de volta).
- **`FK_ProjetoConfiguracao_Norma/PacoteOcr`** (`05`): `SET NULL` → `NO ACTION`, pela mesma razão do
  `FK_Template_Norma` — excluir uma entidade reutilizável ainda vinculada a um projeto exige desvincular
  primeiro, nunca silenciosamente.
- **Contrato de `Data_Atualizacao`** (`05`): sem trigger. Documentado que o backend futuro é responsável
  por atualizar essa coluna explicitamente em todo UPDATE/PATCH.

## Checkpoint 2 (18/09/2026) — `config.PresetLdp` supersedida pelo Novo Motor de LDP

Ver `AUDITORIA_FORGESYNC_NA/17-CONFIGURACAO-LDP-NOVO-MOTOR-ARQUITETURA.md`. `04_criar_config_preset_ldp.sql`
marcada como não-executável; `05_criar_config_projeto_configuracao.sql` teve a coluna/FK `PresetLdpId`
removida (nunca executada, sem custo de correção). O vínculo Projeto→Template LDP do novo motor, quando
autorizado, é proposto direto em `dbo.Projetos.LdpTemplateId`, não em `config.ProjetoConfiguracao` — ver
documento 17 §6.

## Rollback

Cada script tem o bloco de rollback comentado no final (nunca executado automaticamente). Rollback geral
na ordem inversa: 05 → 04 → 03 → 02 → 01 → 00 (DROP TABLE/SCHEMA).
