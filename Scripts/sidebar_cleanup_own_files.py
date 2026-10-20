from pathlib import Path
root=Path(r'C:\Dev\N-A-Connect aa\N-A-Connect')
for rel in ['ClientApp/src/pages/SidebarManagerPage.tsx','ClientApp/src/features/sidebar/sidebarConfig.ts']:
 p=root/rel
 if p.exists():
  p.unlink()
  print('Removed redundant manually-created file:',rel)
