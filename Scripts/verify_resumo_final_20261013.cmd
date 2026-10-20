@echo off
cd /d "C:\Dev\N-A-Connect aa\N-A-Connect\ClientApp"
"C:\Program Files\nodejs\node.exe" "scripts\test-pmo.mjs" > "..\Scripts\verify_resumo_pmo.log" 2>&1
echo %errorlevel% > "..\Scripts\verify_resumo_pmo.exit"
"C:\Program Files\nodejs\node.exe" "node_modules\vite\bin\vite.js" build > "..\Scripts\verify_resumo_build.log" 2>&1
echo %errorlevel% > "..\Scripts\verify_resumo_build.exit"
