@echo off
cd /d "C:\Dev\N-A-Connect aa\N-A-Connect"
dotnet test Tests\NA_Connect.Workflow.Tests\NA_Connect.Workflow.Tests.csproj --artifacts-path Tests/.sidebar-validation --no-restore -p:EnableDefaultContentItems=false -p:ScopedCssEnabled=false -p:UseSharedCompilation=false -m:1 --filter "FullyQualifiedName~SidebarSecurityTests" -v:minimal > "Scripts\sidebar_security_tests_20261013.log" 2>&1
echo %errorlevel% > "Scripts\sidebar_security_tests_20261013.exit"
