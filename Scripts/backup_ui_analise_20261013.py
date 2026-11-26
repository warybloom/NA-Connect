from pathlib import Path
from datetime import datetime
import shutil,subprocess
root=Path(r"C:\Dev\N-A-Connect aa\N-A-Connect")
paths=[
"ClientApp/src/features/production/analytics/ResumoProducaoPage.tsx",
"ClientApp/src/features/production/productionRoutes.ts",
"ClientApp/src/features/production/ControleProducaoPage.tsx",
]
dst=root.parent/"_backups"/("resumo_analise_20261013_"+datetime.now().strftime("%H%M%S"))
dst.mkdir(parents=True)
for rel in paths:
 p=root/rel
 q=dst/rel;q.parent.mkdir(parents=True,exist_ok=True)
 shutil.copy2(p,q)
 print(rel,p.stat().st_size)
p=subprocess.run(["git","status","--short","--",*paths],cwd=root,stdout=subprocess.PIPE,stderr=subprocess.PIPE,encoding="utf-8",errors="replace")
print("STATUS_BEFORE",p.stdout)
print("BACKUP",dst)
