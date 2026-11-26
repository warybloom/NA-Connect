"""Gera layouts normalizados do PBIX BML011.016 sem copiar recursos para o bundle."""
import importlib.util
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "../../../../../.."))
BASE = os.path.join(ROOT, "_bi_chs001_replica", "tools", "extrair_layout.py")
spec = importlib.util.spec_from_file_location("extrator_bml", BASE)
mod = importlib.util.module_from_spec(spec)
assert spec.loader
spec.loader.exec_module(mod)
mod.ORIGEM = os.path.join(ROOT, "_bi_bml011016", "original", "Report")
mod.DEF = os.path.join(mod.ORIGEM, "definition")
mod.DESTINO = os.path.join(ROOT, "ClientApp", "src", "features", "production", "biBml011016", "generated")
mod.tema = json.load(open(os.path.join(mod.ORIGEM, "StaticResources", "SharedResources", "BaseThemes", "CY24SU10.json"), encoding="utf-8"))
mod.CORES_TEMA = ["#FFFFFF", "#000000"] + mod.tema["dataColors"]
mod.RECURSOS = {}
mod.main()
indice = os.path.join(mod.DESTINO, "indice.json")
conteudo = json.load(open(indice, encoding="utf-8"))
conteudo["fonte"] = "BML011.016.pbix — SHA256 448947F79CB8F9C20A45246B8FE222B58097C415D8B450D3E83E1BEEB862E634"
with open(indice, "w", encoding="utf-8") as arquivo:
    json.dump(conteudo, arquivo, ensure_ascii=False, indent=1)
