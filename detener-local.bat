@echo off
rem Detiene el entorno local y el panel de pruebas. Los datos (PostgreSQL, Kafka) se conservan.
title TCC - Detener entorno local
cd /d "%~dp0"
echo.
echo  Deteniendo el panel de pruebas...
taskkill /fi "WINDOWTITLE eq TCC - Panel de pruebas*" /t /f >nul 2>&1
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /r /c:":8095 .*LISTENING"') do taskkill /pid %%p /f >nul 2>&1
echo  Deteniendo los contenedores del entorno local...
docker compose -f infra\docker-compose.yml --profile aplicaciones stop
echo.
echo  Listo. Para volver a levantar todo: iniciar-local.bat
echo.
pause
