# STATUS PÓS-AUDITORIA — CLAUDE — BI CHS001 (10 telas prioritárias) — 15/10/2026

Evidência de base: `AUDITORIA_CODEX_PRIORITARIAS.md`, `MAPA_MEDIDAS_DAX.md`, `DIVERGENCIAS_BI_CHS001.md` (não editados) +
cache do PBIX (cópia SHA-256 `6a5d0a83…c6983aaa197`, igual ao original) + banco CHS001 em leitura (somente SELECT).
Nada foi gravado no banco, backend NÃO foi reiniciado, nada publicado, robô não executado, PBIX original intocado.

## 1. Discrepâncias críticas Codex × Claude — resolvidas com rastreio

| Ponto | Conclusão comprovada |
|---|---|
| Eng 30 "Canceled = 0" × "9 CANCELLED" | **Indicadores diferentes, ambos corretos.** Cartão `9aa32598` exibe `dFase Engenharia 30%[CANCELED 30]` = `COUNTROWS(SITUATION 30% = "Canceled")` → **BLANK** (o Codex anotou como 0; o cartão do Power BI mostra `(Blank)` — confirmar só com captura). `LDP (4)[Cancelled]` está no painel de filtros desse cartão **sem condição** (não filtra). Os 9 são categorias `CANCELLED` dos gráficos STATUS (`DOCUMENT STATUS 30%`, `c1d38717`) e SITUATION (`SITUATION 30%`, `96cec642`). Nenhuma grafia normalizada. Teste: `Eng 30 — CANCELED (cartão) ≠ CANCELLED (gráficos)`. |
| Programação 148 / 103 / 108 | 148 linhas no cache = 28 NOT STARTED + 26 ELABORATION + 49 VERIFICATION + 40 AUTORIZATION + 5 ISSUANCE FLOW. ACTIVITIES (`d2c0e0e9`) = filtro do cartão {ELABORATION, ISSUANCE FLOW, NOT STARTED, VERIFICATION} = **108**; gráfico e tabela = filtro {ELABORATION, NOT STARTED, VERIFICATION} = **103**. Já estava certo; agora testado contra o cache. |
| Programação: slicer `WBX[Término]` e relação 1:1 bidirecional com LDP | Modelo (pbixray): **WBX não tem relacionamento com `Programação Semanal`** → o slicer (modo Between, salvo 09/02–06/03/2026) não altera os visuais da página. A relação Programação↔`LDP (4)` só atua se LDP for filtrada — não há filtro de LDP nesta página. Slicer segue PENDENTE (Between não reproduzido), exibindo o intervalo salvo; a página não carrega mais WBX. |
| Resumo 60/90 = exclusão | Filtros de página preservados literalmente (`NOT IN` de 7 valores). Teste prova que valor fora da lista ENTRA. |
| LDP 3D 90 — "1 AT no total 7" | Há **dois "7" diferentes**: cartão Total `a2dbaffe` = filtro de página `FORMAT NOT IN {A,B,D,null}` → 6 modelos 3D + 1 AT (`CHS001-000-3D1-RE-001`); tabela `51e521cc` tem filtro **de visual** `FORMAT = 3D` e sem filtro de Fase → 7 modelos 3D (um, `MEM-3D-001`, com Fase 90% em branco). Ambos já corretos; agora testados. |
| `KPI[Adh]` | Não é campo nem filtro: é **ícone condicional órfão** (`values[3].icon` com seletor `KPI.Adh`, série não projetada; a medida `Adh` nem existe no modelo). Não afeta render; tabelas LDP calculam normalmente. |
| Interações/relacionamentos | `dSemana` e `dCalendario` não têm relação com WBX/LDP: a data só entra via `SELECTEDVALUE` nas medidas. `AssignmentByDay→WBX/dCalendario` sustenta curvas — sem fonte (ver §4). |

## 2. Matriz das 10 páginas (visuais de dados)

Legenda: **OK-PBIX** = implementado e igual ao cache do PBIX (teste automatizado) · **OK-PBIX / banco difere** = motor igual ao PBIX, banco mais novo · **PENDENTE fonte** · **PENDENTE interação**.

| Página | Visual | Situação |
|---|---|---|
| Resumo 30/60/90 | Término LB + Tendência (`6d9c8b46`/`1cb81bc0`/`bc7e47d1`) | **OK-PBIX** (novo). Banco: 60/90 **iguais** ao PBIX; 30 = (Blank) — ver §3 |
| | Desvio dias (`8d19da71`/`acd6bef2`/`9a23de1b`) | **OK-PBIX** (13/21/22). Banco: 60/90 iguais; 30 (Blank) |
| | Dias Restantes (`bb4e1966`/`c859d8df`/`819db0fa`) | bloco Dias Restantes **OK-PBIX** (0/11/33); bloco Duração Baseline **PENDENTE fonte** (Início da LB) |
| | Activity Title (`9f5e7fa9`/`ae8c65ff`/`b3c33a1f`) | **OK-PBIX** "Week - 02/10 to 08/10" (data de corte salva 08/10/2026) |
| | Rosca Status1 (`4a4c60d0`/`b2955cba`/`be151583`) | **OK-PBIX** (190/6 · 152/113/82 · 174/16); banco difere (Status1 atualizado no .mpp + §3) |
| | Segmentações Disciplina / Responsabilidade / Pacote | implementadas sobre WBX (filtram rosca e ALLSELECTED dos prazos — testado) |
| | Segmentação Date (dSemana) | **PENDENTE fonte**: lista de semanas depende de Início da LB; valor salvo exibido e aplicado às medidas |
| | 6 cartões ACC / BASE SEM (+ Weekly Complete/Deviation no 90) | **PENDENTE fonte** (AssignmentByDay; ACC % Planejado: Início + Trabalho) |
| | Curva S (combo) | **PENDENTE fonte** (AssignmentByDay) |
| | Matriz EDT/BL0/Start/Finish/Status | **PENDENTE fonte** (Level 5–7 e datas de Início) |
| LDP Eng 30 | 5 cartões, 3 gráficos, tabela | **OK-PBIX** (181/172/(Blank)/(Blank)/(Blank)); banco idêntico exceto rótulo INFRASCTRUTURE→INFRASTRUCTURE |
| LDP Eng 60 | idem | **OK-PBIX** (357/129/75/129/(Blank)) / banco difere (361, 81 cancel.) |
| LDP Eng 90 | idem | **OK-PBIX** (166) / banco difere (170) |
| LDP 3D 30 | idem + curva | **OK-PBIX** (4/4); curva PENDENTE fonte; banco: ITEM renumerado |
| LDP 3D 60 | idem + curva | **OK-PBIX** (7, canceled 1) / banco difere (status 60% atualizados); curva PENDENTE |
| LDP 3D 90 | idem + curva | **OK-PBIX** (7 = 6×3D + 1 AT); curva PENDENTE |
| Programação | 4 cartões, gráfico, tabela | **OK-PBIX** e banco idêntico (108/26/28/5, 103); slicer WBX[Término] PENDENTE interação (Between) |

Cross-filter por clique (interações entre visuais) continua **PENDENTE interação** em todas as páginas.

## 3. Prazo/duração/datas dos Resumos a partir do cronograma oficial — avaliação

Fonte usada: `VW_Cronograma_Tipo2_Ultima_Versao` (leitura de 15/10/2026 11:58). Conferência tarefa a tarefa com o WBX do cache
(715 tarefas casadas por nível + nome + EDT): **Término da LB e Término iguais no dia em 100%**; Disciplina 100%; Status1 difere
em parte do 60/90 (campo customizado do .mpp, muda com o avanço).

- O banco **não guarda o pai** da tarefa e há folhas do 90% com EDT "1.6.x" digitada → o Level 4 é derivado da **ordem** do
  cronograma (último resumo de nível 2 antes da folha), como o `GeraWBS` do PBIX faz pela hierarquia: dá 196 / 347 / 190 = cache.
- **Implementável e implementado:** Término Baseline, Término Reprogramado, Desvio, Dias Restantes, Activity Title, rosca, slicers WBX.
- **Não implementável sem inventar:** Início/Início da LB (`PLANNED_DATE_START`/`PLANNED_DATA_BASELINE` nulos nas 755 folhas) →
  Duração Baseline, colunas BL0 Start/Start da matriz, ACC % Planejado, lista dSemana. `Trabalho` nulo por tarefa.
- **Achado de fonte (decisão do usuário):** a EDT 1.5 do cronograma atual chama-se **"Emgineering - 30%"**; o PBIX filtra
  "Engineering - 30%". Com os filtros preservados: Resumo 30% fica (Blank) e, como 60/90 são exclusão, essas 196 tarefas
  **entram** nas roscas do 60% e 90%. A tela mostra uma faixa de aviso fora do canvas explicando isso. Nenhuma correspondência foi
  criada. Opções: corrigir o nome no .mpp (recomendado) ou aprovar uma correspondência explícita.

## 4. Fontes faltantes (necessárias para tirar PENDENTE)

1. **AssignmentByDay** (trabalho real e de LB por tarefa por dia, do .mpp) → curvas S, cartões ACC/BASE SEM, Weekly Complete/Deviation, curvas 3D.
2. **Início e Início da LB por tarefa** (persistir na leitura do cronograma) → Duração Baseline, matriz, ACC % Planejado, dSemana.
3. **Trabalho por tarefa** (WBX[Trabalho]) → ACC % Planejado.
4. **Pai da tarefa / Level 5–7** → matriz hierárquica.
Valores do PBIX para essas medidas podem ser testados com o cache (`AssignmentByDay` 10.186 linhas), mas não foram embutidos como dado vivo.

## 5. Correções feitas nesta etapa (com valor esperado PBIX × calculado)

| Correção | Esperado PBIX | Calculado (motor) |
|---|---|---|
| Nova entidade WBX (cronograma) + medidas KPI 30/60/90 PRAZO e Activity Title, com ALLSELECTED e data de corte | R30 11/08→24/08, 13, 0 · R60 28/09→19/10, 21, 11 · R90 19/10→10/11, 22, 33 · "Week - 02/10 to 08/10" | idêntico (testes 22/24/26) |
| Rosca Status1 | 190/6 · 152/113/82 · 174/16 | idêntico |
| Cartão de vários blocos: bloco sem fonte vira selo, os demais calculam (antes o cartão inteiro ficava pendente) | Dias Restantes 0/11/33 | idêntico + Duração = PENDENTE |
| `cardVisual` respeita `layout.orientation` (1 = vertical) e `label.textWrap` — datas apareciam cortadas "19/1…" | blocos empilhados | ver captura `p04_resumo-60_cache-pbix.jpg` (antes: `…_ANTES-orientacao.jpg`) |
| Motivos de PENDENTE específicos (fonte necessária), agrupados | — | — |
| Slicers Between / dSemana: não carregam fonte, motivo explícito, valor salvo exibido | — | — |

Regressão: 63 visuais LDP + Programação → números idênticos ao relatório anterior (lado PBIX e lado banco);
`paridade_pos_auditoria_20261015.json` (102 visuais: 75 iguais PBIX×banco, 27 diferem só por dado mais novo / §3).

## 6. Testes

- `node _bi_chs001_replica/tools/testar.mjs`: **32/32** (10 anteriores + 22 novos em `__tests__/paridadePbix.test.ts`, motor × cache do PBIX das 10 páginas).
- `tsc --noEmit`: ok. `vite build` → `_build_verify/clientapp_bi_chs001_pos`: ok; nenhuma fixture no bundle (grep).
- `dotnet build` (cópia em `_build_verify/source_bi_pos` → `backend_bi_chs001_pos`): 0 erros.
- `test:sidebar` 22/22 (navegação). `test:pmo` 263/265 — mesmas 2 falhas pré-existentes de outros módulos.
- SQL do novo endpoint executado no banco: 883 linhas, 755 folhas.

**Screenshots:** só da reprodução React (pré-visualização dev, `capturas_pos_auditoria_20261015/`), com o cache do PBIX e com o
banco atual. **Não há captura renderizada do Power BI** (Desktop não automatizado) → nenhuma tela declarada "idêntica"; a
fidelidade é estrutural (PBIR) + numérica (cache). Pontos a conferir com captura real: `(Blank)` × "0" nos cartões novos; rótulos
da rosca próximos à borda.

## 7. Parecer de PRONTIDÃO PARA TESTE AO VIVO

- **Dados já prontos (após reinício do backend):** LDP Eng 30/60/90, LDP 3D 30/60/90 (exceto curvas), Programação; nos Resumos 60/90:
  prazos, desvio, dias restantes, título da semana, rosca e slicers.
- **Dependentes do novo backend (reinício manual pelo usuário):** `/api/bi-chs001/ldp`, `/programacao` e o **novo** `/cronograma`
  (`Controllers/BiChs001Controller.cs`, keys `reports.gestao.view` ou `reports.cronograma.view`). Antes disso: "Endpoint do BI indisponível".
- **Dependente de decisão:** Resumo 30% e roscas 60/90 — nome "Emgineering - 30%" no .mpp (§3).
- **Fontes faltantes:** §4 (curvas, ACC/BASE SEM, duração, matriz, lista de semanas).
- **Correções pendentes:** interações de cross-filter; slicer Between; captura real do PBIX para paridade de pixels.

## Arquivos

Alterados (backup em `_backups/bi_chs001_pos_auditoria_20261015/`, rollback = copiar de volta):
`biChs001/dados/{tipos,entidades,medidas,motor,useDadosBi}.ts`, `biChs001/pbi/{VisuaisDados,VisualBi}.tsx`, `BiChs001Page.tsx`,
`biChs001.css`, `__preview__/preview.tsx` (só dev), `Controllers/BiChs001Controller.cs` (+ endpoint), `tools/paridade_dados.cjs`.
Novos: `biChs001/dados/avisos.ts`, `__tests__/paridadePbix.test.ts`, `_bi_chs001_replica/fixtures_pbix/` (cache do PBIX, só teste),
`paridade_pos_auditoria_20261015.json`, `capturas_pos_auditoria_20261015/`. Nenhum arquivo do Codex, do PBIX ou fora do módulo editado.
