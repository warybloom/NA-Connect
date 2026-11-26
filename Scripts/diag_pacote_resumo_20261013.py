from pathlib import Path
import sys
sys.stdout.reconfigure(encoding='utf-8')
r=Path(r"C:\Dev\N-A-Connect aa\N-A-Connect")
needle=['HH realizado','HHRealizado','HorasRealizadas','CustoRealizado','custoRealizado','PmoActualFactsProvider','ProductionSummary','production-summary','consumoHH','ConsumoHH','ResumoAsync','recognizedHours','HHApropriado','EvaluateDisciplinesAsync','TimesheetRowsAsync','PackageDiscipline']
for folder in ['Services','Controllers','ClientApp/src/features/production']:
 print('### FOLDER',folder)
 count=0
 for p in (r/folder).rglob('*'):
  if p.suffix not in ('.cs','.ts','.tsx'):continue
  try: lines=p.read_text('utf-8-sig',errors='replace').splitlines()
  except:continue
  hits=[(i,x.strip()[:180]) for i,x in enumerate(lines,1) if any(k in x for k in needle)]
  if not hits:continue
  print(str(p.relative_to(r)),[(i,x) for i,x in hits[:7]])
  count+=1
  if count>90:break
