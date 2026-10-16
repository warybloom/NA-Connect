# STATUS — Correção funcional das telas BI CHS001 (15/10/2026)

Evidências: `CLAUDE_POS_AUDITORIA.md`, `STATUS_POS_AUDITORIA_CLAUDE.md`, `AUDITORIA_CODEX_PRIORITARIAS.md`, `AUDITORIA_CODEX_GRAFICOS_20261015.md`,
`MAPA_MEDIDAS_DAX.md`, `PRIORIDADE_10_TELAS.md` (não editados). Backup: `_backups/bi_chs001_correcao_telas_20261015/`.
**Nada gravado no banco, backend NÃO reiniciado, robô não executado, PBIX e .mpp intocados, ThemeLogo/brandAssets/norma intocados.**

## 1. Logos (A)
Origem: recurso `LOGO_NA` = substituição das logos CHS (cabeçalho) e Whitefox (coluna) do PBIX — 2 imagens por página × 23.
`pbi/VisuaisEstaticos.tsx`: `ehLogoOmitida` → esses visuais não são renderizados (sem overlay). Mantidas: logo do canto esquerdo
do canvas (`naUsa`, original do PBIX) e a da sidebar do N&A Connect. Captura: `capturas_correcao_telas_20261015/`.

## 2. Diagnóstico do ambiente real (B)
- Backend em execução (iniciado 15/10 16:16) já publica `/ldp`, `/programacao`, `/cronograma` (400 sem projectId, 401 sem sessão).
  As novidades desta etapa (`Trabalho_Horas`, `/cronograma-diario`) **só entram no ar após reinício manual**.
- Não houve chamada autenticada: a tela real exige login (sem credenciais). Conferência visual feita na pré-visualização de dev
  com o cache do PBIX (`?fonte=pbix`) — **teste isolado, não ambiente real**.
- Causa dos painéis vazios: **ausência de fatos no banco**, não erro de tela. `/cronograma` traz só Término/Término LB; faltam
  Início, Início LB, Trabalho LB, Trabalho Real, pai da tarefa e a série diária.

## 3. Fonte comprovada no .mpp (C)
Extrator somente leitura `tools/extrator_mpp/` (MPXJ.Net 13.5.0, o mesmo do importador) sobre cópia local do .mpp CHS001 (versão
17/09; 711/755 folhas do PBIX casadas por GUID): Início LB 711/711, Término LB 711/711, Trabalho 693/711, **trabalho de LB semanal
2315/2315** pares tarefa×semana iguais ao `AssignmentByDay` do PBIX (que vem da exportação Access do mesmo .mpp, grão semanal no
domingo). Trabalho real diverge só pela data da cópia (17/09 × 08/10). Conclusão: o importador lê os campos e os descarta.
`WBX[Trabalho]` já existe no banco (`__NA_CONNECT_WORK_V1.ItemHoras`): 693/711 iguais (18 marcos: PBIX 1 min, banco 0).

## 4. Código (sem mutação de dados)
| Arquivo | Mudança |
|---|---|
| `Controllers/BiChs001Controller.cs` | `/cronograma` + `Trabalho_Horas` e colunas opcionais (só se existirem); novo `GET /cronograma-diario` → vazio + `indisponivel` enquanto a view não existir. SQL validado (883 linhas, 10.418,74 h) |
| `dados/curvas.ts` (novo) | DAX transcrito: ACC / BASE SEM / COLUNA / CURVA / Mostrar Período 30·60·90, COLUNA % Reprogramado 60, Weekly Complete/Deviation 90, PRAZO Início Baseline/Reprogramado/Duração. 60% = horas úteis (8–12/13–17, seg–sex, exceto 01/05/2026); 30/90% = linear por dia — **DAX diferente entre gates, preservado** |
| `dados/medidas.ts` | registro + `requer` (colunas/série obrigatórias) e contexto (eixo, série) |
| `dados/entidades.ts` | WBX: Trabalho, Início, Início LB, Trab LB, Trabalho Real, Level 5–7, NodeKey; hierarquia pelo PAI quando `UID_Tarefa_Pai` existir (GeraWBS); nova entidade `AssignmentByDay` (diário → semana/domingo); `colunasVazias` |
| `dados/motor.ts` | eixo `dCalendario[Data Corte]`; filtro de visual sobre medida (`Mostrar Período = 1`); medida com fonte vazia → PENDENTE com o campo exato; matriz com Rows → PENDENTE explícito (não agrega errado) |
| `dados/useDadosBi.ts` | carrega fontes exigidas pelas medidas; trata `indisponivel` e colunas vazias |
| `pbi/Graficos.tsx` | `GraficoCombo` desenha colunas (eixo primário oculto) + linhas (eixo secundário), tracejado, marcadores, lacunas em BLANK |
| `pbi/VisuaisEstaticos.tsx` | logos (§1) |

## 5. Verificação
- Oráculo independente `tools/oraculo_curvas.py` (Python, mesmo DAX, cache do PBIX) → `fixtures_pbix/esperado_curvas.json`.
  **Motor TS = oráculo** em todos os cartões e todas as semanas da curva dos Resumos 30/60/90 (|Δ| < 1e-9).
  Não é captura do Power BI (Desktop não automatizado) — nenhuma tela declarada "idêntica".
- Valores no corte 08/10/2026 (cache PBIX): R60 ACC 98,48% / 73,87% / −24,61%, semanal 11,67% / 0,00% / −11,67%, duração 47;
  R30 100% / 100% / 0%, duração 53; R90 0,69% / 1,39% / 0,69%, semanal 0,69% / 0,60% / −0,10%, duração 21.
- `testar.mjs` 36/36; `tsc` ok; `vite build` (`_build_verify/clientapp_bi_correcao`) ok, sem fixtures no bundle;
  `dotnet build` (cópia `_build_verify/source_bi_correcao`) 0 erros; `test:sidebar` 22/22. Fixtures temporárias da pré-visualização removidas.

## 6. No ambiente real HOJE (após reinício do backend)
- Funcionam: logos, LDP Eng/3D 30·60·90, Programação, Resumos (prazos, desvio, dias restantes, título, rosca, slicers).
- Continuam **PENDENTES com o campo faltante**: curvas S, cartões ACC/BASE SEM, Duração Baseline, matriz, lista dSemana, curvas 3D.
  Acendem automaticamente quando a migração + importador gravarem os campos (o endpoint já os lê se existirem).
- Matriz hierárquica (expandir/recolher) ainda não reproduzida; slicer dSemana e cross-filter seguem pendentes.

## 7. Autorizações necessárias
1. **SQL de produção:** `proposta_migracao/01_cronograma_campos_curvas_PROPOSTA.sql` (6 colunas nullable + `Cronograma_Tarefa_Dia` + view; rollback incluso). **Não aplicado.**
2. **Importador:** `CronogramaTipo2Parser`/`Repository` gravarem esses campos na mesma transação (referência: `tools/extrator_mpp`). Não alterado.
3. **Reinício do backend** para publicar o endpoint ampliado e `/cronograma-diario`.
4. **"Emgineering - 30%"** (EDT 1.5): recomendado corrigir no .mpp; impacto atual — Resumo 30% sem dados no banco e 196 tarefas entrando nas roscas/curvas do 60% e 90% (filtros por exclusão). Alias só com aprovação, restrito ao CHS001.
