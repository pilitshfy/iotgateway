@echo off
setlocal
cd /d "%~dp0"
echo ============================================
echo  IoTGateway local start (database + backend + UI)
echo ============================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-all.ps1"
echo.
echo If new windows were opened, keep them running.
pause
