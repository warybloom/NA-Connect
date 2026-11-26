# Migrations — Novo Motor de LDP: Template / Mapeamento de Cronograma / Espelho / Controle

**NÃO EXECUTADO NESTA SESSÃO.** Estes scripts foram propostos a partir da arquitetura em
`AUDITORIA_FORGESYNC_NA/17-CONFIGURACAO-LDP-NOVO-MOTOR-ARQUITETURA.md` (rodada 1) e sua revisão na
rodada 2 (mesmo documento, seção adicionada) — leia antes de decidir executar qualquer coisa aqui.

Nenhum destes scripts foi executado contra `DB_NA_CONNECT`. Nenhuma tabela/coluna existente em `dbo`
ou `config` é alterada — todas as tabelas propostas são NOVAS, no schema novo `[ldp]`. O Worker,
Tipo_LDP 1/2, `dbo.Ldp_Unificada`, `dbo.Cronograma_Projetos`, Timesheet, PMO e Custo não são tocados.

## Ordem de execução manual (se e quando o usuário decidir aplicar)

1. `00_criar_schema_ldp.sql` — cria o schema `[ldp]`. Pré-requisito de todos os demais.
2. `01_criar_ldp_template.sql` — `ldp.Template` + `ldp.TemplateAba` + `ldp.TemplateCampo`. Um campo
   dinâmico é sempre uma LINHA em `TemplateCampo`, nunca uma coluna física nova em nenhuma tabela.
3. `02_criar_ldp_cronograma_mapeamento.sql` — `ldp.CronogramaMapeamento` +
   `ldp.CronogramaMapeamentoCampo`. Persiste De/Para (nomes de coluna), nunca dados reais do cronograma.
   Depende de `01`.
4. `03_criar_ldp_excel_espelho.sql` — `ldp.ExcelEspelho` + `ldp.ExcelEspelhoAba` +
   `ldp.ExcelEspelhoCampo` (renomeado de `ExcelModelo`/… na rodada 2 — nunca executado, renomear não
   migra dado nenhum). Geração PRESERVA um XLSX base (logo/carimbo/estilos). `Arquivo_Referencia` é um
   ponteiro — local físico definitivo ainda PENDENTE (ver documento 17 §9/§20; armazenamento local
   temporário em uso nesta fase só para viabilizar teste real). `Coluna_Inicial` (novo) permite pular
   colunas decorativas do XLSX base sem expor notação A1. Depende de `01`.
5. `04_criar_ldp_excel_controle.sql` — `ldp.ExcelControle` + `ldp.ExcelControleAba` +
   `ldp.ExcelControleCampo` (NOVO na rodada 2). Geração de uma planilha LIMPA (sem XLSX base) —
   por isso sem `Arquivo_Referencia`/`Linha_Cabecalho`/`Coluna_Inicial`. Permite renomear cabeçalhos de
   saída (`Nome_Exibido_Override`) sem alterar o Template. Depende de `01`.
6. `05_criar_ldp_projeto_configuracao.sql` — `ldp.ProjetoConfiguracao` (vínculo de um projeto REAL,
   por `Project_ID_ACC`, a Template/Mapeamento/Espelho/Controle + Estrutura do Projeto) +
   `ldp.ProjetoFase` + `ldp.ProjetoFaseRegra` (NOVO na rodada 3 — "Projeto Único x Projeto por Fase").
   `ModoEstrutura`/`ModoGeracaoFase` são conceitos NOVOS e INDEPENDENTES de `dbo.Projetos.Tipo_LDP` —
   nunca inferir um a partir do outro. Colunas `*IdOverride` em `ProjetoFase` são nullable e não usadas
   por nenhuma UI ainda — preparadas para override futuro sem exigir novo ALTER TABLE. Depende de
   `01`, `02`, `03`, `04`.

## `config.PresetLdp` — supersedida (decisão B, rodada 1)

`Migrations/Configuracao/04_criar_config_preset_ldp.sql` modelava uma única aba + conjunto fixo de ~6
campos num JSON — estruturalmente incompatível com o conceito novo (N abas × campos dinâmicos). Como
nunca foi executada, a decisão foi simplesmente não aplicá-la — este schema `[ldp]` a substitui por
completo. `Migrations/Configuracao/05_criar_config_projeto_configuracao.sql` teve a FK `PresetLdpId`
removida pela mesma razão. Ver documento 17 §4/§5.

## O que NÃO foi criado (e por quê)

- **Nenhuma tabela de dados** (`ldp.Documento`, `ldp.DocumentoValor` ou equivalente). Esta fase é só
  estrutura (Template), mapeamento (De/Para), Espelho e Controle — nunca valores reais de documento
  persistidos permanentemente. Ver documento 17 §12.
- **Nenhuma coluna `VARBINARY`/BLOB** para o `.xlsx` base do Espelho. `ExcelEspelho.Arquivo_Referencia`
  é `NVARCHAR(500)` — um ponteiro. Onde o arquivo físico realmente fica em definitivo (ACC, storage do
  app, blob storage) continua uma decisão de infraestrutura PENDENTE (documento 17 §9/§20) — o
  armazenamento local sob `appsettings.json:LdpConfig:ArmazenamentoBasePath` usado nesta rodada é só
  para viabilizar o botão "Gerar Teste" funcionar de verdade, não a solução definitiva.
- **Nenhuma coluna de letra de coluna Excel** (`A`/`B`/`C`) em `ldp.ExcelEspelhoCampo`/
  `ExcelControleCampo` — só `Ordem` (int) e, no Espelho, `Coluna_Inicial` (int simples, não notação
  A1); a posição física é calculada em tempo de geração a partir da ordem.
- **Nenhuma alteração em `dbo.Projetos`.** A proposta original da rodada 1 (`LdpTemplateId INT NULL`
  direto em `dbo.Projetos`) foi SUPERADA pela rodada 3: o vínculo projeto→configuração agora vive
  inteiramente em `ldp.ProjetoConfiguracao` (chave `Project_ID_ACC`, sem tocar `dbo.Projetos`) — mais
  isolado do legado do que a proposta anterior. `dbo.Projetos.Tipo_LDP` continua intocado.
- **Nenhuma tabela de vínculo Documento↔Fase.** Não existe ainda tabela de dados/documento no motor
  novo para referenciar — a classificação por fase é calculada dinamicamente (ver
  `ClientApp/src/pages/ldp/faseRuleEngine.ts`) a partir do dataset de preview, nunca persistida por
  linha. O modelo não impede uma futura tabela de junção N:N Documento↔Fase.
- **Nenhuma tabela de domínio para `Tipo_Dado`/`Origem`.** São listas pequenas e estáveis, modeladas
  como `CHECK` (mesmo padrão já usado para `Naming_Standard`/`Tipo_LDP`) — não uma tabela CRUD separada.
- **Nenhuma coluna de origem de domínio real em `ldp.ExcelControleCampo`.** A arquitetura já separa
  Controle de Espelho para permitir essa conexão numa fase futura (Custo/HH/Weekly), mas a coluna não
  foi criada agora — sem necessidade comprovada ainda (nenhuma leitura real desses domínios existe
  nesta fase).

## Restrição técnica: múltiplos caminhos de CASCADE

`ldp.CronogramaMapeamentoCampo`, `ldp.ExcelEspelhoAba`/`ExcelEspelhoCampo` e
`ldp.ExcelControleAba`/`ExcelControleCampo` referenciam, cada uma, DUAS tabelas que remontam ao mesmo
`ldp.Template` por rotas diferentes. O SQL Server rejeita `ON DELETE CASCADE` em ambos os caminhos
quando eles convergem na mesma tabela ancestral. Por isso a FK que aponta para `TemplateCampo`/
`TemplateAba` usa `NO ACTION` — excluir um campo/aba do Template enquanto uma linha de mapeamento/
Espelho/Controle ainda o referencia falha explicitamente, em vez de apagar essa linha silenciosamente.

## Persistência real (rodada 2) — backend pronto, aguardando migration

O backend (`Services/Ldp/LdpConfigDatabaseService.cs`, `Controllers/Ldp/*`) já foi escrito contra este
schema e compila, mas **só funciona depois que estas migrations forem aplicadas manualmente** — até
lá, qualquer chamada real retorna erro explícito do SQL Server ("Invalid object name 'ldp.Template'"),
nunca um sucesso falso. Ver documento 17 (rodada 2) para o design "substituir a árvore inteira"
(replace-on-save) adotado para Template/Mapeamento/Espelho/Controle, mais simples que CRUD granular
para dados deste porte.

## Rollback

Cada script tem o bloco de rollback comentado no final (nunca executado automaticamente). Rollback
geral na ordem inversa: 05 → 04 → 03 → 02 → 01 → 00 (DROP TABLE/SCHEMA).
