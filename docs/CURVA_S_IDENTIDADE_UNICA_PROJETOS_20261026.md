# Curva S — identidade visual única (26/10/2026)

## Requisito do usuário
Não apenas "usar o CHS como referência": é O MESMO renderizador/identidade
do Progress Curve do BI CHS001 para TODOS os projetos no Controle de Produção e TV.
A única diferença de interface estrutural é a presença de gates quando a
fonte comprovadamente é faseada (LDP Tipo 2); LDP Tipo 1 e sem LDP NÃO têm gates.

## Alterações no código
- `ClientApp/src/features/production/biChs001/CurvaSIdentidadeVisual.tsx`:
  frame/legenda/área SVG únicos, com o MESMO `GraficoCombo` de
  `biChs001/pbi/Graficos.tsx`. Identidade única para qualquer dado.
- `CurvaSOficialChs001.tsx`: usa o frame compartilhado + medidas oficiais
  WBX / AssignmentByDay e 5 campos originais PBIR (Resumos Geral, 30, 60, 90).
- `CurvaSDocumentalBi.tsx`: usa o MESMO frame e visual p03 com os
  campos comprovados pela LDP, sem forjar série reprogramada; alterna
  HH, custo e documentos e período diário/semanal/mensal.
- `ProductionPageContent.tsx`: CHS001 com fonte BI real; demais com fonte
  documental (sem confundir HH emitido com HH efetivamente realizado);
  porém MESMO componente visual em ambos.
- `ViewCurvaS.tsx` / `ProgressCurveCombo.tsx` NÃO editados por esta tarefa,
  pois Codex trabalha neles independentemente. Não sobrescrever alterações.

## Gates — regra e pendência funcional
- LDP Tipo 1 e sem LDP: sem gates na Curva S.
- CHS001: gate 30/60/90 com série WBX real (o seletor é efetivo).
- Outros projetos LDP Tipo 2: o endpoint documental atual
  `/api/reports/curva-s` produz SOMENTE o total do projeto;
  NÃO tem série diária/semanal por gate. Não habilitar botões 30/60/90
  que mostrem a mesma curva geral. Identificação tipoLdp feita pelo
  metadata da aba LDP; a tela exibe limitação explícita. Para concluir
  seleção por gate em todos os Tipo 2, implementar fonte TEMPORAL
  comprovada por gate (baseline/reprogramado/realizado) e testar.
- Nunca calcular histórico de realizado a partir de `Physical_Completed`
  atual sem data do evento. Isso inventaria uma Curva S.

## Validação
- Compilação TypeScript passou no primeiro estágio da refatoração.
- Vite build isolado (antes da nota tipoLdp) passou:
  `C:\Dev\_agent_runs\chs001_scurve_20261026\dist-unified`.
- Vite dev 5173 passou HTTP 200 nos novos módulos.
- Backend 5277 permanece ativo, sem restart, DB sem alteração.
- Falta homologação visual browser autenticado e testes de permissão,
  especialmente perfil com Curva S mas sem acesso ao relatório LDP Tipo 2.

## Critérios para concluir a próxima etapa
1. Páginas CHS001 e de projetos Tipo 1/sem LDP devem utilizar mesma
   aparência real (gráfico SVG, barras, linhas, cores, tipografia).
2. Para Tipo 2 com cronograma temporal de gate, toggles de gate devem
   alterar NUMERICAMENTE a série e datas; nada de estado ornamental.
3. LDP Tipo 1/sem LDP jamais ganham filtros de gate nem percentuais
   inventados; falta de dado deve ter mensagem explicativa.
4. Não interromper trabalhos paralelos de Codex e Claude.
