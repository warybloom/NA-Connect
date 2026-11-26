from pathlib import Path
from collections import Counter
p=Path(r'C:\Dev\N-A-Connect aa\N-A-Connect')
skip={'.git','node_modules','ClientApp','Tests','Tools','bin','obj','Migrations','graphify-out','build-stage02'}
count=Counter()
for d in p.iterdir():
 if d.is_dir() and (d.name in skip or d.name.startswith('artifacts') or d.name.startswith('obj') or d.name.startswith('bin')):continue
 if d.is_dir():
  count[d.name]=sum(1 for x in d.rglob('*.cs') if not any(a in x.parts for a in skip))
 elif d.suffix.lower()=='.cs':count['ROOT']+=1
print(count)
