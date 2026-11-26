# Inventário confirmado do PBIX original — BML011.016 (26/10/2026)
Fonte: C:\Users\Christian Sanatana\Downloads\BML011.016.pbix
SHA256: 448947F79CB8F9C20A45246B8FE222B58097C415D8B450D3E83E1BEEB862E634
Arquivo PBIX moderno com Report/definition e DataModel (XPress9); modelagem DAX e consultas necessitam inspeção do DataModel/pbi-tools/Power BI quando possível.
Total: **9 páginas, 118 visuais**, tema **CY24SU10**.
Visuais incluem objetos estéticos + cartões, tabelas, matrizes e gráficos. Este inventário não comprova disponibilidade de todas as fontes no SQL.

| Ordem | Página PBIX | Tela base | Visuais | Campos / visualizações principais |
|---|---|---|---:|---|
| 1 | Resumo | 2048x1123 | 34 | cartões % Acc Planejado, % Acc Realizado, % Deviation, Planned/Actual Weekly, Status WBX, Progress Curve 5 medidas, WBS pivot |
| 2 | LDP | 2048x1123 | 18 | LDP (4), discipline, document status, situation, documentos, baseline/replanned date, emitidos, cancelados, comentários |
| 3 | CAP externo | 2048x1123 | 13 | ACTION ITEM LIST, company, respons., schedule/reschedule, status, descriptions, comments |
| 4 | Critical Analysis | 2048x1123 | 10 | imagens/formas/texto e navegação; verificar visuais ocultos |
| 5 | Programação | 1500x1000 | 8 | WBX Level 3..9, KPI.Inicio LB / Inicio / Término LB / Termino, AssignmentByDay.Trabalho |
| 6 | Controle de Desvio | 1500x1000 | 13 | DESVIO, disciplina, HH, nº, descrição, DATA, STATUS, change order, desvio interno |
| 7 | Controle de Holds | 1500x1000 | 9 | HOLDS, disciplina, nº hold, título, motivo, responsável, status |
| 8 | Curva financeira | 1500x1000 | 6 | Curva financeira.MÊS, progresso planejado acumulado, receita realizada acumulada, valor agregado |
| 9 | Pagamento | 1500x1000 | 7 | Pagamentos: BM, competência, valor, envio, NF, desvio, aprovação prevista, pagamento previsto/real, obs |

## Grafico original (Resumo)
`lineClusteredColumnComboChart` com:
- Category: `dCalendario.Início da Semana`;
- barras `KPI.% BASELINE`, `KPI.% Actual Work Corte`;
- linhas `KPI.% Previsto Reprog Curva`, `KPI.% Previsto LB Curva`, `KPI.%  Acc Realizado Curva`.
Não é a mesma Curva S documental atual do PMO. Medidas distintas não podem ser equiparadas por aparência.

## Proteção exigida
Apenas usuário autenticado cuja identidade REAL no backend seja `IsMasterAdmin == true`; erro, sessão expirada, perfil Gestão/TV, permissões globais não equivalem a Master. Não servir dados nem páginas pela API para terceiros. Evitar carga público de dataset, medidas confidenciais ou arquivos PBIX.

## Critérios de paridade
1. Os 9 nomes, ordem e 118 visuais correspondem ao JSON PBIR original.
2. Navegação e slicers funcionam e não se reduzem a imagens estáticas.
3. Cada KPI só aparece com fórmula e fonte comprovada.
4. Indicar pendente de fonte no nível do campo/visual; não inventar valores.
5. Não alterar dados ou permissões de projetos existentes.

## Projeto ACC validado por SQL read-only
SELECT TOP(12) Project_ID_ACC, Nome_Projeto, Tipo_LDP
FROM dbo.Projetos WHERE Nome_Projeto LIKE '%BML011.016%'
retornou APENAS:
- Project_ID_ACC = `b.9907135f-ff67-43bb-887b-20c3a7f75807`
- Nome_Projeto = `BML011.016 - COOLING TOWER`
- Tipo_LDP = `1` (LDP Tipo 1, SEM gates por padrão).
Nunca assumir Tipo 2 ou forçar gates 30/60/90.
O cronograma WBX do PBIX pode ter suas próprias fases, que não equivalem a
gates LDP Tipo 2 do N&A Connect.

## Fontes relacionadas encontradas no SQL (existência, não equivalência automática)
A consulta read-only a INFORMATION_SCHEMA.TABLES identificou:
- CAP externo / ACTION ITEM: `dbo.tb_ExternalActionLog`, `dbo.vw_Latest_ExternalAction`;
- HOLD: `dbo.tb_HoldControl`, `dbo.vw_Latest_HoldControl`, `gestao.vw_Holds`;
- desvio: `gestao.vw_Desvios_Escopo`, `gestao.vw_AnaliseDesvios_Resumo`, `dbo.[DESVIO DE QUALIDADE]`;
- financeiro: `gestao.vw_Financeiro_Projeto`, `gestao.vw_Financeiro_Documento`, `gestao.vw_Financeiro_Disciplina`;
- cronograma: `dbo.Cronograma_Projetos`, `dbo.VW_Cronograma_Projetos_BI`.
Essas fontes são APENAS candidatas. Validar chaves, IDs e semântica de campos
antes de usar. Não contar linhas de projetos alheios, não misturar CAP interno
e externo, nem substituir BMs/receitas do PBIX por custos/hora da LDP.

## Validação real de cobertura por projeto (26/10/2026, leitura SQL)
Projeto exato ACC `b.9907135f-ff67-43bb-887b-20c3a7f75807`, Tipo LDP 1:
- `dbo.Ldp_Unificada`: **4.830** linhas;
- `dbo.vw_Latest_ExternalAction`: **20** linhas;
- `dbo.vw_Latest_HoldControl`: **0** linhas;
- `dbo.vw_Latest_ScopeDeviation`: **0** linhas;
- `dbo.VW_Cronograma_Tipo2_Ultima_Versao`: **0** linhas;
- `dbo.Cronograma_Projetos`: **377** linhas.
**ALERTA DE MAPEAMENTO:** a fonte de cronograma escolhida no contrato v1
(`VW_Cronograma_Tipo2_Ultima_Versao`) é inadequada para este Tipo LDP 1
sem registros nessa view; não declarar a Programação/Resumo "com dados"
só porque a view existe. Há 377 tarefas em `Cronograma_Projetos`:
validar a semântica e a versão de cada tarefa antes de propor fonte
adicional. A tabela Tipo 1 contém
`EDT_WBS, TASK_NAME, PLANNED_DATE_START, PLANNED_DATA_BASELINE,
REPLANNED_DATE, Trabalho, PERCENT_PHYSICAL_COMPLETED,
Percent_Previsto_LB, Percent_Previsto_Reprogramado, Percent_Realizado,
Termino_Linha_Base, Termino, Data_Leitura_Cronograma`.
Não equivale automaticamente a AssignmentByDay / DAX do PBIX; dados
diários só existem se fonte temporal original estiver comprovada.

## DataModel original extraido com pbixray (26/10/2026)
A leitura somente local do PBIX original identificou **13 tabelas, 75 medidas DAX e 3 relacionamentos**.
Tabelas efetivamente presentes: ACTION ITEM LIST, Assignment, AssignmentByDay, DESVIO, HOLDS,
LDP (4), ResourceByDay, Tabela, TaskByDay, WBX, dCalendario, dEAP, dSemana.
Algumas tabelas usadas nas definicoes PBIR, como **Curva financeira** e **Pagamentos**,
NAO existem no DataModel embutido; nao assumir que os visuais dessas paginas tem
dados presentes e nem substituir por custos do N&A Connect.
Dataset embutido historico: AssignmentByDay 3.247 linhas, WBX 264, LDP (4) 105,
ACTION ITEM LIST 20, DESVIO 4 e HOLDS 1. Esses valores sao o snapshot
DO PBIX, nao a contagem SQL atual. A API nova consulta SQL atual e distingue
fontes pendentes, em vez de publicar o snapshot como se fosse informacao viva.
Medidas de KPI sao DAX, p.ex. KPI.Trabalho Real = SUM(AssignmentByDay[Trabalho Real]);
outras envolvem ALLSELECTED, datas e relacionamentos, portanto somar
colunas atuais SEM provar a equivalencia nao reproduz o Power BI.
O arquivo de auditoria detalhada (privado) esta em
%LOCALAPPDATA%\NAConnect\BI_BML011016\auditoria_pbir_privada\modelo_inspecao_privada.json.
