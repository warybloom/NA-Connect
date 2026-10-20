@echo off
cd /d "C:\Dev\N-A-Connect aa\N-A-Connect\ClientApp"
"C:\Program Files\nodejs\node.exe" "node_modules\typescript\bin\tsc" --noEmit --pretty false > "..\Scripts\verify_resumo_tsc.log" 2>&1
echo %errorlevel% > "..\Scripts\verify_resumo_tsc.exit"
"C:\Program Files\nodejs\node.exe" "scripts\test-sidebar.cjs" > "..\Scripts\verify_resumo_test.log" 2>&1
echo %errorlevel% > "..\Scripts\verify_resumo_test.exit"
