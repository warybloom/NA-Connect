@echo off
cd /d "C:\Dev\N-A-Connect aa\N-A-Connect\ClientApp"
node --max-old-space-size=8192 node_modules\typescript\bin\tsc --noEmit --pretty false > "..\Scripts\verify_pacotes_ui_20261013.log" 2>&1
echo %errorlevel% > "..\Scripts\verify_pacotes_ui_20261013.exit"
