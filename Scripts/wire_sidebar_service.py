from pathlib import Path
p=Path(r'C:\Dev\N-A-Connect aa\N-A-Connect\Program.cs')
s=p.read_text(encoding='utf-8-sig')
key='builder.Services.AddScoped<APS.WebApp.Services.Permissions.IPermissionService, APS.WebApp.Services.Permissions.PermissionService>();'
assert key in s
addon='builder.Services.AddScoped<APS.WebApp.Services.Sidebar.ISidebarStore, APS.WebApp.Services.Sidebar.SqlSidebarStore>();'
if addon not in s:s=s.replace(key,key+'\n'+addon)
p.write_text(s,encoding='utf-8')
print('Service registered',s.count(addon))
