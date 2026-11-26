@echo off
cd /d "C:\Dev\N-A-Connect aa\N-A-Connect\ClientApp"
node --max-old-space-size=8192 node_modules\typescript\bin\tsc --noEmit --pretty false > "C:\Dev\N-A-Connect aa\N-A-Connect\sidebar_tsc_controlled.txt" 2>&1
echo %errorlevel% > "C:\Dev\N-A-Connect aa\N-A-Connect\sidebar_tsc_exit.txt"
