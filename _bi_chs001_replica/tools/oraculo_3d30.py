"""Oráculo INDEPENDENTE (Python, sem reaproveitar o TypeScript) das medidas KPI 30 | 3D | COLUNA/CURVA | % Realizado da página
LDP - 3D - 30%, transcritas do DAX em MAPA_MEDIDAS_DAX.md, sobre o cache do PBIX (fixtures_pbix). SOMENTE TESTE.

Uso: python -I oraculo_3d30.py  → grava fixtures_pbix/esperado_3d30.json
"""
import json
from datetime import date, timedelta
from pathlib import Path

FIX = Path(__file__).resolve().parent.parent / "fixtures_pbix"
CORTE = date(2026, 10, 8)
EXCLUIDOS_FORMAT = {"A", "AT", "B", "D", None, "A1"}  # filtro da página p08 (NOT IN), PBIR
GATE = "Engineering - 30%"


def d(s):
    return date.fromisoformat(s[:10]) if s else None


def inicio_semana(x):  # domingo
    return x - timedelta(days=(x.weekday() + 1) % 7)


def main():
    ldp = json.loads((FIX / "ldp4.json").read_text(encoding="utf-8"))
    wbx = json.loads((FIX / "wbx.json").read_text(encoding="utf-8"))
    abd = json.loads((FIX / "abd.json").read_text(encoding="utf-8"))

    docs = {str(l["N&A DOCUMENT NUMBER"]).upper() for l in ldp
            if (l.get("FORMAT") if l.get("FORMAT") not in ("",) else None) not in EXCLUIDOS_FORMAT
            and str(l.get("FORMAT") or "").upper() == "3D" and l.get("N&A DOCUMENT NUMBER")}
    gate = [w for w in wbx if w.get("Level 4") == GATE]
    gl = [w for w in gate if not w.get("É Resumo") and str(w.get("NA DOCUMENT NUMBER") or "").upper() in docs]
    chaves = {w["NodeKey"].upper() for w in gl}
    serie = [(d(r["TimeByDay"]), r["Trabalho Real"]) for r in abd if r["TaskUID"].upper() in chaves and r.get("Trabalho Real") is not None]
    lb = sum(w.get("Trab Linha de Base") or 0 for w in gl) if gl else None
    total = sum(w.get("Trabalho Real") or 0 for w in gl) if gl else None

    def soma(cond):
        v = [x for t, x in serie if cond(t)]
        return sum(v) if v else None

    # KPI 30 | CURVA | Mostrar Período (eixo filtrado pelo visual): sobre WBX do gate (sem o TREATAS).
    def ext(col, maior):
        v = [d(w[col]) for w in gate if w.get(col)]
        return (max(v) if maior else min(v)) if v else None
    zero = date(1899, 12, 30)
    dmin = min(ext("Início da Linha de Base", False) or zero, ext("Início", False) or zero)
    dmax = max(ext("Término da Linha de Base", True) or zero, ext("Término", True) or zero)

    pontos = []
    e = date(2025, 1, 2)
    while e <= date(2027, 12, 30):
        s = inicio_semana(e)
        if s + timedelta(days=6) >= dmin and s <= dmax:
            if e > CORTE or not lb:
                col = cur = None
            else:
                ini = e - timedelta(days=6)
                if e == CORTE:
                    semana = max(0, (total or 0) - (soma(lambda t: t < ini) or 0))
                else:
                    semana = soma(lambda t: ini <= t < e + timedelta(days=1))
                col = None if semana is None else semana / lb
                acum = total if e == CORTE else soma(lambda t: t < e + timedelta(days=1))
                cur = min(1, (acum / lb) if acum is not None else 0)  # MIN(1, BLANK()) = 0 no DAX
            pontos.append({"categoria": e.isoformat(), "3D | COLUNA | % Realizado": col, "3D | CURVA | % Realizado": cur})
        e += timedelta(days=7)

    saida = {"documentos3D": sorted(docs), "tarefas3D": len(gl), "trabLinhaBase": lb, "trabalhoReal": total, "pontos": pontos}
    (FIX / "esperado_3d30.json").write_text(json.dumps(saida, ensure_ascii=False, indent=1), encoding="utf-8")
    print(json.dumps({k: v for k, v in saida.items() if k != "pontos"}, ensure_ascii=False), "pontos:", len(pontos),
          "não nulos:", sum(1 for p in pontos if p["3D | COLUNA | % Realizado"] is not None))


if __name__ == "__main__":
    main()
