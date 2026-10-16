"""Extrai (SOMENTE LEITURA) o estado de expansão salvo das matrizes (pivotTable) do PBIR original.

Uso: python -I extrair_expansao_matrizes.py "<pasta PBIR>" <saida.json>
Saída: { "<id do visual>": { "niveis": [queryRef...], "recolhidoPorPadrao": [bool...], "abertos": [[valor nível 1, valor nível 2...], ...] } }
"abertos" = caminhos com isToggled (o nó foi aberto/fechado em relação ao padrão do nível).
"""
import json
import sys
from pathlib import Path


def literal(v):
    s = v.get("Literal", {}).get("Value")
    if isinstance(s, str) and len(s) >= 2 and s[0] == "'" and s[-1] == "'":
        return s[1:-1].replace("''", "'")
    return s


def caminhos(no, prefixo, out):
    for filho in no.get("children", []) or []:
        valores = [literal(x) for x in filho.get("identityValues", [])]
        caminho = prefixo + valores
        if filho.get("isToggled"):
            out.append(caminho)
        caminhos(filho, caminho, out)


def main(pasta, saida):
    res = {}
    for arq in Path(pasta).rglob("visual.json"):
        j = json.loads(arq.read_text(encoding="utf-8"))
        v = j.get("visual", {})
        if v.get("visualType") != "pivotTable":
            continue
        for es in v.get("expansionStates", []) or []:
            if es.get("roles") != ["Rows"]:
                continue
            abertos = []
            caminhos(es.get("root", {}), [], abertos)
            res[j["name"]] = {
                "niveis": [lv.get("queryRefs", [None])[0] for lv in es.get("levels", [])],
                "recolhidoPorPadrao": [bool(lv.get("isCollapsed")) for lv in es.get("levels", [])],
                "abertos": abertos,
            }
    Path(saida).write_text(json.dumps(res, ensure_ascii=False, indent=1), encoding="utf-8")
    print(json.dumps(res, ensure_ascii=False)[:3000])


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
