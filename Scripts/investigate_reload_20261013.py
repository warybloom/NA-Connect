from pathlib import Path
import re, subprocess
root=Path(r"C:\Dev\N-A-Connect aa\N-A-Connect")
src=root/"ClientApp"/"src"
patterns = [r'location\.reload\s*\(', r'window\.location',r'visibilitychange',r'addEventListener\(["\']focus',r'onfocus=',r'beforeunload',r'window\.on',r'navigate\(["\']0',r'form\.submit\(',r'<form\b',r'location\.href',r'window\.history\.go']
for pat in patterns:
 print("\n### PATTERN",pat)
 out=0
 for p in src.rglob("*"):
  if p.suffix not in (".ts",".tsx",".js",".jsx"): continue
  try: txt=p.read_text(encoding="utf-8",errors="replace")
  except Exception: continue
  for i,line in enumerate(txt.splitlines(),1):
   if re.search(pat,line,re.I):
    print(str(p.relative_to(root))+":"+str(i)+": "+line.strip()[:210])
    out+=1
    if out>=35: break
  if out>=35: break
print("\n### GIT")
res=subprocess.run(["git","status","--short"],cwd=root,text=True,capture_output=True)
print(res.stdout[:4000])
