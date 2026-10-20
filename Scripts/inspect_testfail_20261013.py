from pathlib import Path
p=Path(r"C:\Dev\N-A-Connect aa\N-A-Connect\Scripts\verify_resumo_pmo.log")
l=p.read_text(encoding="utf-8",errors="replace").splitlines()
for i,line in enumerate(l):
 if line.startswith("not ok"):
  print("\n".join(l[max(0,i-4):min(len(l),i+34)]))
  print("-"*40)
