from pathlib import Path
import subprocess
r=Path(r"C:\Dev\N-A-Connect aa\N-A-Connect")
for section,root,patterns in [
 ('FRONTEND-RESUMO',r/'ClientApp/src', ['Consumo HH','CONSUMO HH','Avanço Previsto','AVANÇO PREVISTO','Controle de Produção','Controle de Producao']),
 ('ROUTES',r/'ClientApp/src',['gestao-vista','gestaoVista','control-producao','producao/resumo','controle-producao']),
 ('BACKEND',r/'Controllers',['gestao-vista','GestaoVista','PmoExecutive','GetProfessional','ProfissionaisPorDocumento']),
]:
 print('\n###',section)
 count=0
 for f in root.rglob('*'):
  if f.suffix not in ['.ts','.tsx','.cs']:continue
  try:txt=f.read_text(encoding='utf-8-sig',errors='replace').splitlines()
  except:continue
  hits=[(i,line.strip()[:210])for i,line in enumerate(txt,1)if any(s in line for s in patterns)]
  if hits:
   print(f.relative_to(r),'|',hits[:6]); count+=1
   if count>35:break
print('\n### STATUS relevant')
p=subprocess.run(['git','status','--short'],cwd=r,text=True,encoding='utf-8',errors='replace',capture_output=True)
print(p.stdout[:9000]);print('GIT_RETURN',p.returncode)
