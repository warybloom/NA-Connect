from pathlib import Path
p=Path(r'C:\Dev\N-A-Connect aa\N-A-Connect\AccIntegration.WebApp.csproj')
s=p.read_text('utf-8')
marker='</Project>'
assert s.count(marker)==1
if 'SidebarCompileOnly' not in s:
 block='''  <!-- Compilação de validação isolada: evita varredura de artefatos gerados e node_modules.
       Não se aplica à publicação normal, somente quando -p:SidebarCompileOnly=true. -->
  <PropertyGroup Condition="'$(SidebarCompileOnly)' == 'true'">
    <EnableDefaultItems>false</EnableDefaultItems>
  </PropertyGroup>
  <ItemGroup Condition="'$(SidebarCompileOnly)' == 'true'">
    <Compile Include="Program.cs;Properties/**/*.cs;Areas/**/*.cs;Controllers/**/*.cs;Services/**/*.cs;Models/**/*.cs;Data/**/*.cs;Migrations/**/*.cs" />
  </ItemGroup>
'''
 s=s.replace(marker,block+marker);p.write_text(s,'utf-8')
print('Validation-specific compile items configured')
