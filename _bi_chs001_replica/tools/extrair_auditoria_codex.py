"""Extrai evidências somente leitura das dez páginas prioritárias e do modelo PBIX."""
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).parents[2] / "_build_verify" / "pbixray_lib"))
from pbixray import PBIXRay

ROOT = pathlib.Path(__file__).parents[2]
PAGES = pathlib.Path(r"F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages")
PBIX = pathlib.Path(r"F:\BI N&A CONNECT\CHS001-000-PM0-RT-002-01.pbix")
OUT = ROOT / "_build_verify" / "bi_chs001_auditoria.json"
NAMES = [
    "Resumo 30%", "Resumo 60%", "Resumo 90%",
    "LDP - Engineering - 30%", "LDP - Engineering - 60%", "LDP - Engineering - 90%",
    "LDP - 3D - 30%", "LDP - 3D - 60%", "LDP - 3D - 90%", "Programação",
]


def refs(node, result):
    if isinstance(node, dict):
        for key, value in node.items():
            if key in ("Measure", "Column", "Aggregation") and isinstance(value, dict):
                prop = value.get("Property")
                expr = value.get("Expression", {})
                if key == "Aggregation":
                    inner = expr.get("Column", {})
                    prop = inner.get("Property")
                    expr = inner.get("Expression", {})
                source = expr.get("SourceRef", {})
                if prop:
                    result.add((key, source.get("Entity") or source.get("Source"), prop))
            refs(value, result)
    elif isinstance(node, list):
        for value in node:
            refs(value, result)


def literal_text(node):
    texts = []
    if isinstance(node, dict):
        for key, value in node.items():
            if key in ("text", "title") and isinstance(value, dict):
                raw = ((value.get("expr") or {}).get("Literal") or {}).get("Value")
                if raw:
                    texts.append(raw.strip("'"))
            literal_text(value) if False else None
            texts.extend(literal_text(value))
    elif isinstance(node, list):
        for value in node:
            texts.extend(literal_text(value))
    return texts


order = json.loads((PAGES / "pages.json").read_text(encoding="utf-8"))["pageOrder"]
pages = []
used_measures = set()
used_tables = set()
for ordinal, page_id in enumerate(order, 1):
    page_file = PAGES / page_id / "page.json"
    page = json.loads(page_file.read_text(encoding="utf-8"))
    if page.get("displayName") not in NAMES:
        continue
    page_refs = set()
    refs(page.get("filterConfig"), page_refs)
    visuals = []
    for visual_file in sorted((PAGES / page_id / "visuals").glob("*/visual.json")):
        value = json.loads(visual_file.read_text(encoding="utf-8"))
        visual = value.get("visual") or {}
        visual_refs = set()
        refs(value, visual_refs)
        for kind, table, prop in visual_refs:
            if table:
                used_tables.add(table)
            if kind == "Measure" and table:
                used_measures.add((table, prop))
        position = value.get("position") or {}
        visuals.append({
            "id": value.get("name"),
            "source": str(visual_file),
            "type": visual.get("visualType") or ("group" if "visualGroup" in value else "unknown"),
            "position": {key: position.get(key) for key in ("x", "y", "width", "height", "z")},
            "hidden": bool(value.get("isHidden")),
            "titles": sorted(set(literal_text(visual.get("objects", {})) + literal_text(visual.get("visualContainerObjects", {})))),
            "refs": [{"kind": k, "table": t, "field": p} for k, t, p in sorted(visual_refs, key=str)],
            "filters": value.get("filterConfig"),
            "query": visual.get("query"),
        })
    pages.append({
        "ordinal": ordinal, "id": page_id, "name": page["displayName"],
        "source": str(page_file), "width": page.get("width"), "height": page.get("height"),
        "filters": page.get("filterConfig"),
        "filter_refs": [{"kind": k, "table": t, "field": p} for k, t, p in sorted(page_refs, key=str)],
        "visuals": visuals,
    })

pages.sort(key=lambda page: NAMES.index(page["name"]))

model = PBIXRay(str(PBIX))
try:
    measure_rows = model.dax_measures.to_dict("records")
    measures = [row for row in measure_rows if (row["TableName"], row["Name"]) in used_measures]
    relationships = model.relationships.to_dict("records")
    schema = model.schema.to_dict("records")
    mashup = model.power_query.to_dict("records") if hasattr(model.power_query, "to_dict") else []
finally:
    model.close()

OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(json.dumps({
    "pbix": str(PBIX), "pages": pages, "measures": measures,
    "relationships": relationships, "schema": schema, "power_query": mashup,
}, ensure_ascii=False, indent=2, default=str), encoding="utf-8")
print(f"pages={len(pages)} visuals={sum(len(p['visuals']) for p in pages)} measures={len(measures)} relationships={len(relationships)}")
print(OUT)
