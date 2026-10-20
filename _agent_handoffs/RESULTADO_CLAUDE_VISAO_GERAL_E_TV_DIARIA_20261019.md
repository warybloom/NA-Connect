# RESULTADO — 01 VISÃO GERAL (mesma fonte da 02) + PROGRAMAÇÃO DIÁRIA na TV — 19/10/2026

Handoff: `_agent_handoffs/CLAUDE_CORRIGIR_VISAO_GERAL_E_TV_DIARIA_20261019.md`

## Status real

| Item | Status |
|---|---|
| Tarefa A — 01 VISÃO GERAL | Código pronto e testado (lógica + contrato da página). Validação visual no navegador: **pendente (usuário)**. |
| Tarefa B — Programação Diária na TV | Código pronto e testado (frontend + backend + Portal TV). Validação visual: **pendente**. **Backend precisa ser publicado/reiniciado** para a tela nova ser aceita. |
| Aplicação no checkout principal | **Pendente: o usuário roda o `aplicar.py`.** A sessão rodou isolada num worktree e não pode escrever no checkout principal. |
| Deploy/restart do backend | **Não feito.** Nenhum processo foi parado e nenhum sincronizador foi tocado. |
| SQL / LDP / robô / Normalizador / auth / permissões do Emídio | Nada alterado. As consultas ao banco foram só leitura. |

## Como aplicar

O worktree foi isolado em `.claude/worktrees/visao-geral-tv-diaria-20261019`, branch `worktree-visao-geral-tv-diaria-20261019`. O pacote de entrega está fora do repositório, em `C:\Dev\N-A-Connect aa\_entrega_claude_visao_tv_diaria_20261019\`:

- `novo/`: arquivos finais.
- `base/`: arquivos como estavam no checkout ao iniciar.
- `manifesto.txt`
- `aplicar.py`
- `CLAUDE_VISAO_GERAL_E_TV_DIARIA_20261019.patch`: só para referência.

```powershell
cd "C:\Dev\N-A-Connect aa\N-A-Connect"
python "C:\Dev\N-A-Connect aa\_entrega_claude_visao_tv_diaria_20261019\aplicar.py" --conferir   # nada é escrito
python "C:\Dev\N-A-Connect aa\_entrega_claude_visao_tv_diaria_20261019\aplicar.py"              # aplica (aborta inteiro se houver conflito)
# rollback:
python "C:\Dev\N-A-Connect aa\_entrega_claude_visao_tv_diaria_20261019\aplicar.py" --reverter
graphify update .
```

Ao finalizar, os 16 arquivos alterados estavam idênticos à base no checkout principal: não havia trabalho paralelo neles. Mesmo assim, o script confere de novo antes de escrever.

O backup dos originais está em `C:\Dev\N-A-Connect aa\_backup_claude_visao_tv_diaria_20261019\`.

- **Tarefa A** é só frontend: entra via Vite/HMR, sem reiniciar o backend.
- **Tarefa B** muda `TvTelas`/`TvSlide`. Até o backend ser publicado:
  - o editor mostra a tela como "indisponível (backend desatualizado)";
  - um JSON com `dataReferencia` seria recusado.
- O backend compilado está em `C:\Dev\N-A-Connect aa\_build_verify\visao_tv_diaria_20261019\backend-bin`, para o seu procedimento de deploy controlado. Antes de publicar, confira os jobs críticos e não pare o sincronizador em andamento.

## Tarefa A — 01 VISÃO GERAL

Arquivos:
- `ClientApp/src/features/production/analytics/GestaoVistaJamilePage.tsx` (componente `Overview`, `useProgramacao`, `falhasTela`)
- `gestaoVistaJamileLogic.ts` (nova função pura `visaoGeralDaSemana` + `ldpParaEmissao`)

- **Fonte:**
  - O MESMO dataset da 02: `/api/reports/gestao-vista-jamile/programacao` (dbo.tb_WeeklyEngineeringSchedule), uma consulta por projeto, com `inicio` = domingo e `fim` = sábado.
  - `useProgramacao` agora também fica ativo em `screen=visao-geral`.
  - Usa `deduplicarProgramacao` (mesma chave da 02) e `classificarProgramacao`, com Normalizador no consolidado.
  - Linha fora de domingo–sábado é descartada (`foraDaSemana`).
  - `entregasDaSemana` (gates/LDP) não é mais usado na 01. A função continua existindo para `projetoFaseLogic`, que já está desligado.
- **Contagem:**
  - Documento = projeto + código (trim/maiúsculas). Um documento com várias atividades conta uma vez.
  - Linha sem código conta como item próprio, mesma regra da 02.
  - Os cartões mostram "documento(s) distinto(s) em N atividade(s)" e quantos registros repetidos da importação foram contados uma vez.
  - Por dia: o documento aparece em cada dia em que tem atividade. O tooltip diz isso e mostra as atividades.
- **Por projeto:** TODOS os projetos selecionados, na ordem da seleção, inclusive com zero ("— sem programação na semana").
- **Por disciplina:** todas as disciplinas com programação na semana em todos os projetos.
- **Emissão:** conciliada por projeto + código com a LDP oficial já carregada (`emitido` = DocumentEmissionRules: status aprovado ou data de emissão).
  - **Emitido:** data de emissão ≤ sábado; ou status emitido sem data com a semana ainda em curso.
  - **Não emitido:** sem emissão, ou emitido depois do sábado.
  - **Sem comprovação**, sempre com o motivo exibido:
    - atividade sem código;
    - código não encontrado na LDP do projeto;
    - documento cancelado na LDP;
    - código repetido na LDP com situações diferentes;
    - emitido sem data numa semana já encerrada.
  - **Em atraso:** não emitido cuja última atividade programada já passou. Semana encerrada: até o sábado. Semana em curso: antes de hoje. Data futura nunca é atraso.
  - **%Aderência** = emitidos ÷ documentos. **%Desvio** = 100 − aderência. Os dois ficam "—" (indisponíveis) enquanto houver documento sem comprovação, para não tratar "não comprovado" como "não emitido".
  - O gauge por disciplina segue a mesma regra, com a fração "x de y · z s/ comprov.".
- **Falhas:**
  - Na 01, qualquer projeto cuja programação falhe entra em `ConsolidacaoIncompleta`: mensagem, lista dos projetos e "Tentar novamente". Nunca aparece total parcial.
  - Falha da LDP (`usePmoReports`) já bloqueava antes.
- **Semana:** domingo–sábado, com o sábado como corte implícito. Não há data de corte, fase, gate nem botões Projeto/Fase.

Conferência no banco (somente leitura), semana 18–24/10/2026:

| Projeto | Atividades | Disciplinas (Weekly) |
|---|---|---|
| CHS001 | 39 | 4 |
| BML012.01 | 1 | — |
| TIM003 | 2 | — |

Na semana 11–17/10/2026 há 5 projetos com programação. Projeto ativo sem linha na semana aparece com zero.

## Tarefa B — Programação Diária na TV

- **Nova tela `gestao-a-vista-programacao-diaria`:**
  - Só existe na TV (fora de PRODUCTION_NAV/sidebar), na categoria "Gestão à Vista".
  - `MultiProjeto=false` e `DataReferenciaObrigatoria=true`.
  - Usa as MESMAS keys da 02 (`reports.gestao.view`).
  - A `gestao-a-vista-programacao` semanal está intacta no Controle de Produção e na TV.
- **Visual:** a mesma 02 (`Programming` em modo `diario`).
  - Mostra: "DOCUMENTOS DO DIA", "HH PLANEJADO DO DIA", recursos, tabela com dia/disciplina/código/título/elaborador/verificador/aprovador/situação/HH, e entregáveis × HH por disciplina.
  - Não tem "AGRUPAR POR DIA", navegação de semana nem fallback para a LDP.
  - Cabeçalho: "PROGRAMAÇÃO DIÁRIA", nome do projeto e "DIA DE REFERÊNCIA: QUI 15/10/2026".
  - O player da TV também mostra "CHS001 · 15/10/2026".
- **Dados:** o MESMO endpoint semanal oficial (semana da data), deduplicado e filtrado EXATAMENTE no dia. Um projeto por slide; nunca mescla projetos.
- **Data:**
  - `TvSlide.DataReferencia` / `dataReferencia` ("yyyy-MM-dd") é obrigatória nesta tela e validada no backend com `DateOnly.TryParseExact`. O frontend espelha a regra.
  - Data ausente ou inválida no player gera um aviso explícito; "hoje" nunca é usado.
  - Nas demais telas o campo é descartado ao salvar, então não muda dados e o JSON antigo fica idêntico.
- **Editor (Configurações → Telas para TV):**
  - Ao escolher a tela, aparece "Data de referência", que começa vazia. Sem data válida o botão Adicionar fica desabilitado.
  - Vários projetos selecionados geram um item por projeto, com a data escolhida.
  - Cada item da sequência tem o seu campo de data, editável de forma independente.
  - A mesma tela pode ser repetida (limite atual de 50 itens). Só o item idêntico (mesmo projeto + mesma data) é ignorado.
  - "Usar seleção" preserva a data. "Adicionar todas" não inclui a diária.
- **Portal TV (read-only):**
  - A tela já era coberta pelos recursos `gestao-a-vista-*`.
  - Endurecido: em slide com data, `/programacao` só aceita `inicio`/`fim` = semana da data salva (`TvPortalResources.PeriodoPermitido`).
  - Projeto, slide e apresentação continuam validados como antes.
- **Arquivos:**
  - Backend: `Services/Tv/TvConfiguration.cs`, `TvTelas.cs`, `Portal/TvPortalResources.cs`, `Portal/TvPortalMiddleware.cs` (uma condição a mais).
  - Frontend: `tvLogic.ts`, `tvScreens.tsx`, `TvPlayerPage.tsx`, `settings/TelasTvConfigPage.tsx`, `GestaoVistaJamilePage.tsx`.
  - `item3/GerenciaEngenhariaItem3.tsx`: o controle "AGRUPAR POR DIA" passou a ser opcional.
- **Emídio / perfil 9 / Tipo 2:** nada alterado.

## Testes (executados no worktree)

| Verificação | Resultado |
|---|---|
| `tsc --noEmit` | OK, 0 erros |
| `npm run test:pmo` | 329 testes: 323 passam, 6 falham |
| `npm test` (sidebar) | 22/22 |
| Vite build | OK. Saída em `_build_verify\visao_tv_diaria_20261019\vite-dist`; o `wwwroot` do principal não foi tocado |
| `dotnet build` isolado (`-o _build_verify\...\backend-bin`) | OK. O aviso CS8604 em TvConfiguration.cs:87 já existia |
| `NA_Connect.TvPortal.Tests` (DLL nova) | 62/62, sendo 19 novos (18 em TvProgramacaoDiariaTests + 1 em TvPortalTests) |
| `NA_Connect.Planning.Tests` (TvConfigurationTests antigos) | **Não executado.** O projeto não compila por fakes desatualizados (pré-existente); os testes novos de configuração foram colocados no projeto TvPortal |

As 6 falhas do `test:pmo` são as mesmas, pelo mesmo motivo, da linha de base sem as alterações:
- 2 de mojibake;
- "Tela 02: … ATIVIDADES IMPORTADAS COM DATA";
- 3 de Tipo 2 (keys `production.bi-chs001.view` × `reports.gestao.view`, divergência front/back pré-existente que não foi tocada).

Testes novos:
- `visaoGeralProgramacao.test.ts` (11): 16 projetos com zero, todas as disciplinas, dedup/repetição, documento com várias atividades, mesmo código em projetos diferentes, outra semana descartada, conciliação de emissão e indisponibilidade, aderência, semana em curso, zero, contrato da página (falha bloqueia; sem gates/data de corte).
- `tvProgramacaoDiaria.test.ts` (7): catálogo, contrato com TvTelas.cs, data ISO real, validação (data ausente/inválida, dois projetos, JSON antigo), editor (repetições, mesmo projeto em outra data, limite de 50, "Adicionar todas"), player (dia ausente/inválido nunca vira hoje; cabeçalho com projeto e dia; dois projetos nunca exibidos), remontagem na troca de slide.
- `TvProgramacaoDiariaTests.cs` (18 casos) e `TvPortalTests.DailySlideReadsOnlyItsProjectAndWeek`:
  - validação backend: dia inválido/ausente, projeto único;
  - descarte nas demais telas;
  - JSON antigo lido e salvo preservando a outra apresentação;
  - repetições persistidas em ordem;
  - limite de 50;
  - período do portal;
  - projeto do outro slide negado;
  - troca de slide.

Testes antigos ajustados porque a mudança foi intencional:
- `gestaoVistaJamile.test.ts`: fração da 01 e regex do `useProgramacao`;
- `programacaoImportada.test.ts`: regex do `useProgramacao`;
- `tvTelasProducao.test.ts`: contagem do catálogo, de 41 para 42;
- `tvGestaoVistaTipo2.test.ts`: regex do `validarApresentacoes` com o novo argumento.

## Pendências

1. Aplicar com `aplicar.py` e rodar `graphify update .`.
2. Conferir a 01 visualmente com vários projetos, comparando com a 02 na mesma semana.
3. Publicar o backend de forma controlada e depois montar slides diários no editor e testar no player e no Portal TV.
4. Se a regra de "sem comprovação" deixar a aderência indisponível com frequência, decidir com a gestão se a LDP deve ganhar data de emissão ou se o painel deve mostrar a aderência só sobre os documentos conciliados. Hoje é indisponível, por ser a regra que não inventa status.
