# BI BML011.016 — DADOS REAIS DO PBIX EM VEZ DE "PENDENTE DE PARIDADE" (26/10/2026)

## Problema
No print do usuário, Resumo tinha praticamente todos os visuais amarelos
"PENDENTE DE PARIDADE". A tela anterior usava apenas fontes SQL atuais e
bloqueava qualquer measure DAX do PBIX, mesmo com os dados do próprio PBIX
disponíveis na máquina. Isso não atendia à exigência de reprodução.

## Correção executada
- Leitura direta em modo read-only do DataModel do original BML011.016.pbix,
  SHA256 448947F79CB8F9C20A45246B8FE222B58097C415D8B450D3E83E1BEEB862E634.
- Materializados no diretório PRIVADO %LOCALAPPDATA%\NAConnect\BI_BML011016\snapshot:
  10 tabelas originais e manifesto, 12,16MB. Inclui WBX (264 tarefas),
  AssignmentByDay (3247 registros diarios), LDP (4) (105 linhas),
  ACTION ITEM LIST (20), DESVIO (4), HOLDS (1), dSemana (16), dCalendario.
- Novo GET /api/bi-bml011016/pbix/snapshot/{tabela}, allowlist de tabelas e
  gate IsMasterAdmin real na classe e na action (401 anônimo/403 nao-master).
  Leitura exclusivamente do snapshot privado; fonte atual SQL continua em API
  separada sem ser confundida com snapshot do PBIX.
- Frontend useDadosBml agora consome as tabelas originais via a API master-only
  e converte campos de tabelas, slicers, donut de Status1 e a matriz WBX.
- Medidas DAX documentadas em daxSnapshot.ts implementadas explicitamente,
  respeitando filtros WBX, relaçao TaskUID, denominações de HH, datas e cutoff
  05/08/2026 fixado nas fórmulas originais do PBIX.
- Cartoes e serie PROGRESS CURVE do Resumo agora tem dados reais.
  02/08/2026: Planejado acum 95,60%, Realizado acum 89,33%,
  desvio -6,27 pontos; linha reprog 94,17%, LB 100,00%.
- LDP: contagens CALCULATE(COUNTROWS(...)) para Emitido, Cancelado,
  Revisão N&A, Revisão BML conforme status original.
- Origem "PBIX original (snapshot histórico, não sincronizado com SQL)" em
  destaque; não falseia atualizacao para 26/10/2026.

## Validações
- TypeScript e Vite build isolado aprovados.
- 68/68 testes backend aprovados (incluindo rota snapshot master-only
  em anônimo/nao-master).
- Deploy com rollback 22:47, login HTTP 200, snapshot sem login HTTP 401.
- Teste de converterSnapshot aplicado diretamente aos layouts privados:
  Resumo 17 visuais de dados OK, 0 pendentes;
  LDP 9 OK, 0 pendentes;
  CAP Externo 5 OK, Programação 2 OK, Desvio 7 OK, Holds 5 OK.
  Total 45 visuais de dados OK; 2 visuais de Pagamento ainda sem tabela
  nos dados embutidos.
- Nao houve alteracao na LDP, SQL, nos BIs CHS001, Curva S geral,
  menu lateral ou normalizador de siglas. Nenhum mock inserido.

## Ainda faltam
- Acesso ao browser autenticado real Master Admin para homologacao visual.
- Equivalencia validada de medidas DAX ainda nao implementadas fora do Resumo;
  controle de interacoes e filtros cruzados complexos.
- Curva financeira e Pagamentos NÃO são tabelas dentro do DataModel original
  inspecionado. Necessário definir fontes e calculos reais antes de preencher.
- Dois campos de DESVIO estao nulos no PBIX original e permanecem sem valor;
  nulo não vira 0.

## Arquivos
ClientApp/src/features/production/biBml011016/useDadosBml.ts
ClientApp/src/features/production/biBml011016/daxSnapshot.ts
ClientApp/src/features/production/biBml011016/BiBml011016Page.tsx
Controllers/BiBml011016Controller.cs
Tests/NA_Connect.BiBml011016.Tests/SegurancaTests.cs
run-backend-persistent.ps1
_bi_bml011016/tools/export_pbix_snapshot.py
_bi_bml011016/tools/deploy_snapshot_pbix_20261026.ps1
C:\Dev\_agent_runs\bi_bml011016_snapshot_20261026\
