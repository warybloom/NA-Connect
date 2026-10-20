@echo off
cd /d "C:\Dev\N-A-Connect aa\N-A-Connect\ClientApp"
"C:\Program Files\nodejs\node.exe" "node_modules\typescript\bin\tsc" --noEmit --pretty false > "..\Scripts\tv_tsc.log" 2>&1
echo %errorlevel% > "..\Scripts\tv_tsc.exit"
"C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" --version >NUL 2>&1
"C:\Program Files\nodejs\node.exe" "scripts\test-sidebar.cjs" > "..\Scripts\tv_sidebar.log" 2>&1
echo %errorlevel% > "..\Scripts\tv_sidebar.exit"
