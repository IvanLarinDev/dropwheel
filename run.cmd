@echo off
rem Dropwheel helper: run.cmd [run|build|publish|sdk|stop]   (default: run)
setlocal
set "PROJ=%~dp0src\Dropwheel"
set "EXE=%PROJ%\bin\Release\net10.0-windows\Dropwheel.exe"
set "CMD=%~1"
if "%CMD%"=="" set "CMD=run"

if /i "%CMD%"=="stop"    goto :stop
if /i "%CMD%"=="build"   goto :build
if /i "%CMD%"=="publish" goto :publish
if /i "%CMD%"=="sdk"     goto :sdk
if /i "%CMD%"=="run"     goto :run
echo Usage: run.cmd [run^|build^|publish^|sdk^|stop]
exit /b 1

:stop
taskkill /im Dropwheel.exe /f >nul 2>&1
echo Dropwheel stopped.
exit /b 0

:build
call :restore
if errorlevel 1 exit /b 1
call :invoke_dotnet build "%PROJ%" -c Release -v q --no-restore
exit /b %errorlevel%

:run
taskkill /im Dropwheel.exe /f >nul 2>&1
call :restore
if errorlevel 1 exit /b 1
call :invoke_dotnet build "%PROJ%" -c Release -v q --no-restore || exit /b 1
start "" "%EXE%"
echo Dropwheel started.
exit /b 0

:publish
call :restore
if errorlevel 1 exit /b 1
call :invoke_dotnet publish "%PROJ%" -c Release -o "%~dp0dist" --no-restore
if errorlevel 1 exit /b 1
echo Published to %~dp0dist
exit /b 0

:sdk
call :select_dotnet
if errorlevel 1 exit /b 1
echo DOTNET_EXE=%DOTNET_EXE%
echo DOTNET_ROOT=%DOTNET_ROOT%
call :invoke_dotnet --version
exit /b %errorlevel%

:restore
call :select_dotnet
if errorlevel 1 exit /b 1
call :invoke_dotnet restore "%~dp0Dropwheel.slnx" --locked-mode -v q
exit /b %errorlevel%

:invoke_dotnet
pushd "%~dp0" >nul 2>&1
if errorlevel 1 exit /b 1
"%DOTNET_EXE%" %*
set "DOTNET_COMMAND_ERROR=%errorlevel%"
popd
exit /b %DOTNET_COMMAND_ERROR%

:select_dotnet
set "DOTNET_EXE="
if defined DOTNET_ROOT call :try_dotnet "%DOTNET_ROOT%\dotnet.exe"
if defined DOTNET_EXE exit /b 0
call :try_dotnet "%USERPROFILE%\.dotnet\dotnet.exe"
if defined DOTNET_EXE exit /b 0
for /f "delims=" %%D in ('where.exe dotnet 2^>nul') do if not defined DOTNET_EXE call :try_dotnet "%%D"
if defined DOTNET_EXE exit /b 0
echo No compatible .NET SDK was found for %~dp0global.json.
echo Install the pinned SDK under "%USERPROFILE%\.dotnet" or make it available on PATH.
exit /b 1

:try_dotnet
if "%~1"=="" exit /b 0
if not exist "%~1" exit /b 0
pushd "%~dp0" >nul 2>&1
if errorlevel 1 exit /b 0
"%~1" --version >nul 2>&1
set "DOTNET_CHECK_ERROR=%errorlevel%"
popd
if not "%DOTNET_CHECK_ERROR%"=="0" exit /b 0
set "DOTNET_EXE=%~f1"
for %%D in ("%DOTNET_EXE%") do set "DOTNET_ROOT=%%~dpD"
set "PATH=%DOTNET_ROOT%;%PATH%"
exit /b 0
