# STATUS — CLAUDE — BI CHS001 (Original) — 15/10/2026

Prioridade aplicada: `PRIORIDADE_10_TELAS.md` (Resumo 30/60/90 → LDP Engineering 30/60/90 → LDP 3D 30/60/90 → Programação).
Nada foi gravado no banco, nenhum serviço foi reiniciado, nenhuma tela existente foi alterada. O PBIX foi lido só em CÓPIA
(SHA-256 `6a5d0a83…c6983aaa197` igual ao original).

## ⚠️ Sem screenshot de referência do Power BI

O Power BI Desktop não foi aberto (precisaria de automação de desktop). **Nenhuma página está declarada "idêntica"**.
A fidelidade abaixo é: (a) estrutural — posições/tamanhos/cores/fontes/títulos lidos 1:1 do PBIR; (b) de DADOS — conferida
número a número contra o cache de dados do PBIX. Falta a comparação de pixels com o PBIX renderizado (Codex / usuário).

## Como acessar

Sidebar → **Controle de Produção → BI — CHS001 (Original)** → 23 subtelas com os nomes e a ordem do PBIX.
Rota: `/controle-producao/bi-chs001/<slug>` (ex.: `/controle-producao/bi-chs001/ldp-engineering-30`). Projeto: CHS001
(outro projeto selecionado → aviso, nada é exibido sob os títulos da CHS001). As 13 páginas fora do grupo prioritário aparecem
com o selo "Em desenvolvimento".

**Para os dados aparecerem é preciso reiniciar o backend** (manualmente, pelo usuário): o endpoint novo
`/api/bi-chs001/*` só existe após o reinício. Antes disso, os visuais de dados mostram "Endpoint do BI indisponível".

## Matriz das 10 páginas prioritárias

| # | Página | Layout | Dados | Situação |
|---|--------|--------|-------|----------|
| 3 | Resumo 30% | extraído do PBIR, renderiza | **PENDENTE** | KPIs/curva S/rosca/matriz sem fonte verificada (ver abaixo) |
| 4 | Resumo 60% | idem | **PENDENTE** | idem |
| 5 | Resumo 90% | idem | **PENDENTE** | idem |
| 12 | LDP - Engineering - 30% | ok | **igual ao PBIX** em todos os visuais | só o rótulo "INFRASCTRUTURE" (PBIX) × "INFRASTRUCTURE" (banco já corrigido) |
| 13 | LDP - Engineering - 60% | ok | motor = PBIX; banco mais novo | ex.: Total 361 × 357, CANCELED 81 × 75 (LDP atual tem 4 documentos a mais) |
| 14 | LDP - Engineering - 90% | ok | motor = PBIX; banco mais novo | Total 170 × 166 |
| 8 | LDP - 3D - 30% | ok | cartões/gráficos iguais | ITEM renumerado na LDP atual (396 → 400) |
| 9 | LDP - 3D - 60% | ok | banco mais novo | status 60% dos modelos 3D mudaram (PBIX: 4 em branco; banco: ELABORATION/NOT STARTED) |
| 10 | LDP - 3D - 90% | ok | iguais (exceto ordem de empates) | — |
| 17 | Programação | ok | **igual ao PBIX** (148 linhas, mesmo arquivo `CHS001-000-PM0-RT-001-08.xlsx`) | 108 / 26 / 28 / 5; gráfico e tabela (103 linhas) idênticos |

Combo "curva" das páginas LDP 3D (KPI 30/60/90) → PENDENTE (mesma dependência do Resumo).

Evidências: `paridade_dados_claude_20261015.json` (motor sobre PBIX × banco, visual a visual) e capturas da reprodução em
`capturas_claude_20261015/` (renderizadas pela pré-visualização de desenvolvimento com as linhas atuais do banco).
Cálculo independente em pandas sobre o cache do PBIX bateu com o motor (Eng30 181/172/9; Eng60 357/75/129; Eng90 166;
Programação 26/28/108).

## Por que o Resumo 30/60/90 está PENDENTE (sem atalho)

As medidas `KPI 30|60|90 | …` (DAX completo em `dax_medidas_prioritarias.txt`) usam o cronograma MS Project do PBIX
(`WBX` = tarefas folha com `Level 4 = "Engineering - 30%"` etc., e `AssignmentByDay` = trabalho distribuído por dia).
No banco existe a hierarquia (`VW_Cronograma_Tipo2_Ultima_Versao`, EDT 1.5 "Emgineering - 30%" — grafia diferente) e
`Trabalho`/datas, mas **não existe trabalho real/linha de base por dia**. Sem isso, curva S, `COLUNA | %`, `BASE SEM | %` e
`% Realizado` não podem ser reproduzidos exatamente. Os cartões de PRAZO (datas, desvio, duração, dias restantes) e a
rosca de status são candidatos a mapeamento — exigem conferir a correspondência de tarefas folha (Codex: auditar).
Também ausentes no banco: `KPI.Activity Title` e a seleção de data de corte (`dSemana`).

## Decisões / limitações

- Endpoint NOVO somente leitura `Controllers/BiChs001Controller.cs` (não altera serviços existentes, sem DI nova):
  `GET api/bi-chs001/ldp` (PmoLdpSnapshot = LDP atual, TODAS as linhas, inclusive AT — o BI filtra FORMAT por página — +
  gates 30/60/90 de `VW_Ldp_Documento_Gates_Ultima_Versao`) e `GET api/bi-chs001/programacao` (última importação de
  `tb_WeeklyEngineeringSchedule`). Keys aceitas: `reports.gestao.view` ou as keys já existentes de LDP/Programação.
- Filtros de página/visual e medidas são os do PBIR/DAX, transcritos (`dados/filtrosPbi.ts`, `dados/medidas.ts`):
  ex. `CANCELED 30` compara com "Canceled" e por isso dá (Blank) no 30% (o dado é "CANCELLED") — exatamente como no BI.
- Permissões (keys existentes, nenhuma nova): páginas comuns `reports.gestao.view`; Controle de Hora e Custo (+ Gestão)
  `production.managerial.hh-custo.view`.
- NÃO registrado na TV: o grupo fica fora de `PRODUCTION_NAV` (teste garante).
- Logos CHS e Whitefox → logo oficial N&A 30 anos (versão negativa; ambas ficam sobre fundo escuro). A logo "N&A USA" do
  BI é da N&A e foi PRESERVADA como no original — confirmar se deve virar a logo 30 anos.
- Textos fixos do PBIX preservados (ex.: "WEEKLY SCHEDULE 24/09 - 07/10", data salva nas segmentações de data).
- Gráficos em SVG próprio seguindo o PBIR (cores, rótulos, eixos, legendas); tamanhos de fonte pt→px. Empates de
  ordenação seguem a ordem das linhas da fonte (o PBI usa a ordem interna do modelo).
- Interações (cross-filter por clique em barra) não implementadas; segmentações com fonte mapeada filtram a página.
- Visuais personalizados (Autodesk viewer, advancedSlicer) e as 13 páginas restantes: layout carregado, dados PENDENTES.

## Arquivos

Novos: `ClientApp/src/features/production/biChs001/**` (registro, canvas, visuais, motor, CSS, assets, `generated/` 23
layouts + índice, `__tests__`, `__preview__` só de desenvolvimento); `Controllers/BiChs001Controller.cs`;
`_bi_chs001_replica/tools/{extrair_layout.py, paridade_dados.cjs, testar.mjs}`.
Alterados (mínimo, só acréscimos — originais em `_backups/bi_chs001_20261015/`):
`ClientApp/src/features/production/ControleProducaoPage.tsx` (rota lazy do BI), `features/sidebar/officialNavigation.ts`
(grupo), `features/permissions/permissionLogic.ts` (rotas do guard), `features/sidebar/catalog.json` +
`Services/Sidebar/SidebarCatalog.Generated.cs` (regerados: +24 nós, nenhum alterado), `ClientApp/scripts/test-sidebar.cjs`
(contagem 92 → 116). Até o reinício do backend, salvar customização do sidebar pode ser recusado (catálogo do backend antigo).

## Testes

- `tsc --noEmit`: ok. `vite build` em `C:\Dev\N-A-Connect aa\_build_verify\clientapp_bi_chs001`: ok (BI em chunks sob demanda).
- `dotnet build` em `_build_verify\backend_bi_chs001`: ok.
- Novos isolados `node _bi_chs001_replica/tools/testar.mjs`: 10/10.
- `test-sidebar`: 22/22. `test:pmo`: 263/265 — as 2 falhas são de codificação em testes de outros módulos
  (`physicalProgress`, `qualityAuditActions`), não tocados aqui.

## Próximos passos

1. Usuário: reiniciar o backend e abrir as páginas LDP/Programação com a CHS001.
2. Codex: screenshots do PBIX (cópia) das 10 páginas para a comparação de pixels; auditoria do mapeamento WBX/KPI.
3. Claude: Resumo 30/60/90 (cartões de prazo e rosca, se a correspondência do cronograma for confirmada); depois as 13 restantes.
