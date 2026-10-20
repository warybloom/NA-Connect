from pathlib import Path
import shutil,subprocess
root=Path(r"C:\Dev\N-A-Connect aa\N-A-Connect")
dst=root.parent/"_backups"/"resumo_analise_20261013_221634"
rel=["ClientApp/src/features/sidebar/catalog.json","Services/Sidebar/SidebarCatalog.Generated.cs"]
for fn in rel:
 p=root/fn
 q=dst/fn;q.parent.mkdir(parents=True,exist_ok=True)
 shutil.copy2(p,q)
 print("backup",fn,p.stat().st_size)
cmd=["node","scripts/sidebar-catalog.cjs"]
r=subprocess.run(cmd,cwd=root/"ClientApp",capture_output=True,text=True,encoding="utf-8",errors="replace",timeout=20)
print("GEN_RETURN",r.returncode,"OUT",r.stdout[:800],"ERR",r.stderr[:800])
