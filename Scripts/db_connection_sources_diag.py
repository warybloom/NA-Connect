import json, os
from pathlib import Path
root=Path(r"C:\Dev\N-A-Connect aa\N-A-Connect")
for p in root.glob("appsettings*.json"):
 try:
  obj=json.loads(p.read_text(encoding='utf-8-sig'));c=obj.get("ConnectionStrings",{}).get("LocalConnection")
  if c:
   fragments={}
   for part in c.split(';'):
    if '=' in part:
     k,v=part.split('=',1);k=k.strip().lower()
     if k in ('server','data source','initial catalog','database','integrated security','trusted_connection','trusted connection'):fragments[k]=v.strip()
   print(p.name,'LocalConnection present',fragments)
  else:print(p.name,'no LocalConnection')
 except Exception as e:print(p.name,type(e).__name__)
