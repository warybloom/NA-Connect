@echo off
cd /d "C:\Dev\N-A-Connect aa\N-A-Connect"
dotnet build AccIntegration.WebApp.csproj --artifacts-path Tests/.sidebar-validation --no-restore -p:EnableDefaultContentItems=false -p:ScopedCssEnabled=false -p:UseSharedCompilation=false -m:1 -v:minimal > "Scripts\sidebar_backend_build_20261013.log" 2>&1
echo %errorlevel% > "Scripts\sidebar_backend_build_20261013.exit"
