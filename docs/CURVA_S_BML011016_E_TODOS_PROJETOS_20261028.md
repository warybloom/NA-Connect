# CURVA S — BML011.016 E TODOS OS PROJETOS (28/10/2026)

## Escopo e preservação
Solicitante: curva do reprogramado em TODOS os projetos; BML011.016
primeiro marco em 14/05/2026 em vez de 17/05; corrigir leitura do
acumulado ~78% vs reprogramado ~98%; BASELINE ESTÁ CORRETA e não deve mudar.
Claude e Codex trabalham em alterações independentes de status/documentos,
pacotes ETS e sidebar: não editar suas áreas.

## Evidência original BML011.016
Fonte protegida: %LOCALAPPDATA%\NAConnect\BI_BML011016\source\BML011.016.pbix,
SHA256 448947F79CB8F9C20A45246B8FE222B58097C415D8B450D3E83E1BEEB862E634.
Modelo extraído em
%LOCALAPPDATA%\NAConnect\BI_BML011016\auditoria_pbir_privada\modelo_inspecao_privada.json
Datas semanais do PBIX dSemana: PRIMEIRA 17/05/2026.
Cronograma SQL do projeto ACC b.9907135f-ff67-43bb-887b-20c3a7f75807:
Kick-off Meeting, baseline 12/05/2026;
primeiros três entregáveis de referência Equipment Tags,
Preliminary Drawing e Load Information for Civil Design:
Planned_Data_Baseline 14/05/2026.
O eixo novo inclui marco inicial 14/05 com a evolução das curvas NA DATA;
mantém intactos os pontos semanais 17/05 em diante, sem deslocar baseline.

Auditoria numérica direta usando as 264 tarefas WBX do PBIX e expressões
KPI.% Previsto Reprog Curva e KPI.% Previsto LB Curva:
Data 02/08: reprogramado 94,171%, baseline 99,999%;
09/08: reprogramado 97,020%, baseline 100%;
16/08: reprogramado 98,063%, baseline 100%;
23/08: reprogramado 98,60665093871342%, baseline 100%;
30/08: reprogramado 100%, baseline 100%.
NÃO transformar 78% em 98% artificialmente. Confirmar a série selecionada:
78% de outro indicador (emitido/realizado/percentual de período) não pode
ser substituído por 98% da curva REPROGRAMADA.

## Código alterado
- ClientApp/src/features/production/biBml011016/useDadosBml.ts:
  ponto inicial 14/05 na Curva S BML, sem inventar barra semanal antecipada.
- Services/Reports/PmoCurvaS.cs: adicionadas séries cumulativas
  ReprogramadoHh, ReprogramadoCusto, ReprogramadoDocumentos.
  Para cada documento NÃO CANCELADO com baseline válida, usa Replanned_Date
  válida; se não foi alterada, mantém na baseline original. Mesmo universo e
  denominador (baseline), não mistura outros cronogramas nem datas de HH trabalhadas.
  LDP não alterada; emissão continua separada por Emission_Date.
- ClientApp/src/components/pmo/pmoMultiCore.ts:
  série opcional de reprogramado em projeto/multi-projetos,
  soma acumulados em data cronológica com valor 0 antes do primeiro evento,
  nulo quando falta fonte. Não faz média simples de percentuais.
- ClientApp/src/features/production/biChs001/CurvaSDocumentalBi.tsx:
  3ª linha acumulada do original PBIR: baseline, reprogramado, emitido;
  barras continuam incrementos semanais. Usa campos cumulativos da API;
  NÃO soma os acumulados novamente; Y = frações 0..1.
- Tests/NA_Connect.GestaoVista.Tests/PmoCurvaSTests.cs:
  novos testes 98%->100%, baseline intacta, cancelados e ausência de data.

## Testes executados
- BML original: script de regressão em
  C:\Dev\_agent_runs\curva_s_auditoria_20261028\test-bml-curva.cjs:
  14/05 + 17/05 preservados; 23/08 reprogramado=0.9860665093871342;
  baseline=1, curva monotônica — passou.
- Multi-projetos:
  C:\Dev\_agent_runs\curva_s_auditoria_20261028\test-consolidado.cjs:
  98+2=100; sem fonte fica null; PASSOU.
- Testes C# CURVA S isolados: 8/8 PASSARAM.
- Suíte geral GestaoVista: 179 aprovados de 181, 2 reprovados
  ligados a GestaoVistaDocumentStatusTests (Tipo 1 percentual físico),
  assuntos sob responsabilidade das alterações paralelas de Claude.
  NÃO alterar essas regras ao corrigir Curva S.
- TypeScript passou após correção do null da data.

## Publicação
NÃO reiniciar nem publicar a DLL da branch compartilhada enquanto Claude
e Codex editam o backend / Resumo: um build dessa árvore incluiria alterações
parciais alheias. É imprescindível CI/build integrado/backup da release
antes de pôr a nova API em 5277. O frontend Vite dev pode mostrar
o BML atualizado via HMR, mas a Curva S documental genérica só exibirá
reprogramado após o backend com os novos campos ser implantado.
Backend existente foi restaurado pelo supervisor e login HTTP 200
durante a execução dos testes.

## Critérios de aceite pendentes
- Verificar em browser autenticado BML primeira data 14/05 e linha
  reprogramada 98,61% em 23/08; baseline intocada.
- Verificar um projeto tipo 1 diferente, um tipo 2 e multi-projetos
  com emissão e reprogramação (e sem reprogramação).
- Homologar status dos 2 testes alheios antes de deploy integrado.
