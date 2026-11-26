from pathlib import Path
import shutil
r=Path(r"C:\Dev\N-A-Connect aa\N-A-Connect")
d=r.parent/"_backups"/"resumo_analise_20261013_221634"
for name in ["ClientApp/src/features/sidebar/sidebarModel.ts","Services/Sidebar/SidebarStore.cs","ClientApp/src/features/permissions/__tests__/permissions.test.ts","ClientApp/scripts/test-sidebar.cjs"]:
 p=r/name;q=d/name
 q.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,q);print("BACKUP",name)
