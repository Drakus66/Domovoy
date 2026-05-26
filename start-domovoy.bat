@echo off
echo === Starting Domovoy ===
echo.

echo Running pre-startup script...
powershell -ExecutionPolicy Bypass -File "pre-startup.ps1"

echo.
echo Starting Docker Compose...
docker-compose up -d

echo.
echo Domovoy startup completed!
pause
