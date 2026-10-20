@echo off
cd /d "C:\Dev\N-A-Connect aa\N-A-Connect"
dotnet build AccIntegration.WebApp.csproj --no-restore -c Debug -o "C:\Dev\N-A-Connect aa\_build_validation\resumo_20261013" /p:UseAppHost=false > "Scripts\verify_resumo_backend.log" 2>&1
echo %errorlevel% > "Scripts\verify_resumo_backend.exit"
