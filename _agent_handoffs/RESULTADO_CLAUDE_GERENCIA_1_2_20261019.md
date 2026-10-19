# RESULTADO — CLAUDE — GERÊNCIA DA ENGENHARIA, ITENS 1 E 2 (+ integração do item 3 do Codex) — 19/10/2026

## 0. Situação em uma frase
O código dos itens 1 e 2 está pronto, compilado e testado, e o item 3 do Codex já está montado no container. **Nada disso
está aplicado no checkout principal ainda.** Também nada foi aplicado no banco, publicado ou reiniciado.

### Por que ainda não está no checkout principal
Esta sessão rodou em background, e o guard do Claude Code **bloqueia edições no checkout compartilhado**
("This background session hasn't isolated its changes yet"). Por isso todo o trabalho foi feito no worktree
`.claude/worktrees/gerencia-eng-1-2-20261019` (branch `worktree-gerencia-eng-1-2-20261019`), que espelha o estado atual
do checkout principal, inclusive as alterações não commitadas. A entrega é um **patch verificado**:
- `_agent_handoffs/CLAUDE_GERENCIA_1_2_20261019.patch` (dentro do worktree): 35 arquivos, sendo 10 alterados e 25 novos;
- base do patch = backup `C:\Dev\N-A-Connect aa\_backup_claude_gerencia_1_2_20261019\` (SHA256SUMS);
- antes de gerar o patch conferi que os 10 arquivos alterados no checkout principal ainda são **idênticos** ao backup
  (ninguém mexeu neles depois) e que nenhum dos 25 arquivos novos existe lá;
- o `git apply --check` passou numa cópia da base, e depois de aplicado o resultado é idêntico ao worktree.

**Para aplicar (no checkout principal, PowerShell):**
```powershell
cd "C:\Dev\N-A-Connect aa\N-A-Connect"
$p = ".claude\worktrees\gerencia-eng-1-2-20261019\_agent_handoffs\CLAUDE_GERENCIA_1_2_20261019.patch"
git apply --check $p; if ($?) { git apply $p }
Copy-Item ".claude\worktrees\gerencia-eng-1-2-20261019\_agent_handoffs\RESULTADO_CLAUDE_GERENCIA_1_2_20261019.md" _agent_handoffs\
graphify update .
```
Se o `--check` falhar, alguém editou um dos 10 arquivos depois das 09:40. Nesse caso, peça ao Claude para refazer o
merge, em vez de forçar o patch.

---

## 1. ITEM 1 — Multiprojeto separado por PROJETO → FASE → disciplina
**O que foi feito.** Com mais de um projeto selecionado, a Gerência da Engenharia passa a abrir por padrão em
**"POR PROJETO E FASE"**. Um seletor no cabeçalho (`radiogroup` acessível) troca para **"CONSOLIDADO POR DISCIPLINA"**,
que são as telas originais somadas e agora normalizadas pelo item 2. Com um projeto só, as telas continuam exatamente
como antes (nenhum seletor e nenhuma mudança).

Componente isolado `projetoFase/ProjetoFaseAgrupamento.tsx`, com CSS próprio `.pf-*`:
- tabela hierárquica. A linha do projeto mostra código, nome e LDP Tipo, e é recolhível. Abaixo vêm a linha da fase
  (recolhível) e as linhas de disciplina. Cada linha repete o código do projeto e a fase, então nada fica ambíguo;
- os botões "Expandir tudo" / "Recolher tudo" e o checkbox "Consolidado" acrescentam uma linha com a soma dos totais
  dos projetos. Isso é seguro porque os projetos são disjuntos, e os percentuais são recalculados pela razão, nunca
  pela média;
- a primeira coluna e o cabeçalho ficam fixos (sticky). A rolagem horizontal fica restrita à tabela, e abaixo de
  760 px a coluna encolhe e o nome do projeto quebra em linhas. Usei `aria-expanded`, `scope`, `role=region` e
  `tabIndex`, e o foco fica visível.

**Fase = somente campos REAIS:**

| Tela | Fonte da fase | Sem o campo → "Sem fase definida" |
|---|---|---|
| 01 Visão geral (entregas da semana) | gate ativo Tipo 2 (`gates[].percentual` → "FASE 30%") | Tipo 1 |
| 02 Programação | `fase` da linha = coluna `Weekly_Phase`, texto como veio da fonte | linha sem fase |
| 03 Documentação | gate ativo, com a situação DO GATE (emitido/cancelado/Document_Status) | Tipo 1 e documento sem gate ativo |
| 04 Indicadores | HH planejado por gate = `esforcoHoras` (a mesma fonte de "HH PREVISTO POR FASE") | documentos sem gate; **todo o realizado** (o Timesheet não tem gate) |

Nenhuma fase é inferida pelo nome. Cada projeto usa **o próprio catálogo** de disciplinas (rótulo original da Norma do
projeto, sem normalização e sem sufixo). Assim nenhum código, entregável ou HH de um projeto é somado a outro.

**Totais sem dupla contagem:**
- Documentação: um documento Tipo 2 com vários gates aparece em cada fase, mas o total do projeto conta **cada
  documento uma vez**, como na tela 03. Uma nota explica isso;
- Indicadores: o total do projeto vem dos **cards oficiais** (`projectFacts`). O que não cabe numa fase (custo
  planejado de Tipo 2, que não existe por gate, e a diferença entre o esforço dos gates e o HH do documento) aparece
  numa linha explícita **"NÃO ATRIBUÍDO A FASE"**. Nada é rateado;
- Programação: a fase é a folha. Ao abrir uma fase, aparece a lista de atividades com PROJETO, FASE, DIA, DOCUMENTO,
  TAREFA, DISCIPLINA, responsáveis, SITUAÇÃO e HH.

**Mudança no container** (`GestaoVistaJamilePage.tsx`): imports, estado da visão, carga das regras do normalizador
(só em multiprojeto), um ramo `porProjetoFase` que monta o componente, uma nota de normalização no consolidado e o
seletor no cabeçalho. Os filtros, a data de corte, a navegação de semana e dia, `ConsolidacaoIncompleta`,
`DiagnosticoNorma` e o modo de 1 projeto ficaram intactos.

## 2. ITEM 2 — Configurações → "Normalizador de Siglas"
**Tela** `/configuracoes/normalizador-siglas`. Fica no sidebar em Configurações, ícone Languages, e o catálogo foi
regenerado nos dois lados. Tem três abas:
- **Regras**: pesquisa (sem distinção de caixa e espaços, com acentos), filtro de ativas/inativas, adicionar,
  corrigir, ativar/desativar (checkbox) e excluir, esta com confirmação (AlertDialog). Também mostra os nomes
  canônicos que somam várias siglas e se avisa quando falta permissão (somente leitura);
- **Importar Excel**: o upload aceita .xlsx de até 5 MB e até 5000 linhas. O servidor devolve os **cabeçalhos reais**
  e a aba, e você faz o **de/para** de cada papel (sigla*, disciplina canônica*, tipo de contexto, contexto, ativo).
  A sugestão automática só acontece quando o nome do cabeçalho é exatamente igual, e você sempre confirma. Em seguida
  vêm a amostra das primeiras linhas e a **prévia linha a linha** (Incluir / Alterar / Inalterada / Rejeitada /
  Duplicada, com o motivo e filtro por resultado). Por fim, a confirmação, que exige o mesmo arquivo (hash SHA-256)
  e é replanejada no servidor dentro de uma transação, e o relatório final;
- **Histórico** das importações (arquivo, hash, contagens, quando e quem).

**Permissões:** só keys que **já existem**. A visualização usa `settings.projects.view` e a edição e importação usam
`settings.projects.edit`. O endpoint `/ativas`, usado pelo painel, também aceita as keys da Gestão à Vista.
Nenhuma key, perfil, usuário ou role foi criado ou alterado. A única linha nova em `permissionLogic.ts` é o
mapeamento da rota nova para a key existente, porque sem ela a rota seria bloqueada por padrão.

**Modelo da regra:** sigla original, disciplina canônica PT-BR (Unicode preservado), contexto opcional
(PROJETO: código ou ID ACC; FONTE: LDP | TIMESHEET | PROGRAMACAO), ativo, data e autor (criado/alterado), origem
(MANUAL/IMPORTACAO + id da importação) e versão (concorrência otimista, conflito → 409).
- **Comparação:** Unicode NFC, espaços colapsados, sem distinção de caixa. **Os acentos NÃO são removidos**, para
  nunca unir nomes por semelhança;
- **Resolução:** primeiro a regra do contexto (projeto/fonte), depois a global, depois o rótulo original. Se houver
  duas regras no mesmo nível com nomes diferentes, é um **conflito**: o item fica com o rótulo original e o conflito
  aparece como diagnóstico. O banco impede duplicidade com `UNIQUE (SiglaChave, ContextoChave)`;
- **Função única:** `SiglaNormalizer` (C#) e `normalizadorSiglas.ts` (TS) rodam **o mesmo fixture**
  (`Tests/NA_Connect.DisciplineAlias.Tests/Fixtures/casos_resolucao.json`, com 11 casos) e dão o mesmo resultado
  dos dois lados.

**Onde a normalização é aplicada:** **somente** no catálogo consolidado da Gerência da Engenharia com mais de um
projeto, por um gancho opcional `criarCatalogo(normas, selection, normalizador)` em `gestaoVistaJamileLogic.ts`.
Cada item cai em UMA chave (`S:<nome canônico>`), e o HH, as entregas e os documentos são somados por nome, sem dupla
contagem (há um teste para isso). Com um projeto, na visão por projeto e fase e em todas as outras telas, a
classificação original continua igual. Nenhum dado de origem é renomeado: LDP, Timesheet, Norma, Plant/Revit e
cadastros ficam como estão.

**Sem a migração aplicada**, a API responde 503 com `instalado=false` e a tela mostra a instrução. O painel continua
igual a hoje (sem normalizar) e diz isso numa nota.

### Importação futura (quando o Excel chegar)
1. Abrir Configurações → Normalizador de Siglas → Importar Excel e escolher o arquivo.
2. Conferir os cabeçalhos REAIS que aparecem e mapear cada um. Se os nomes não baterem exatamente com
   "Sigla" / "Disciplina canônica" etc., **nada é sugerido**: o mapeamento é manual.
3. Gerar a prévia. As linhas rejeitadas trazem o motivo (sigla vazia, contexto inválido, ativo não reconhecido,
   conflito no arquivo etc.).
4. Confirmar. Reimportar o mesmo arquivo resulta em tudo "Inalterada". Linhas que não estão no arquivo **nunca**
   apagam regras. Para mudar a disciplina de uma regra que já existe, é preciso marcar "aplicar alterações".
5. Nenhuma sigla foi cadastrada nem inventada: a migração não tem seed, e os testes usam apenas siglas sintéticas
   (prefixo X/T, "Teste").

## 3. Migração (NÃO aplicada) e rollback
- `Migrations/Normalizador/20261019_001_NormalizadorSiglas_UP.sql`: aditiva e idempotente. Cria só o schema
  `normalizador` com `Importacao`, `SiglaDisciplina` e `SiglaDisciplinaAuditoria` (antes e depois em JSON; o
  histórico sobrevive à exclusão). Não tem DROP, ALTER, UPDATE nem INSERT, e não toca em nenhuma tabela legada.
- `..._DOWN.sql`: remove só esses objetos e **recusa** rodar se houver dados, até que `@ConfirmoExportacao = 1`.
- `Migrations/Normalizador/README.md`: backup `COPY_ONLY, CHECKSUM` + `RESTORE VERIFYONLY`, teste num clone
  (UP → UP → DOWN → UP) e depois a aplicação.
- Verificado: os dois scripts passam em `SET PARSEONLY ON` no SQLEXPRESS (só análise, sem execução).
  `SCHEMA_ID('normalizador')` = ausente no DB_NA_CONNECT, ou seja, nada foi criado.
- Rollback do código: o backup acima. O painel volta a não normalizar e a misturar projetos como antes.

## 4. Integração do item 3 (Codex) — FEITA no worktree
O Codex sinalizou a conclusão às 10:19 (`INTEGRACAO_CODEX_ITEM3.md`). Os arquivos dele foram **copiados sem
alteração** para o worktree (`item3/*`, o teste e `verify-gerencia-item3.mjs`). Eles já existem no checkout
principal e, por isso, não estão no patch. Apliquei o patch dele no `Programming`:
- `tituloDocumento` via `criarIndiceTitulosDocumentos` / `obterTituloDocumentoOficial` (LDP `Document_Title`,
  projeto + código; a tarefa nunca é usada como título);
- os KPIs ficaram numa linha acima, e logo abaixo entra `<GerenciaEngenhariaItem3>` (Semana Completa no topo e
  Entregáveis × HH por disciplina embaixo);
- a mensagem de vazio, com os botões de semana anterior e seguinte, o aviso de fallback e o rodapé de
  fonte/cobertura foram transplantados sem perda. Saíram a tabela lateral antiga e os gráficos da coluna esquerda,
  além de `hhMax` e `porDia`.
- O contrato antigo em `gestaoVistaJamile.test.ts`, que exigia a lista de colunas dentro do container, agora
  verifica a montagem de `<GerenciaEngenhariaItem3` e a lista de colunas (com TÍTULO DO DOCUMENTO) dentro do
  componente.
- No modo multiprojeto "POR PROJETO E FASE", a Programação usa o agrupamento do item 1, onde a folha lista as
  atividades por projeto e fase. O componente do Codex aparece na visão consolidada e com 1 projeto.

## 5. Testes e builds (no worktree, saídas isoladas)
| Verificação | Resultado |
|---|---|
| `dotnet test Tests/NA_Connect.DisciplineAlias.Tests` → `_build_verify/discipline_alias_tests` | **47/47** (normalização, paridade com o fixture, importação/idempotência/conflitos, contratos de SQL só no schema `normalizador`, permissões do controller, 503/409/400/401) |
| `dotnet build AccIntegration.WebApp.csproj` → `_build_verify/gerencia12-backend` | **0 erros** (nenhum aviso nos arquivos novos) |
| `dotnet test Tests/Sidebar.Security.Tests` | **7/7** |
| `tsc --noEmit` (ClientApp) | **0 erros** |
| `node scripts/test-pmo.mjs` | **295/297**. As 2 falhas são **pré-existentes** e também falham no checkout principal sem as minhas mudanças: `physicalProgress.test` "Link de configuração…" e `qualityAuditActions.test` "Chave canônica…". O Codex relatou as mesmas. |
| `node scripts/test-sidebar.cjs` | **22/22**. A contagem fixa de nós passou de 116 para 117 (o novo item). |
| `vite build` → `_build_verify/gerencia12-vite` | **OK** (só o aviso de chunk grande, que já existia) |
| SQL `PARSEONLY` UP/DOWN | **OK** |

Os testes novos de TS são `projetoFase.test.ts` (hierarquia, fases reais, sem dupla contagem, "não atribuído",
programação com dedup e um SSR do componente com `aria-expanded` e códigos de projeto) e
`normalizadorSiglas.test.ts` (11 casos de paridade C#/TS, soma por nome canônico sem dupla contagem, nenhuma
normalização com 1 projeto, conflito, contexto FONTE e a lógica da tela).

## 6. O que NÃO foi verificado e pendências
- **Validação visual com dados reais: NÃO feita.** Subi um Vite do worktree na porta 5181 com proxy para o 5277 em
  execução, mas a sessão redirecionou para a autorização OAuth da Autodesk. Não autorizei nem fiz login, e encerrei
  o servidor. Falta conferir na tela, depois de aplicar o patch: multiprojeto por projeto e fase nas 4 telas, a
  troca para consolidado, 1 projeto, a Programação com o componente do Codex, a aba de Configurações e o tema
  claro/escuro.
- **Backend:** o controller e o serviço novos só existem no processo depois do deploy ou reinício feito por você.
  Até lá a aba mostra "404 — backend em execução sem o Normalizador", e o painel segue sem normalizar.
- **Migração:** aguarda a sua aprovação, seguindo o README (backup, clone, UP).
- **Excel de mapeamento:** ainda não recebido. Quando chegar, siga a seção 2, "Importação futura".
- O `graphify update .` não rodou: o grafo do checkout principal deve ser atualizado depois de aplicar o patch.
- **Pré-existente, fora do escopo:** o `dotnet build` do checkout principal pega
  `_bi_chs001_replica/**/Program.cs` soltos (CS8802, "top-level statements"). No worktree removi só a minha cópia
  para validar. No principal nada foi alterado.
- **Efeito colateral:** o `node_modules` do worktree é um junction para o do checkout principal. Ao subir o Vite na
  5181, ele re-otimizou o cache `node_modules/.vite`. Se o dev server da 5173 recarregar uma vez, a causa é essa;
  não há outra consequência.
- Sidebar com configuração publicada no banco (SidebarOfficial): o item novo entra como página "faltante" pelo
  mecanismo que já existe, com o teste `adds only missing pages` passando.

## 7. Arquivos
**Alterados (10):** `GestaoVistaJamilePage.tsx`, `gestaoVistaJamileLogic.ts` (gancho opcional do normalizador),
`__tests__/gestaoVistaJamile.test.ts` (2 contratos), `App.tsx` (rota), `officialNavigation.ts` (item),
`sidebar/catalog.json` + `Services/Sidebar/SidebarCatalog.Generated.cs` (regenerados pelo script),
`permissionLogic.ts` (1 linha), `scripts/test-sidebar.cjs` (116 para 117), `Program.cs` (2 registros de DI).
**Novos (25):** `Services/DisciplineAlias/*` (5), `Controllers/DisciplineAliasController.cs`,
`Migrations/Normalizador/*` (3), `Tests/NA_Connect.DisciplineAlias.Tests/*` (6),
`ClientApp/src/features/disciplinas/*` (2), `features/settings/normalizador-siglas/*` (3),
`features/production/analytics/projetoFase/*` (3) e `analytics/__tests__/{normalizadorSiglas,projetoFase}.test.ts`.
**Não tocados:** auth, usuários, roles e o cadastro de permissões, BI CHS001, LDP/Timesheet/Norma, LdpTipo2,
CronogramaTipo2, robô, Cloudflare e o backend em execução (5277 seguiu no ar o tempo todo).
