from pathlib import Path
import shutil
p=Path(r'C:\Dev\N-A-Connect aa\N-A-Connect\AccIntegration.WebApp.csproj')
backup=p.parent/'Scripts'/'AccIntegration.WebApp.csproj.sidebar_prebuild.bak'
if not backup.exists():shutil.copy2(p,backup)
s=p.read_text('utf-8-sig')
old='<DefaultItemExcludes>$(DefaultItemExcludes);obj-stage02/**;obj_test_alt/**;bin_test_alt/**;obj_test_05a/**;bin_test_05a/**</DefaultItemExcludes>'
new='<DefaultItemExcludes>$(DefaultItemExcludes);obj-stage02/**;obj_test_alt/**;bin_test_alt/**;obj_test_05a/**;bin_test_05a/**;artifacts_*/**;ClientApp/node_modules/**;node_modules/**;ClientApp/dist/**;graphify-out/**;ClientApp/graphify-out/**;build-stage02/**</DefaultItemExcludes>'
assert old in s or new in s
if old in s:s=s.replace(old,new);p.write_text(s,'utf-8')
print('csproj excludes configured, backup',backup)
