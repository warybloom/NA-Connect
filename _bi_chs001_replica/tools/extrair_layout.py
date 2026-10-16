# Extrai (SOMENTE LEITURA) a definição do relatório Power BI CHS001 (formato PBIR) e grava um layout normalizado por página
# em ClientApp/src/features/production/biChs001/generated/. Nada é escrito na pasta de origem.
#   python _bi_chs001_replica/tools/extrair_layout.py
# Normalização: expressões Literal/ThemeDataColor/ResourcePackageItem viram valores simples (string, número, bool, cor hex);
# expressões não literais (formatação condicional por medida etc.) ficam em {"expr": ...} para tratamento explícito.
import colorsys
import json
import os
import re
import unicodedata

ORIGEM = r"F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report"
DEF = os.path.join(ORIGEM, "definition")
RAIZ = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DESTINO = os.path.join(RAIZ, "ClientApp", "src", "features", "production", "biChs001", "generated")

tema = json.load(open(os.path.join(ORIGEM, "StaticResources", "SharedResources", "BaseThemes", "CY24SU10.json"), encoding="utf-8"))
CORES_TEMA = ["#FFFFFF", "#000000"] + tema["dataColors"]

# Recursos do PBIX -> asset do módulo. LOGO_NA = logo CHS/Whitefox substituída pela logo oficial N&A (única exceção).
RECURSOS = {
    "azfszdfsaa05038555710675152.png": "fundo", "azfszdfsaa17605422379070523.png": "fundo", "azfszdfsaa8787839910429552.png": "fundo",
    "casa8408699405427408.png": "casa", "SCHEDULE9820500175684618.png": "schedule", "Start8922201642400366.png": "start",
    "imagem_(1)0030942338025109484.png": "capaPlanta", "NA_USA9095526283384914.png": "naUsa",
    "Imagem2_CHS24845378456814537.jpg": "LOGO_NA", "Imagem_WFX05116637210839181.png": "LOGO_NA",
}


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


def cor_tema(cid, pct):
    base = CORES_TEMA[cid] if cid < len(CORES_TEMA) else "#000000"
    if not pct:
        return base.upper()
    r, g, b = hex_rgb(base)
    hh, lum, s = colorsys.rgb_to_hls(r, g, b)
    # Power BI: percent < 0 escurece (fração da luminosidade), > 0 clareia em direção ao branco.
    lum = lum * (1 + pct) if pct < 0 else lum + (1 - lum) * pct
    r, g, b = colorsys.hls_to_rgb(hh, max(0, min(1, lum)), s)
    return "#%02X%02X%02X" % (round(r * 255), round(g * 255), round(b * 255))


def literal(v):
    if not isinstance(v, str):
        return v
    if v.startswith("'") and v.endswith("'"):
        return v[1:-1].replace("''", "'")
    if v in ("true", "false"):
        return v == "true"
    if v == "null":
        return None
    m = re.fullmatch(r"(-?\d+(?:\.\d+)?)(D|L|M)?", v)
    if m:
        n = float(m.group(1))
        return int(n) if n.is_integer() else n
    m = re.fullmatch(r"datetime'([^']*)'", v)
    if m:
        return m.group(1)
    return v


def resolver(x):
    if isinstance(x, dict):
        if set(x) == {"expr"}:
            e = x["expr"]
            if "Literal" in e:
                return literal(e["Literal"]["Value"])
            if "ThemeDataColor" in e:
                return cor_tema(e["ThemeDataColor"]["ColorId"], e["ThemeDataColor"].get("Percent", 0))
            if "ResourcePackageItem" in e:
                return {"recurso": RECURSOS.get(e["ResourcePackageItem"]["ItemName"], e["ResourcePackageItem"]["ItemName"])}
            return {"expr": e}
        if set(x) == {"solid"}:
            return resolver(x["solid"]["color"])
        return {k: resolver(v) for k, v in x.items()}
    if isinstance(x, list):
        return [resolver(v) for v in x]
    return x


def objetos(objs):
    out = {}
    for nome, arr in (objs or {}).items():
        lst = []
        for e in arr:
            item = {"p": resolver(e.get("properties", {}))}
            if "selector" in e:
                item["s"] = e["selector"]
            lst.append(item)
        out[nome] = lst
    return out


def campo(f):
    """Projeção de campo -> {entity, prop, kind, fn?}."""
    if not f:
        return {}
    k = next(iter(f))
    v = f[k]
    if k in ("Column", "Measure"):
        return {"entity": v["Expression"]["SourceRef"].get("Entity"), "prop": v["Property"], "kind": k.lower()}
    if k == "Aggregation":
        inner = campo(v["Expression"])
        inner.update({"kind": "aggregation", "fn": v["Function"]})
        return inner
    if k == "HierarchyLevel":
        h = v["Expression"]["Hierarchy"]
        return {"entity": h["Expression"]["SourceRef"].get("Entity"), "prop": v["Level"], "hierarchy": h["Hierarchy"], "kind": "hierarchyLevel"}
    return {"kind": k, "raw": v}


def consulta(q):
    out = {}
    for papel, proj in ((q or {}).get("queryState") or {}).items():
        out[papel] = [dict(campo(p.get("field")), queryRef=p.get("queryRef"), nativeQueryRef=p.get("nativeQueryRef"),
                           displayName=p.get("displayName"), active=p.get("active")) for p in proj.get("projections", [])]
    return out


def visual(vj):
    p = vj["position"]
    vis = vj.get("visual") or {}
    out = {
        "id": vj["name"], "type": vis.get("visualType") or ("group" if "visualGroup" in vj else "?"),
        "x": round(p.get("x", 0), 2), "y": round(p.get("y", 0), 2), "w": round(p.get("width", 0), 2), "h": round(p.get("height", 0), 2),
        "z": p.get("z", 0),
    }
    if vj.get("isHidden"):
        out["hidden"] = True
    if vj.get("parentGroupName"):
        out["parent"] = vj["parentGroupName"]
    if "visualGroup" in vj:
        out["group"] = resolver(vj["visualGroup"])
    o = objetos(vis.get("objects"))
    c = objetos(vis.get("visualContainerObjects"))
    if o:
        out["o"] = o
    if c:
        out["c"] = c
    q = consulta(vis.get("query"))
    if q:
        out["fields"] = q
        srt = ((vis.get("query") or {}).get("sortDefinition") or {}).get("sort")
        if srt:
            out["sort"] = [dict(campo(s.get("field")), direction=s.get("direction")) for s in srt]
    if vj.get("filterConfig"):
        out["filters"] = resolver(vj["filterConfig"])
    return out


def slug(nome):
    s = unicodedata.normalize("NFKD", nome).encode("ascii", "ignore").decode().lower()
    s = s.replace("%", "")
    return re.sub(r"[^a-z0-9]+", "-", s).strip("-")


def main():
    os.makedirs(DESTINO, exist_ok=True)
    ordem = json.load(open(os.path.join(DEF, "pages", "pages.json"), encoding="utf-8"))["pageOrder"]
    indice = []
    for i, pid in enumerate(ordem, 1):
        pj = json.load(open(os.path.join(DEF, "pages", pid, "page.json"), encoding="utf-8"))
        vd = os.path.join(DEF, "pages", pid, "visuals")
        visuais = []
        if os.path.isdir(vd):
            for v in sorted(os.listdir(vd)):
                visuais.append(visual(json.load(open(os.path.join(vd, v, "visual.json"), encoding="utf-8"))))
        visuais.sort(key=lambda v: (v["z"], v["id"]))
        pagina = {
            "ordem": i, "id": pid, "nome": pj["displayName"], "slug": slug(pj["displayName"]),
            "largura": pj["width"], "altura": pj["height"], "displayOption": pj.get("displayOption"),
            "objetos": objetos(pj.get("objects")), "filtros": resolver(pj.get("filterConfig")),
            "interacoes": pj.get("visualInteractions", []), "visuais": visuais,
        }
        arq = "p%02d.json" % i
        with open(os.path.join(DESTINO, arq), "w", encoding="utf-8") as f:
            json.dump(pagina, f, ensure_ascii=False, separators=(",", ":"))
        indice.append({"ordem": i, "id": pid, "nome": pj["displayName"], "slug": pagina["slug"], "arquivo": arq, "visuais": len(visuais)})
    # Bookmarks (só leitura): nome + página ativa + estado de exibição dos visuais.
    bm = []
    bd = os.path.join(DEF, "bookmarks")
    if os.path.isdir(bd):
        for f in sorted(os.listdir(bd)):
            if f.endswith(".bookmark.json"):
                b = json.load(open(os.path.join(bd, f), encoding="utf-8"))
                st = b.get("explorationState") or {}
                secs = st.get("sections") or {}
                bm.append({
                    "id": b.get("name"), "nome": b.get("displayName"), "pagina": st.get("activeSection"),
                    "ocultos": {sec: sorted(vid for vid, vc in (s.get("visualContainers") or {}).items()
                                            if ((vc.get("singleVisual") or {}).get("display") or {}).get("mode") == "hidden")
                                for sec, s in secs.items()},
                })
    with open(os.path.join(DESTINO, "indice.json"), "w", encoding="utf-8") as f:
        json.dump({"fonte": "CHS001-000-PM0-RT-002-01.pbix (PBIR extraído)", "paginas": indice, "bookmarks": bm}, f, ensure_ascii=False, indent=1)
    print("paginas:", len(indice), "->", DESTINO)


if __name__ == "__main__":
    main()
