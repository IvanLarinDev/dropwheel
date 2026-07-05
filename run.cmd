@echo off
rem Dropwheel helper: run.cmd [run|build|publish|stop]   (default: run)
setlocal
set "PROJ=%~dp0src\Dropwheel"
set "EXE=%PROJ%\bin\Debug\net10.0-windows\Dropwheel.exe"
set "CMD=%~1"
if "%CMD%"=="" set "CMD=run"

if /i "%CMD%"=="stop"    goto :stop
if /i "%CMD%"=="build"   goto :build
if /i "%CMD%"=="publish" goto :publish
if /i "%CMD%"=="run"     goto :run
echo Usage: run.cmd [run^|build^|publish^|stop]
exit /b 1

:stop
taskkill /im Dropwheel.exe /f >nul 2>&1
echo Dropwheel stopped.
exit /b 0

:build
dotnet build "%PROJ%" -v q
exit /b %errorlevel%

:run
taskkill /im Dropwheel.exe /f >nul 2>&1
dotnet build "%PROJ%" -v q || exit /b 1
start "" "%EXE%"
echo Dropwheel started.
exit /b 0

:publish
dotnet publish "%PROJ%" -c Release -o "%~dp0dist"
if errorlevel 1 exit /b 1
echo Published to %~dp0dist
exit /b 0
