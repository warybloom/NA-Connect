# Oráculo INDEPENDENTE (Python, transcrição direta do DAX do PBIX) para as medidas de curva/ACC/BASE SEM dos Resumos 30/60/90.
# Lê só o cache do PBIX (fixtures_pbix/wbx.json + abd.json) e grava fixtures_pbix/esperado_curvas.json, usado por
# ClientApp/src/features/production/biChs001/__tests__/paridadePbix.test.ts para conferir o motor TypeScript.
# NÃO é captura do Power BI: é um segundo cálculo escrito separadamente a partir do mesmo DAX (MAPA_MEDIDAS_DAX.md).
# Uso: python -I _bi_chs001_replica/tools/oraculo_curvas.py
import json, math, os
from datetime import date, datetime, timedelta

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures_pbix")
wbx = json.load(open(os.path.join(BASE, "wbx.json"), encoding="utf-8"))
abd = json.load(open(os.path.join(BASE, "abd.json"), encoding="utf-8"))
CORTE = datetime(2026, 10, 8)
FERIADO = date(2026, 5, 1)

def dt(s):
    return None if s in (None, "") else datetime.fromisoformat(s[:19])

EXCL = ["Engineering - 15%", "Field Activies", "Milestone", "WFX Deliverables", "CHS Deliverables"]
PAGINA = {
    30: lambda l: l["Level 4"] == "Engineering - 30%",
    60: lambda l: l["Level 4"] not in EXCL + ["Engineering - 30%", "Engineering - 90%"],
    90: lambda l: l["Level 4"] not in EXCL + ["Engineering - 30%", "Engineering - 60%"],
}

serie = {}
for r in abd:
    serie.setdefault(r["TaskUID"], []).append((datetime.fromisoformat(r["TimeByDay"]), r["Trabalho Real"]))

def soma(vals):
    vals = [v for v in vals if v is not None]
    return sum(vals) if vals else None

def real(linhas, cond):
    return soma([v for l in linhas for (t, v) in serie.get(l["NodeKey"], []) if cond(t)])

def sub(a, b):
    return None if a is None and b is None else (a or 0) - (b or 0)

def divide(a, b, alt=None):
    if b in (None, 0): return alt
    return None if a is None else a / b

def zero(x): return x is None or x == 0
def inicio_semana(d): return datetime(d.year, d.month, d.day) - timedelta(days=(d.weekday() + 1) % 7)
def floor(d): return datetime(d.year, d.month, d.day)
def datediff(a, b): return (floor(b) - floor(a)).days

# ---------------------------------------------------------------------- 60%: horas úteis 8–12/13–17, seg–sex, exceto 01/05/2026
def fracao_util(ini, fim, corte):
    if corte >= fim: return 1
    if corte <= ini: return 0
    dur = dec = 0.0
    d = floor(ini)
    while d <= floor(fim):
        if d.weekday() <= 4 and d.date() != FERIADO:
            for a, b in ((8, 12), (13, 17)):
                ja, jb = d + timedelta(hours=a), d + timedelta(hours=b)
                dur += max(0, (min(fim, jb) - max(ini, ja)).total_seconds())
                dec += max(0, (min(corte, jb) - max(ini, ja)).total_seconds())
        d += timedelta(days=1)
    return None if dur == 0 else dec / dur

def curva60(linhas, gate, eixo, ci, cf):
    if eixo is None: return None
    corte = floor(eixo) + timedelta(hours=17)
    at = [l for l in linhas if l["Level 4"] == gate and l.get("É Resumo") is False]
    if not at: return None
    inval = [l for l in at if (l["Trabalho"] or 0) < 0 or ((l["Trabalho"] or 0) > 0 and (dt(l[ci]) is None or dt(l[cf]) is None or dt(l[cf]) < dt(l[ci])))]
    if inval: return None
    total = soma([l["Trabalho"] for l in at])
    prev = 0.0
    for l in at:
        p, i, f = l["Trabalho"], dt(l[ci]), dt(l[cf])
        if p and p > 0 and i and f and f >= i:
            fr = fracao_util(i, f, corte)
            if fr is not None: prev += p * fr
    return divide(prev, total, 1)

# ---------------------------------------------------------------------- 30/90: avanço linear por dia corrido
def avanco(i, f, data):
    if i is None or f is None: return 0
    if data < i: return 0
    if data >= f: return 1
    dur = datediff(i, f)
    if dur <= 0: return 1
    return datediff(i, data) / dur

def planejado(gl, ci, cf, data):
    return soma([None if l["Trabalho"] is None else l["Trabalho"] * avanco(dt(l[ci]), dt(l[cf]), data) for l in gl])

def calcular(n, linhas, corte):
    gate = f"Engineering - {n}%"
    gl = [l for l in linhas if l["Level 4"] == gate]
    LB = soma([l["Trab Linha de Base"] for l in gl])
    total = soma([l["Trabalho"] for l in gl])
    IS = inicio_semana(corte)
    acc_real = None if zero(LB) else (None if (r := divide(soma([l["Trabalho Real"] for l in gl]), LB, 0)) is None else min(1, r))
    out = {"ACC | % Realizado": acc_real}
    if n == 60:
        out["ACC | % Planejado"] = curva60(linhas, gate, corte, "Início", "Término")
        a, b = curva60(linhas, gate, corte, "Início", "Término"), curva60(linhas, gate, corte - timedelta(days=7), "Início", "Término")
        out["BASE SEM | % Planejado"] = None if a is None or b is None else a - b
        rs = sub(real(gl, lambda t: t < floor(corte) + timedelta(days=1)), real(gl, lambda t: t < IS))
        out["BASE SEM | % Realizado"] = None if zero(LB) else divide(rs, LB, 0)
    else:
        out["ACC | % Planejado"] = None if zero(total) else divide(planejado(gl, "Início", "Término", IS + timedelta(days=7)), total, 0)
        pl = sub(planejado(gl, "Início", "Término", IS + timedelta(days=7)), planejado(gl, "Início", "Término", IS))
        out["BASE SEM | % Planejado"] = None if zero(total) else divide(pl, total, 0)
        rs = sub(real(gl, lambda t: t <= corte), real(gl, lambda t: t < IS))
        out["BASE SEM | % Realizado"] = None if zero(LB) or (n == 90 and zero(rs)) else divide(rs, LB, 0)
    p, r = out["ACC | % Planejado"], out["ACC | % Realizado"]
    out["ACC | % Desvio"] = None if p is None or r is None else r - p
    p, r = out["BASE SEM | % Planejado"], out["BASE SEM | % Realizado"]
    out["BASE SEM | % Desvio"] = None if p is None or r is None or (n == 90 and p == 0) else r - p
    # PRAZO (ALLSELECTED): inícios e duração
    ib = min([dt(l["Início da Linha de Base"]) for l in gl if dt(l["Início da Linha de Base"])], default=None)
    ir = min([dt(l["Início"]) for l in gl if dt(l["Início"])], default=None)
    tb = max([dt(l["Término da Linha de Base"]) for l in gl if dt(l["Término da Linha de Base"])], default=None)
    out["PRAZO | Início Baseline"] = ib.isoformat() if ib else None
    out["PRAZO | Início Reprogramado"] = ir.isoformat() if ir else None
    out["PRAZO | Duração Baseline"] = datediff(ib, tb) if ib and tb else None

    # Curva (eixo dCalendario[Data Corte] = quinta; Início da Semana = domingo) — só semanas com Mostrar Período = 1
    tr = max([dt(l["Término"]) for l in gl if dt(l["Término"])], default=None)
    dmin, dmax = min(x for x in (ib, ir) if x), max(x for x in (tb, tr) if x)
    pontos = []
    eixo = datetime(2025, 1, 2)
    while eixo <= datetime(2027, 12, 30):
        s = eixo - timedelta(days=4)
        if s + timedelta(days=6) >= dmin and s <= dmax:
            pt = {"categoria": eixo.strftime("%Y-%m-%d")}
            if n == 60:
                cb, cbA = curva60(linhas, gate, eixo, "Início da Linha de Base", "Término da Linha de Base"), curva60(linhas, gate, eixo - timedelta(days=7), "Início da Linha de Base", "Término da Linha de Base")
                pt["COLUNA | % Baseline"] = None if cb is None or cbA is None else cb - cbA
                pt["CURVA | % Baseline"] = cb
                pt["CURVA | % Reprogramado"] = curva60(linhas, gate, eixo, "Início", "Término")
                fim = s + timedelta(days=6); lim = min(fim, corte)
                pt["COLUNA | % Realizado"] = None if s > corte or zero(LB) else divide(real(gl, lambda t: s <= t < floor(lim) + timedelta(days=1)), LB, 0)
                if eixo > corte or zero(LB): cr = None
                else:
                    ac = soma([l["Trabalho Real"] for l in gl]) if eixo == corte else real(gl, lambda t: t < floor(eixo) + timedelta(days=1))
                    d = divide(ac, LB, 0); cr = None if d is None else min(1, d)
                pt["CURVA | % Realizado"] = cr
            else:
                def curva(ci, cf):
                    de = s + timedelta(days=7)
                    dm = max([dt(l[cf]) for l in gl if dt(l[cf])], default=None)
                    if dm is None: return None
                    return 1 if de >= dm else divide(planejado(gl, ci, cf, min(de, dm)), total, 0)
                pt["CURVA | % Baseline"] = curva("Início da Linha de Base", "Término da Linha de Base")
                pt["CURVA | % Reprogramado"] = curva("Início", "Término")
                pl = sub(planejado(gl, "Início", "Término", s + timedelta(days=7)), planejado(gl, "Início", "Término", s))
                pt["COLUNA | % Baseline"] = None if zero(total) else divide(pl, total, 0)
                lim = min(s + timedelta(days=6), corte)
                pt["COLUNA | % Realizado"] = None if s > corte or zero(LB) else divide(real(gl, lambda t: s <= t <= lim), LB, 0)
                de = s + timedelta(days=6)
                if zero(LB): cr = None
                elif de < corte:
                    h = divide(real(gl, lambda t: t <= min(de, corte)), LB, 0); cr = None if h is None else min(1, h)
                elif de >= corte and de - timedelta(days=6) <= corte: cr = acc_real
                else: cr = None
                pt["CURVA | % Realizado"] = cr
            if any(v is not None for k, v in pt.items() if k != "categoria"):
                pontos.append(pt)
        eixo += timedelta(days=7)
    out["pontos"] = pontos
    return out

res = {}
for n in (30, 60, 90):
    linhas = [l for l in wbx if PAGINA[n](l)]
    res[str(n)] = calcular(n, linhas, CORTE)
json.dump(res, open(os.path.join(BASE, "esperado_curvas.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
for n in res:
    print(n, {k: (round(v, 6) if isinstance(v, float) else v) for k, v in res[n].items() if k != "pontos"}, "pontos", len(res[n]["pontos"]))
