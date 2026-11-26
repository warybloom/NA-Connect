from pathlib import Path
r=Path(r"C:\Dev\N-A-Connect aa\N-A-Connect")
keys=['IGestaoVistaReportRepository','PmoActualFactsProvider','IPmoActualFactsProvider','gestao-vista','gestao-vista/report','HhRealizado','PlannedHhTotal','HH_Realizado','GestaoVistaReport(', 'ProductionDashboards.']
for root in ['Controllers','Services','Program.cs']:
 rp=r/root
 files=rp.rglob('*.cs') if rp.is_dir() else [rp]
 for p in files:
  t=p.read_text(encoding='utf-8-sig',errors='replace').splitlines()
  a=[f'{i} {x.strip()[:180]}' for i,x in enumerate(t,1) if any(k in x for k in keys)]
  if a: print(f'{p.relative_to(r)}\n'+'\n'.join(a[:12]))
