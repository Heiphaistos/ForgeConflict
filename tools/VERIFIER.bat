@echo off
REM Lance la verification ForgeConflict en s'elevant si necessaire.
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Elevation requise, acceptez l invite UAC...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\ForgeConflict\vm-verify.ps1"
echo.
echo ================================================
echo  Verification terminee. Resultats transmis a l hote.
echo ================================================
pause
