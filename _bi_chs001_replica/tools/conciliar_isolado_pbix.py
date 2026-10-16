"""Conciliação SOMENTE LEITURA: leitura isolada do .mpp (BiChs001MppReader → JSON) × cache do PBIX (fixtures_pbix).

Uso: python -I conciliar_isolado_pbix.py <leitura.json> <saida.json>
  leitura.json = {"tarefas": [...BiTarefa...], "diario": [...BiTrabalhoDia...]} (harness/CLI do leitor isolado)
Compara por GUID da tarefa (WBX[NodeKey] / AssignmentByDay[TaskUID] do PBIX): datas, trabalhos e a série semanal de Trabalho Real
(semana iniciada no domingo, grão do AssignmentByDay). Não arredonda para "passar": reporta igualdade exata (1e-6) e a 2 casas.
"""
import json
import sys
from collections import defaultdict
from datetime import date, datetime, timedelta
from pathlib import Path

FIX = Path(__file__).resolve().parent.parent / "fixtures_pbix"


def domingo(iso: str) -> str:
    d = date.fromisoformat(iso[:10])
    return (d - timedelta(days=(d.weekday() + 1) % 7)).isoformat()


def guid(g):
    return None if g is None else "{" + str(g).strip("{}").upper() + "}"


def main(entrada: str, saida: str) -> int:
    leitura = json.loads(Path(entrada).read_text(encoding="utf-8"))
    wbx = json.loads((FIX / "wbx.json").read_text(encoding="utf-8"))
    abd = json.loads((FIX / "abd.json").read_text(encoding="utf-8"))
    tarefas = {guid(t["GuidTarefa"]): t for t in leitura["tarefas"]}
    por_uid = {t["UidTarefa"]: t for t in leitura["tarefas"]}

    campos = {  # coluna PBIX -> campo do leitor
        "Início": "Inicio", "Término": "Termino", "Início da Linha de Base": "InicioLinhaBase", "Término da Linha de Base": "TerminoLinhaBase",
        "Trabalho": "TrabalhoHoras", "Trab Linha de Base": "TrabalhoLinhaBaseHoras", "Trabalho Real": "TrabalhoRealHoras",
    }
    res = {"folhas_pbix": len(wbx), "casadas_por_guid": 0, "campos": {}, "gates": {}}
    dif_exemplos = defaultdict(list)
    for c in campos:
        res["campos"][c] = {"comparadas": 0, "iguais_exato": 0, "iguais_2casas_ou_minuto": 0}
    for w in wbx:
        t = tarefas.get(guid(w["NodeKey"]))
        if not t:
            continue
        res["casadas_por_guid"] += 1
        for c, k in campos.items():
            a, b = w.get(c), t.get(k)
            if a is None and b is None:
                continue
            r = res["campos"][c]
            r["comparadas"] += 1
            if isinstance(a, str) or isinstance(b, str):
                da = datetime.fromisoformat(a) if a else None
                db = datetime.fromisoformat(b) if b else None
                igual = da == db
                perto = da is not None and db is not None and abs((da - db).total_seconds()) <= 60
            else:
                igual = a is not None and b is not None and abs(a - b) <= 1e-6
                perto = a is not None and b is not None and abs(a - b) <= 0.005 + 1e-9
            r["iguais_exato"] += igual
            r["iguais_2casas_ou_minuto"] += perto
            if not perto and len(dif_exemplos[c]) < 5:
                dif_exemplos[c].append({"NodeKey": w["NodeKey"], "EDT": w.get("EDT"), "pbix": a, "mpp": b})
    res["exemplos_diferenca"] = dif_exemplos

    # Level 4 pelo PAI (GeraWBS) com o alias do BI aplicado ao nome bruto.
    alias = {"Emgineering - 30%": "Engineering - 30%"}
    cont = defaultdict(int)
    for t in leitura["tarefas"]:
        if t["EhResumo"]:
            continue
        a = t
        while a and a.get("NivelEstrutura") != 2:
            a = por_uid.get(a.get("UidTarefaPai"))
        if a:
            cont[alias.get(a["Nome"], a["Nome"])] += 1
    pbix = defaultdict(int)
    for w in wbx:
        pbix[w.get("Level 4")] += 1
    res["gates"] = {g: {"mpp_com_alias": cont.get(g, 0), "pbix": pbix.get(g, 0)} for g in sorted(set(cont) | set(pbix), key=str)}

    # Série semanal de Trabalho Real (domingo).
    uid_guid = {t["UidTarefa"]: guid(t["GuidTarefa"]) for t in leitura["tarefas"]}
    sem = defaultdict(float)
    for d in leitura["diario"]:
        if d.get("TrabalhoRealHoras"):
            sem[(uid_guid[d["UidTarefa"]], domingo(d["Data"]))] += d["TrabalhoRealHoras"]
    pb = {(guid(r["TaskUID"]), r["TimeByDay"][:10]): r["Trabalho Real"] for r in abd if r.get("Trabalho Real") is not None}
    chaves = set(pb) | {k for k, v in sem.items() if abs(v) > 1e-9}
    iguais = perto = 0
    so_pbix = so_mpp = 0
    exemplos = []
    for k in sorted(chaves, key=str):
        a, b = pb.get(k), sem.get(k)
        if a is None:
            so_mpp += 1
        elif b is None or abs(b) <= 1e-9:
            so_pbix += 1
        if a is not None and b is not None:
            iguais += abs(a - b) <= 1e-6
            perto += abs(a - b) <= 0.005 + 1e-9
            if abs(a - b) > 0.005 and len(exemplos) < 8:
                exemplos.append({"TaskUID": k[0], "TimeByDay": k[1], "pbix": a, "mpp": round(b, 6)})
    res["serie_real_semanal"] = {
        "pares_pbix": len(pb), "pares_mpp_nao_nulos": sum(1 for v in sem.values() if abs(v) > 1e-9), "pares_uniao": len(chaves),
        "iguais_exato": iguais, "iguais_2casas": perto, "so_no_pbix": so_pbix, "so_no_mpp": so_mpp,
        "datas_pbix_todas_domingo": all(date.fromisoformat(k[1]).weekday() == 6 for k in pb),
        "soma_pbix": round(sum(pb.values()), 6), "soma_mpp_mesmas_tarefas": round(sum(v for (g, _), v in sem.items() if any(g == x[0] for x in pb)), 6),
        "exemplos_diferenca": exemplos,
    }
    Path(saida).write_text(json.dumps(res, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({k: v for k, v in res.items() if k != "exemplos_diferenca"}, ensure_ascii=False, indent=1))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
