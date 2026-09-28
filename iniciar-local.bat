@echo off
setlocal EnableDelayedExpansion
rem ============================================================================
rem  Levanta TODO el entorno local de la prueba TCC con un doble clic:
rem  Docker Desktop, Kafka (3 brokers), PostgreSQL, Redis, Keycloak, API,
rem  procesador, notificador, observabilidad (Grafana, Prometheus, Jaeger, Loki)
rem  y el panel de pruebas. Al final abre el navegador.
rem  Para apagar todo: detener-local.bat
rem ============================================================================
title TCC - Iniciar entorno local
cd /d "%~dp0"

echo.
echo  TCC - Plataforma de eventos de guias : entorno local
echo  ====================================================
echo.

rem ---- 1. Docker Desktop ------------------------------------------------------
echo [1/5] Verificando Docker...
docker info >nul 2>&1
if not errorlevel 1 goto :docker_listo

echo       Docker no esta corriendo. Iniciando Docker Desktop...
set "DOCKER_EXE="
if exist "%LOCALAPPDATA%\Programs\DockerDesktop\Docker Desktop.exe" set "DOCKER_EXE=%LOCALAPPDATA%\Programs\DockerDesktop\Docker Desktop.exe"
if exist "%ProgramFiles%\Docker\Docker\Docker Desktop.exe" set "DOCKER_EXE=%ProgramFiles%\Docker\Docker\Docker Desktop.exe"
if not defined DOCKER_EXE goto :sin_docker
start "" "%DOCKER_EXE%"
set /a intentos=0

:esperar_docker
timeout /t 5 /nobreak >nul
docker info >nul 2>&1
if not errorlevel 1 goto :docker_listo
set /a intentos+=1
if !intentos! geq 48 goto :docker_lento
echo       esperando a Docker... !intentos!
goto :esperar_docker

:docker_listo
echo       Docker listo.

rem ---- 2. Apagar el entorno de Aiven (usa los mismos puertos) -----------------
echo [2/5] Deteniendo el entorno de Aiven si estaba arriba (comparte puertos)...
docker compose -f infra\aiven\docker-compose.yml --profile publico stop >nul 2>&1

rem ---- 3. Entorno local completo ----------------------------------------------
echo [3/5] Levantando Kafka, PostgreSQL, Redis, Keycloak, observabilidad y aplicaciones...
echo       (la primera vez puede tardar varios minutos porque construye las imagenes)
docker compose -f infra\docker-compose.yml --profile aplicaciones up -d
if errorlevel 1 goto :compose_fallo

echo       Esperando a que la API este lista...
set /a intentos=0

:esperar_api
set "SALUD="
for /f "delims=" %%s in ('curl.exe -s -m 3 http://127.0.0.1:8090/salud/lista 2^>nul') do set "SALUD=%%s"
if "!SALUD!"=="Healthy" goto :api_lista
set /a intentos+=1
if !intentos! geq 60 goto :api_lenta
timeout /t 5 /nobreak >nul
goto :esperar_api

:api_lenta
echo       [AVISO] La API no respondio Healthy en 5 minutos (ultimo estado: !SALUD!). Se continua igual.
goto :panel

:api_lista
echo       API: Healthy

rem ---- 4. Panel de pruebas ------------------------------------------------------
:panel
echo [4/5] Iniciando el panel de pruebas...
netstat -ano | findstr /r /c:":8095 .*LISTENING" >nul
if not errorlevel 1 goto :panel_ya_estaba

set "PYTHON="
where python >nul 2>&1 && set "PYTHON=python"
if not defined PYTHON where py >nul 2>&1 && set "PYTHON=py"
if not defined PYTHON goto :sin_python
start "TCC - Panel de pruebas (no cerrar)" /min %PYTHON% herramientas\panel-pruebas\panel.py
timeout /t 3 /nobreak >nul
echo       Panel iniciado en una ventana minimizada (cerrarla detiene el panel).
goto :navegador

:panel_ya_estaba
echo       El panel ya estaba corriendo.
goto :navegador

:sin_python
echo       [AVISO] No se encontro Python: el panel no se inicio. Instala Python 3.10+ y vuelve a ejecutar.

rem ---- 5. Navegador -----------------------------------------------------------
:navegador
echo [5/5] Abriendo el navegador...
if defined TCC_SIN_NAVEGADOR goto :resumen
start "" "http://127.0.0.1:8095"
start "" "http://127.0.0.1:3000"
start "" "%~dp0docs\presentacion\index.html"

:resumen
echo.
echo  ====================================================
echo   Todo arriba. Direcciones:
echo     Panel de pruebas ..... http://127.0.0.1:8095
echo     Presentacion ......... docs\presentacion\index.html
echo     API .................. http://127.0.0.1:8090
echo     Grafana .............. http://127.0.0.1:3000
echo     Jaeger ............... http://127.0.0.1:16686
echo     Prometheus ........... http://127.0.0.1:9090
echo     Kafka UI ............. http://127.0.0.1:8080
echo     Keycloak (admin) ..... http://localhost:8081/admin
echo   Para apagar todo: detener-local.bat
echo  ====================================================
echo.
if not defined TCC_SIN_NAVEGADOR pause
exit /b 0

:sin_docker
echo       [ERROR] No se encontro Docker Desktop. Abrelo a mano y vuelve a ejecutar este archivo.
goto :fin_error

:docker_lento
echo       [ERROR] Docker no respondio en 4 minutos.
goto :fin_error

:compose_fallo
echo       [ERROR] docker compose fallo. Revisa el mensaje de arriba.
goto :fin_error

:fin_error
echo.
if not defined TCC_SIN_NAVEGADOR pause
exit /b 1
