# Divergências — réplica BI CHS001

> Leitura do frontend atual; nenhuma correção foi aplicada por Codex.

## Prioridade crítica para o Claude

| Gravidade | Área | Divergência comprovada | Efeito | Evidência |
|---|---|---|---|---|
| Crítica | Resumos 30/60/90 | O registro `MEDIDAS` implementa apenas contagens de LDP por gate; as medidas KPI de prazo, ACC, semana e curva usadas nos três Resumos não estão registradas. | Cards, curva, matriz e percentuais ficam `PENDENTE`/BLANK, mesmo com dados carregados. | `ClientApp/src/features/production/biChs001/dados/medidas.ts`; 77 medidas requeridas em `MAPA_MEDIDAS_DAX.md`. |
| Crítica | Modelo | O motor declara que filtros de outra entidade não restringem o visual e não propaga relacionamentos. | Realizado de `AssignmentByDay`, filtros WBX e Programação↔LDP não reproduzem o Power BI. | `dados/motor.ts`, função `linhasFiltradas`; 16 relações em `AUDITORIA_CODEX_PRIORITARIAS.md`. |
| Crítica | Fonte | Só `LDP (4)` e `Programação Semanal` estão mapeadas. Não há fontes equivalentes para WBX, Assignment, AssignmentByDay, dCalendario/dSemana. | Os três Resumos e as curvas 3D não possuem base suficiente para paridade numérica. | `dados/entidades.ts`. |
| Alta | LDP tabelas | As tabelas incluem `KPI[Adh]`, mas a medida não está registrada. | A tabela inteira pode ser marcada pendente pelo `campoSuportado`. | Visual IDs listados no relatório; `dados/motor.ts`. |
| Alta | Resumo 60% | O filtro original é `NOT IN` de sete valores, não `Level 4 = Engineering - 60%`. | Outros valores não excluídos também entram; simplificação altera totais. | `p04/page.json`. |
| Alta | Resumo 90% | O filtro original também é por exclusão, não igualdade. | Simplificação altera totais e curvas. | `p05/page.json`. |
| Alta | LDP 3D 90% | O filtro não exclui `AT`; no cache, uma linha AT participa do gate 90%. | Forçar `FORMAT = 3D` produz total 6, enquanto o visual original retorna 7. | `p10/page.json`; cache `LDP (4)`. |
| Alta | Cancelados | DAX usa `SITUATION n% = "Canceled"`; há também `CANCELED` e `CANCELLED` em outras colunas. | Normalizar grafia ou usar status geral muda o card. | Medidas `CANCELED 30/60/90`. |
| Alta | Programação | O slicer é `WBX[Término]`, enquanto os visuais são de `Programação Semanal`; o motor atual não propaga esse contexto. | Selecionar período pode não alterar cards/gráfico/tabela como no PBIX. | `p17`, visual `ac7de2b1978e37d8b854`; `motor.ts`. |
| Média | Curvas 3D 60/90 | Os visuais carregam séries 30, 60 e/ou 90 no mesmo gráfico conforme o JSON; selecionar só a medida do gate nominal perde séries comparativas. | Gráfico visualmente incompleto. | IDs `9bd852d7dad89a36ea49` e `2c9e8683b347b30efac7`. |
| Média | Interações | O PBIR contém segmentações e filtros por visual; os testes atuais cobrem estrutura e um caso LDP 30, mas não paridade dos 317 visuais. | Regressões numéricas podem passar pelos testes atuais. | `__tests__/biChs001.test.ts`. |

## Checklist prático por página

- Resumo 30%: implementar todas as medidas KPI 30; validar 10 cards, rosca, curva e matriz com data/Disciplina/Responsabilidade/Pacote.
- Resumo 60%: preservar o filtro por exclusão; a curva contém séries 30 e 60; validar BLANK futuro e semana do corte.
- Resumo 90%: preservar o filtro por exclusão; `Weekly Complete` e `Weekly Deviation` são medidas textuais formatadas.
- LDP Engineering 30/60/90: conferir total e quatro cards contra a tabela de controle; usar status/situação específicos do gate.
- LDP 3D 30/60/90: não generalizar o filtro FORMAT; manter curva adicional e slicer de data.
- Programação: validar 148/28/26/5 no cache, relacionamento com LDP e a categoria AUTORIZATION, cuja grafia vem da fonte.

## O que já está confirmado

- Os 317 IDs e coordenadas da fonte estão presentes na extração gerada.
- O dispatcher possui componentes para todos os tipos usados nestas dez páginas.
- Os filtros simples `In`, `Not`, comparações e BLANK têm implementação e testes unitários.
- A divergência principal é semântica/numérica: medidas, fontes e propagação de relacionamento.

## Critério para retirar as pendências

Executar cada visual de dados com o cache do PBIX e comparar o resultado do motor com os valores do modelo; depois repetir com o projeto CHS001 do banco, registrando a data de atualização. Não declarar paridade apenas porque o layout e os tipos coincidem.
