# Migrations — Fundação Gestão à Vista (FASE B + correção "abas internas da LDP")

**NÃO EXECUTADO NESTA SESSÃO.** Estes scripts foram projetados (FASE B3/B4, revisados após a auditoria
real das abas internas da LDP) a partir de `AUDITORIA_FORGESYNC_NA/MATRIZ_GESTAO_A_VISTA.md`,
`MAPA_DADOS_LDP_FINANCEIRO.md` e `GESTAO_VISTA_MODELO_DADOS.md`. Leia esses três documentos antes de
decidir executar qualquer coisa aqui — documentam exatamente quais fórmulas são reais (extraídas do
código do ForgeSync E da própria LDP `.xlsx`) e quais indicadores (SPI/CPI/EAC/ETC/VAC) **não têm fonte
comprovada ainda** e por isso não foram criados.

`dbo.VW_Custo_Diario_Documento` (nunca confirmada como existente no `DB_NA_CONNECT` real) **foi
descartada como dependência** — a versão atual do `04` não depende mais dela. A fonte de HH/Custo real
agora é `gestao.TimesheetLancamento`, alimentada pelo extrator a partir das abas Timesheet/Custo Real/
Custo profissionais da LDP (fonte comprovada por auditoria real do arquivo, não hipotética).

## Ordem de execução manual (se e quando o usuário decidir aplicar)

1. `01_criar_schema_gestao.sql` — cria o schema `[gestao]`. Pré-requisito de todos os demais.
2. `02_criar_snapshot_diario_documento.sql` — cria uma TABELA FÍSICA (`[gestao].Snapshot_Diario_Documento`)
   para começar a acumular histórico diário de LDP a partir do dia em que passar a ser alimentada. Sem
   histórico anterior — Curva S baseada nela só terá dados a partir da data de início da alimentação.
   Precisa de um job diário (fora do escopo desta sessão) para ser populada; a migration só cria a
   estrutura.
3. `03_criar_views_ldp_gestao_vista.sql` — views 100% baseadas em `dbo.VW_Ldp_Unificada` (fonte já
   comprovada e em uso na ETAPA A) — SEM depender de nenhuma fonte não confirmada. Pode ser executada
   independentemente das demais.
4. `05_criar_tabela_timesheet_lancamento.sql` — tabela física `gestao.TimesheetLancamento` (fonte: aba
   Timesheet da LDP). Precisa do extrator (`Services/GestaoVista/*`, fora do escopo de migration) para ser
   populada.
5. `06_criar_tabela_custo_real_historico.sql` — tabela física `gestao.CustoProfissionalRealHistorico`
   (SCD tipo 2 — histórico obrigatório de custo/hora por pessoa) + view `gestao.vw_Custo_Real_Vigente`
   (fonte: aba Custo Real da LDP).
6. `07_criar_tabela_custo_profissional_previsto.sql` — tabela física `gestao.CustoProfissionalPrevisto`
   (custo de referência por função/disciplina — NUNCA cruzar com a tabela de custo real; fonte: aba Custo
   profissionais da LDP).
7. `04_criar_view_financeiro_diario_gestao.sql` — **reescrito**: depende de `05` existir. Cria
   `gestao.vw_Financeiro_Documento`/`vw_Financeiro_Disciplina` sobre `gestao.TimesheetLancamento` +
   `dbo.VW_Ldp_Unificada` — sem depender de `VW_Custo_Diario_Documento`.

## O que NÃO foi criado (e por quê)

Nenhuma view/tabela de SPI, CPI, SV, CV, EAC, ETC, VAC ou BAC foi criada. Motivo: nenhuma fonte real de
**BAC (orçamento total por documento/disciplina)** foi confirmada nesta investigação — calcular esses
6 indicadores sem BAC seria inventar dado. Um candidato real de BAC financeiro (`Receita_Prevista` da aba
"Planejamento financeiro" × progresso físico mensal) foi encontrado na auditoria da LDP e está
documentado em `MATRIZ_GESTAO_A_VISTA.md` como `CANDIDATO_COM_EVIDENCIA_EXCEL` — não oficializado, não
migrado. Outra candidata a investigar depois: `dbo.TB_Orcamento_Disciplina` (já catalogada no schema do
N&A, não lida/alterada nesta sessão — instrução explícita do usuário). Enquanto isso não for confirmado,
as telas Curva S/Financeiro continuam PENDENTE_BACKEND para esses 6 indicadores especificamente.

`gestao.CustoProfissionalPrevisto` e `gestao.CustoProfissionalRealHistorico` são tabelas DIFERENTES,
deliberadamente nomeadas para nunca serem confundidas — a primeira é custo de referência/planejado, a
segunda é custo real histórico por pessoa.

O bloco "MIX" (percentuais Sênior/Pleno/Júnior dentro da aba Custo profissionais), e as abas Planejamento
financeiro/Histograma/Tabela financeira/Curva financeira, ficaram fora desta rodada de migrations —
registradas como candidatas em `GESTAO_VISTA_MODELO_DADOS.md`.

## Rollback

Cada script tem o bloco de rollback comentado no final (nunca executado automaticamente). Rollback geral
na ordem inversa: 04 → 07 → 06 → 05 → 03 → 02 → 01 (DROP VIEW/TABLE/SCHEMA).
