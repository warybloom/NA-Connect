import ctypes,shutil,os
from pathlib import Path
class M(ctypes.Structure):
 _fields_=[('dwLength',ctypes.c_ulong),('dwMemoryLoad',ctypes.c_ulong),('ullTotalPhys',ctypes.c_ulonglong),('ullAvailPhys',ctypes.c_ulonglong),('ullTotalPageFile',ctypes.c_ulonglong),('ullAvailPageFile',ctypes.c_ulonglong),('ullTotalVirtual',ctypes.c_ulonglong),('ullAvailVirtual',ctypes.c_ulonglong),('ullAvailExtendedVirtual',ctypes.c_ulonglong)]
m=M();m.dwLength=ctypes.sizeof(M);ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
print('MEM_TOTAL_GB',round(m.ullTotalPhys/1e9,2),'FREE_GB',round(m.ullAvailPhys/1e9,2),'PAGEFREE_GB',round(m.ullAvailPageFile/1e9,2))
u=shutil.disk_usage('C:\\');print('DISK_FREE_GB',round(u.free/1e9,2),'TOTAL_GB',round(u.total/1e9,2))
p=Path(r'C:\Dev\N-A-Connect aa\N-A-Connect')
for x in ['artifacts_sidebar_20261013','artifacts_sidebar_compile','ClientApp/dist','node_modules','ClientApp/node_modules']:
 d=p/x
 if d.exists():
  try:
   sizes=sum(f.stat().st_size for f in d.rglob('*') if f.is_file())
   print(str(x),round(sizes/1e6,1),'MB')
  except Exception as e:print(str(x),str(e))
