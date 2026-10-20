"""Aplica a correção 01 VISÃO GERAL + Programação Diária TV (19/10/2026) no checkout principal.

Uso (PowerShell, na raiz do repositório):
    python "C:\\Dev\\N-A-Connect aa\\_entrega_claude_visao_tv_diaria_20261019\\aplicar.py"            # confere e aplica
    python "C:\\Dev\\N-A-Connect aa\\_entrega_claude_visao_tv_diaria_20261019\\aplicar.py" --conferir  # só confere
    python "C:\\Dev\\N-A-Connect aa\\_entrega_claude_visao_tv_diaria_20261019\\aplicar.py" --reverter  # volta o backup

Segurança: antes de escrever QUALQUER arquivo, compara cada arquivo do checkout com a versão de base usada no
desenvolvimento (pasta base/). Se algum foi alterado por outra pessoa/sessão, nada é escrito e os conflitos são listados.
O backup dos arquivos originais está em C:\\Dev\\N-A-Connect aa\\_backup_claude_visao_tv_diaria_20261019.
"""
import os, shutil, sys

AQUI = os.path.dirname(os.path.abspath(__file__))
REPO = r"C:\Dev\N-A-Connect aa\N-A-Connect"
BACKUP = r"C:\Dev\N-A-Connect aa\_backup_claude_visao_tv_diaria_20261019"
ARQUIVOS = [l.strip() for l in open(os.path.join(AQUI, "manifesto.txt"), encoding="utf-8") if l.strip()]
norm = lambda b: b.replace(b"\r\n", b"\n")
ler = lambda p: open(p, "rb").read() if os.path.exists(p) else None

def conferir():
    conflitos = []
    for f in ARQUIVOS:
        atual, base, novo = ler(os.path.join(REPO, f)), ler(os.path.join(AQUI, "base", f)), ler(os.path.join(AQUI, "novo", f))
        if atual is not None and novo is not None and norm(atual) == norm(novo):
            print("já aplicado  ", f)
        elif base is None and atual is None:
            print("novo         ", f)
        elif base is not None and atual is not None and norm(atual) == norm(base):
            print("ok p/ aplicar", f)
        else:
            print("CONFLITO     ", f); conflitos.append(f)
    return conflitos

if "--reverter" in sys.argv:
    for f in ARQUIVOS:
        origem, destino = os.path.join(BACKUP, f), os.path.join(REPO, f)
        if os.path.exists(origem): shutil.copy2(origem, destino); print("restaurado", f)
        elif os.path.exists(destino): os.remove(destino); print("removido (arquivo novo)", f)
    sys.exit(0)

conflitos = conferir()
if conflitos:
    print(f"\n{len(conflitos)} conflito(s): NADA foi alterado. Compare com {AQUI}\\novo antes de prosseguir.")
    sys.exit(1)
if "--conferir" in sys.argv:
    sys.exit(0)
for f in ARQUIVOS:
    destino = os.path.join(REPO, f)
    os.makedirs(os.path.dirname(destino), exist_ok=True)
    shutil.copy2(os.path.join(AQUI, "novo", f), destino)
print(f"\nAplicado: {len(ARQUIVOS)} arquivo(s). Próximos passos no RESULTADO_CLAUDE_VISAO_GERAL_E_TV_DIARIA_20261019.md.")
