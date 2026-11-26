# CHS001 — Curva S = Gestão à Vista Tipo 2 (26/10/2026)

## Causa confirmada
A tela Controle de Produção > Curva S chamava `/api/reports/curva-s` e
`PmoCurvaSRules.Build` (documentos da LDP; data de baseline e emissão).
O BI CHS001 / Gestão à Vista Tipo 2 usa WBX (.mpp) e AssignmentByDay,
com medidas DAX do modelo PBIR. Mesmo projeto != mesma fonte/denominador.

## Correção (sem conflito com Codex)
- NOVO: `ClientApp/src/features/production/biChs001/CurvaSOficialChs001.tsx`.
  Reusa `useDadosBi`, `GraficoCombo` e layouts PBIR p02/p03/p04/p05
  do MESMO código utilizado na Gestão à Vista Tipo 2.
- Roteamento: `ClientApp/src/features/production/ProductionPageContent.tsx`.
  Somente projeto ACC `b.fcdb7ebd-1b23-440d-a04b-90391098522e` com seleção única
  recebe essa curva; os demais continuam no `ViewCurvaS`.
- 30% é a etapa inicial, por reproduzir as cinco medidas da imagem
  (% Baseline, % Actual Work, % BL0 Planned, % Planned, % Actual Complete).
  Resumo Geral, 60% e 90% selecionáveis.
- Nenhum componente `ViewCurvaS` ou `ProgressCurveCombo` foi editado por
  esta tarefa. Claude/Codex podem continuar em paralelo.
- Em caso de fonte não mapeada: exibe aviso, não cria linhas, 0% ou 100%.

## Evidências da fonte
- `bi_chs001.Versao_Cronograma`: versão 1 importada 16/10/2026,
  status do .mpp 08/10/2026; 883 tarefas (755 folhas), 16.024 registros diários.
- Mesmo carregador `/api/bi-chs001/schedule/tarefas` e
  `/api/bi-chs001/schedule/trabalho-diario?versao=1` das páginas Tipo 2;
  filtros, datas de corte, denominadores e motor no mesmo código.
- Endpoints protegidos por permissões existentes; não contornar auth.

## Testes
- TypeScript `tsc --noEmit`: EXIT 0.
- Build Vite em destino ISOLADO:
  `C:\Dev\_agent_runs\chs001_scurve_20261026\dist` — EXIT 0.
- Testes NUMÉRICOS existentes contra o cache PBIX, transpilados por esbuild
  para contornar imports TS sem extensão em Node22: 26/26 PASS; log:
  `ClientApp/curva_chs001_paridade_bundled_20261026.log`.
- 30/60/90: 5 medidas por visual conferidas em JSON PBIR.
- Vite dev porta 5173 serve o módulo novo HTTP 200; backend 5277 LISTENING.
- Falta conferência de UI com sessão autenticada, comparando exatamente o
  mesmo projeto, gate e data de corte. Sem declarar homologação visual.
- Não foi necessário reiniciar backend nem executar SQL DDL/DML.
- Build isolado foi compilado para validação; Vite dev já serve o código-fonte,
  mas qualquer publicação de frontend estático exige usar processo regular de deploy.

## Impacto nos demais projetos
Não foram convertidos para progresso de cronograma; seguem curva
documental. Não transpor as cinco medidas do CHS001 a outras obras
sem cronograma/task-timephased equivalente.

## Pendências
1. Comparar em tela autenticada Curva S > CHS001 > 30% com
   Gestão à Vista Tipo 2 > Resumo 30%, mesmo projeto e data.
2. Caso haja diferença temporal, avaliar se a versão 1 do .mpp
   está atualizada; sincronização do cronograma deve ser decisão separada.
3. Validar acessos de usuários que só possuem `reports.curvas.view`
   mas não as chaves de leitura de cronograma.
