"""Gera os três entregáveis da auditoria a partir do snapshot somente leitura."""
import collections
import json
import pathlib

ROOT = pathlib.Path(__file__).parents[2]
SNAP = json.loads((ROOT / "_build_verify/bi_chs001_auditoria.json").read_text(encoding="utf-8"))
OUT = ROOT / "_bi_chs001_replica"
STATIC = {"shape", "image", "actionButton", "textbox"}


def ref(v):
    return f"`{v['source']}`"


def fields(v):
    return "; ".join(f"{r['kind']} `{r['table']}[{r['field']}]`" for r in v["refs"]) or "—"


def counts(page):
    c = collections.Counter(v["type"] for v in page["visuals"])
    return ", ".join(f"{k} ({c[k]})" for k in sorted(c))


audit = [
"# Auditoria Codex — dez páginas prioritárias do BI CHS001", "",
"> Escopo: auditoria técnica independente. Nenhum arquivo do frontend do Claude, dado SQL ou PBIX foi modificado.", "",
"## Fontes e método", "",
f"- PBIX: `{SNAP['pbix']}`.",
"- PBIR extraído: `F:\\BI N&A CONNECT\\CHS001-000-PfM0-RT-002-01\\Report\\definition`.",
"- Modelo semântico: `DataModel` do PBIX, aberto em modo somente leitura com PBIXRay 0.15.5.",
"- Resultado: 10 páginas, 317 visuais, 77 medidas efetivamente referenciadas e 16 relacionamentos do modelo.",
"- A igualdade de IDs/coordenadas do JSON confirma estrutura; não confirma cálculo nem renderização. As fórmulas deste relatório vêm do modelo semântico, não de inferência.", "",
"## Regras transversais comprovadas", "",
"- Os Resumos usam `WBX[Level 4]` para separar Engineering 30/60/90. O filtro de 60% é uma exclusão de sete valores e não uma igualdade simples; o de 90% também é uma exclusão.",
"- Planejado e realizado são ponderados por trabalho/HH. As curvas não são médias simples de percentuais por tarefa.",
"- Os cards de LDP contam linhas de `LDP (4)` dentro do gate e usam `DOCUMENT STATUS n%` ou `SITUATION n%`; o status geral não substitui o status do gate.",
"- `CANCELED n` compara exatamente a situação à grafia `Canceled`. Registros com `CANCELLED` em outra coluna não entram automaticamente.",
"- `Programação Semanal[N&A DOCUMENT NUMBER]` relaciona-se 1:1 e bidirecionalmente com `LDP (4)[N&A DOCUMENT NUMBER]`; ignorar essa propagação muda cards, gráfico e tabela.",
"- `AssignmentByDay[TaskUID]` filtra `WBX[NodeKey]`, e `AssignmentByDay[TimeByDay]` filtra `dCalendario[Data]`. Esses caminhos sustentam o realizado semanal/acumulado.", "",
"## Valores de controle disponíveis no cache CHS001", "",
"| Página | Total | Issued | Canceled | Client review | Addressing comments |", "|---|---:|---:|---:|---:|---:|",
"| LDP - 3D - 30% | 4 | 4 | 0 | 0 | 0 |",
"| LDP - 3D - 60% | 7 | 0 | 1 | 0 | 0 |",
"| LDP - 3D - 90% | 7 | 0 | 0 | 0 | 0 |",
"| LDP - Engineering - 30% | 181 | 172 | 0 | 0 | 0 |",
"| LDP - Engineering - 60% | 357 | 129 | 75 | 129 | 0 |",
"| LDP - Engineering - 90% | 166 | 0 | 0 | 0 | 0 |", "",
"Esses números foram calculados sobre as 442 linhas de `LDP (4)` do cache embutido, aplicando os filtros de página e as medidas DAX originais. Servem para comparar o PBIX; não devem ser tratados como estado atual do banco.", "",
"A Programação contém 148 atividades, todas na fase textual `0.6`: NOT STARTED 28, ELABORATION 26, VERIFICATION 49, AUTORIZATION 40 e ISSUANCE FLOW 5.", "",
]

for p in SNAP["pages"]:
    audit += [f"## {p['name']}", "", f"Página {p['ordinal']}, ID `{p['id']}`, {p['width']}×{p['height']}; {len(p['visuals'])} visuais. Fonte: `{p['source']}`.", "", f"Tipos: {counts(p)}.", ""]
    # Practical page filter description
    names = p["name"]
    if names == "Resumo 30%": audit.append("Filtro: `WBX[Level 4] = \"Engineering - 30%\"`; `WBX[Pacote]` está no painel sem condição persistida.")
    elif names == "Resumo 60%": audit.append("Filtro: `WBX[Level 4] NOT IN {Engineering - 30%, Engineering - 90%, Field Activies, Milestone, WFX Deliverables, Engineering - 15%, CHS Deliverables}`.")
    elif names == "Resumo 90%": audit.append("Filtro: `WBX[Level 4] NOT IN {Engineering - 30%, Engineering - 60%, Field Activies, Milestone, WFX Deliverables, Engineering - 15%, CHS Deliverables}`.")
    elif "LDP - 3D" in names:
        gate = names[-3:-1]
        exclusions = {"30": "A, AT, B, D, null, A1", "60": "A, B, D, AT, null", "90": "A, B, D, null"}[gate]
        audit.append(f"Filtro: `LDP (4)[FORMAT] NOT IN {{{exclusions}}}`. O conjunto remanescente não deve ser simplificado para `FORMAT = 3D` sem verificar os valores do cache.")
    elif "LDP - Engineering" in names:
        gate = names[-3:-1]
        excluded = "null, AT, 3D" if gate == "90" else "null, 3D"
        audit.append(f"Filtros: `FORMAT NOT IN {{{excluded}}}` e `Fase {gate}% <> BLANK()`.")
    else: audit.append("Sem filtro de página persistido; os quatro cards têm filtros próprios por `DOCUMENT STATUS`, e o slicer usa `WBX[Término]`.")
    audit += ["", "### Visuais de dados", "", "| ID | Tipo | Título | Campos/medidas | Evidência |", "|---|---|---|---|---|"]
    for v in p["visuals"]:
        if v["type"] in STATIC: continue
        title = " / ".join(v["titles"]).replace("|", "\\|") or "—"
        audit.append(f"| `{v['id']}` | {v['type']} | {title} | {fields(v).replace('|','/')} | {ref(v)} |")
    audit += ["", "### Inventário completo (ID, tipo e coordenadas)", "", "| ID | Tipo | X | Y | L | A | Z |", "|---|---|---:|---:|---:|---:|---:|"]
    for v in sorted(p["visuals"], key=lambda x: ((x["position"].get("z") or 0), x["id"])):
        pos=v["position"]
        audit.append(f"| `{v['id']}` | {v['type']} | {pos.get('x')} | {pos.get('y')} | {pos.get('width')} | {pos.get('height')} | {pos.get('z')} |")
    audit.append("")

audit += ["## Relacionamentos que afetam as dez páginas", "", "| De | Para | Ativo | Cardinalidade | Direção |", "|---|---|---:|---|---|"]
for r in SNAP["relationships"]:
    audit.append(f"| `{r['FromTableName']}[{r['FromColumnName']}]` | `{r['ToTableName']}[{r['ToColumnName']}]` | {r['IsActive']} | {r['Cardinality']} | {r['CrossFilteringBehavior']} |")
audit += ["", "## Limites desta evidência", "", "O PBIX não foi renderizado no Power BI Desktop nesta auditoria. Cores, coordenadas, fontes e conteúdos configurados estão comprovados pelo PBIR; a comparação pixel a pixel ainda exige captura do Desktop na mesma seleção de filtros da aplicação. Os valores de controle são do cache do PBIX e podem divergir legitimamente do banco atual por data de atualização.", ""]
(OUT / "AUDITORIA_CODEX_PRIORITARIAS.md").write_text("\n".join(audit), encoding="utf-8")

# Complete DAX map, exact text from the embedded semantic model.
usage = collections.defaultdict(list)
for p in SNAP["pages"]:
    for v in p["visuals"]:
        for r in v["refs"]:
            if r["kind"] == "Measure": usage[(r["table"], r["field"])].append(f"{p['name']} / `{v['id']}`")
dax = ["# Mapa de medidas DAX — BI CHS001", "", "> Fórmulas extraídas do DataModel do PBIX original. Não são aproximações do frontend.", "", f"Total referenciado pelas dez páginas: {len(SNAP['measures'])} medidas.", ""]
for m in sorted(SNAP["measures"], key=lambda x:(x["TableName"],x["Name"])):
    dax += [f"## {m['TableName']} — {m['Name']}", "", "Uso: " + "; ".join(sorted(set(usage[(m['TableName'],m['Name'])]))) + ".", "", "```DAX", (m.get("Expression") or "").strip(), "```", ""]
dax += ["## Observações de implementação", "", "- `ALLSELECTED(WBX)` preserva segmentações do usuário; substituí-lo por todas as linhas muda o denominador.", "- Medidas `CURVA` são acumuladas; `COLUNA` é o incremento da semana.", "- O realizado é limitado pela data de corte e retorna BLANK para semanas futuras.", "- As medidas 3D usam `TREATAS` dos números de documento de `LDP (4)` para `WBX[NA DOCUMENT NUMBER]`.", "- Há medidas legadas referenciadas somente como filtros sem condição; não confundir presença no JSON com série ativa.", ""]
(OUT / "MAPA_MEDIDAS_DAX.md").write_text("\n".join(dax), encoding="utf-8")

div = [
"# Divergências — réplica BI CHS001", "", "> Leitura do frontend atual; nenhuma correção foi aplicada por Codex.", "",
"## Prioridade crítica para o Claude", "",
"| Gravidade | Área | Divergência comprovada | Efeito | Evidência |", "|---|---|---|---|---|",
"| Crítica | Resumos 30/60/90 | O registro `MEDIDAS` implementa apenas contagens de LDP por gate; as medidas KPI de prazo, ACC, semana e curva usadas nos três Resumos não estão registradas. | Cards, curva, matriz e percentuais ficam `PENDENTE`/BLANK, mesmo com dados carregados. | `ClientApp/src/features/production/biChs001/dados/medidas.ts`; 77 medidas requeridas em `MAPA_MEDIDAS_DAX.md`. |",
"| Crítica | Modelo | O motor declara que filtros de outra entidade não restringem o visual e não propaga relacionamentos. | Realizado de `AssignmentByDay`, filtros WBX e Programação↔LDP não reproduzem o Power BI. | `dados/motor.ts`, função `linhasFiltradas`; 16 relações em `AUDITORIA_CODEX_PRIORITARIAS.md`. |",
"| Crítica | Fonte | Só `LDP (4)` e `Programação Semanal` estão mapeadas. Não há fontes equivalentes para WBX, Assignment, AssignmentByDay, dCalendario/dSemana. | Os três Resumos e as curvas 3D não possuem base suficiente para paridade numérica. | `dados/entidades.ts`. |",
"| Alta | LDP tabelas | As tabelas incluem `KPI[Adh]`, mas a medida não está registrada. | A tabela inteira pode ser marcada pendente pelo `campoSuportado`. | Visual IDs listados no relatório; `dados/motor.ts`. |",
"| Alta | Resumo 60% | O filtro original é `NOT IN` de sete valores, não `Level 4 = Engineering - 60%`. | Outros valores não excluídos também entram; simplificação altera totais. | `p04/page.json`. |",
"| Alta | Resumo 90% | O filtro original também é por exclusão, não igualdade. | Simplificação altera totais e curvas. | `p05/page.json`. |",
"| Alta | LDP 3D 90% | O filtro não exclui `AT`; no cache, uma linha AT participa do gate 90%. | Forçar `FORMAT = 3D` produz total 6, enquanto o visual original retorna 7. | `p10/page.json`; cache `LDP (4)`. |",
"| Alta | Cancelados | DAX usa `SITUATION n% = \"Canceled\"`; há também `CANCELED` e `CANCELLED` em outras colunas. | Normalizar grafia ou usar status geral muda o card. | Medidas `CANCELED 30/60/90`. |",
"| Alta | Programação | O slicer é `WBX[Término]`, enquanto os visuais são de `Programação Semanal`; o motor atual não propaga esse contexto. | Selecionar período pode não alterar cards/gráfico/tabela como no PBIX. | `p17`, visual `ac7de2b1978e37d8b854`; `motor.ts`. |",
"| Média | Curvas 3D 60/90 | Os visuais carregam séries 30, 60 e/ou 90 no mesmo gráfico conforme o JSON; selecionar só a medida do gate nominal perde séries comparativas. | Gráfico visualmente incompleto. | IDs `9bd852d7dad89a36ea49` e `2c9e8683b347b30efac7`. |",
"| Média | Interações | O PBIR contém segmentações e filtros por visual; os testes atuais cobrem estrutura e um caso LDP 30, mas não paridade dos 317 visuais. | Regressões numéricas podem passar pelos testes atuais. | `__tests__/biChs001.test.ts`. |", "",
"## Checklist prático por página", "",
"- Resumo 30%: implementar todas as medidas KPI 30; validar 10 cards, rosca, curva e matriz com data/Disciplina/Responsabilidade/Pacote.",
"- Resumo 60%: preservar o filtro por exclusão; a curva contém séries 30 e 60; validar BLANK futuro e semana do corte.",
"- Resumo 90%: preservar o filtro por exclusão; `Weekly Complete` e `Weekly Deviation` são medidas textuais formatadas.",
"- LDP Engineering 30/60/90: conferir total e quatro cards contra a tabela de controle; usar status/situação específicos do gate.",
"- LDP 3D 30/60/90: não generalizar o filtro FORMAT; manter curva adicional e slicer de data.",
"- Programação: validar 148/28/26/5 no cache, relacionamento com LDP e a categoria AUTORIZATION, cuja grafia vem da fonte.", "",
"## O que já está confirmado", "", "- Os 317 IDs e coordenadas da fonte estão presentes na extração gerada.", "- O dispatcher possui componentes para todos os tipos usados nestas dez páginas.", "- Os filtros simples `In`, `Not`, comparações e BLANK têm implementação e testes unitários.", "- A divergência principal é semântica/numérica: medidas, fontes e propagação de relacionamento.", "",
"## Critério para retirar as pendências", "", "Executar cada visual de dados com o cache do PBIX e comparar o resultado do motor com os valores do modelo; depois repetir com o projeto CHS001 do banco, registrando a data de atualização. Não declarar paridade apenas porque o layout e os tipos coincidem.", ""]
(OUT / "DIVERGENCIAS_BI_CHS001.md").write_text("\n".join(div), encoding="utf-8")

print("written:", *(p.name for p in [OUT/'AUDITORIA_CODEX_PRIORITARIAS.md', OUT/'MAPA_MEDIDAS_DAX.md', OUT/'DIVERGENCIAS_BI_CHS001.md']))
