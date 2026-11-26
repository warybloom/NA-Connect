@echo off
cd /d "C:\Dev\N-A-Connect aa\N-A-Connect"
dotnet build AccIntegration.WebApp.csproj --no-restore -m:1 -v:q -p:SidebarCompileOnly=true -p:UseSharedCompilation=false -p:OutputPath=C:\Dev\_sidebar_validate_20261013\bin\ > sidebar_dotnet_controlled.txt 2>&1
echo %errorlevel% > sidebar_dotnet_exit.txt
