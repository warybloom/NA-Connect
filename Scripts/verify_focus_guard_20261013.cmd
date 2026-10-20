@echo off
cd /d "C:\Dev\N-A-Connect aa\N-A-Connect\ClientApp"
node --max-old-space-size=8192 node_modules\typescript\bin\tsc --noEmit --pretty false > "..\Scripts\verify_focus_guard_tsc.log" 2>&1
echo %errorlevel% > "..\Scripts\verify_focus_guard_tsc.exit"
node scripts\test-sidebar.cjs > "..\Scripts\verify_focus_guard_tests.log" 2>&1
echo %errorlevel% > "..\Scripts\verify_focus_guard_tests.exit"
