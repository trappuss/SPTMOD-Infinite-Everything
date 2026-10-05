@echo off
setlocal EnableExtensions
rem BUILD_AND_INSTALL.bat v2.1 (Infinite Everything) - builds, installs and packages both parts.
rem Close the game AND the SPT server first. Needs the .NET 10 SDK (dotnet) and an SPT 4.1.x install.
rem   client plugin -> <game>\BepInEx\plugins\InfiniteEverything\InfiniteEverything.dll
rem   server part   -> <game>\SPT_Runtime\user\mods\InfiniteEverything\InfiniteEverythingServer.dll (Infinite money, hideout fuel/filters)
rem   release zip   -> dist\SPTMOD-Infinite-Everything-<version>.zip (what publish.bat attaches to the GitHub release)
rem The SPT folder is asked once and saved in build.local.cfg (not committed). Log: _build\build.log
rem If the old InfiniteAmmo plugin is still installed it asks, then MOVES it to BepInEx\_disabled\ (nothing is deleted).
set "HERE=%~dp0"
set "LOGDIR=%HERE%_build"
if not exist "%LOGDIR%" mkdir "%LOGDIR%"
set "LOG=%LOGDIR%\build.log"

call :getgame || ( pause & exit /b 1 )
tasklist /FI "IMAGENAME eq EscapeFromTarkov.exe" 2>nul | find /I "EscapeFromTarkov.exe" >nul && ( echo Close the game first. & pause & exit /b 1 )
tasklist /FI "IMAGENAME eq SPT.Server.exe" 2>nul | find /I "SPT.Server.exe" >nul && ( echo Close the SPT server first. & pause & exit /b 1 )
where dotnet >nul 2>&1 || ( echo dotnet not found - install the .NET 10 SDK: https://dotnet.microsoft.com/download & pause & exit /b 1 )

set "VER="
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content -Raw '%HERE%Directory.Build.props')).Project.PropertyGroup.Version"`) do set "VER=%%v"
if not defined VER ( echo Could not read the version from Directory.Build.props. & pause & exit /b 1 )

> "%LOG%" echo Infinite Everything %VER% build %DATE% %TIME% - game: %GAME%
echo === client plugin %VER% ===
echo === client plugin ===>> "%LOG%"
dotnet build "%HERE%InfiniteEverything.csproj" -c Release -p:TarkovDir="%GAME%" -p:DeployToGame=true >> "%LOG%" 2>&1
set "RC1=%ERRORLEVEL%"
echo === server part %VER% ===
echo === server part ===>> "%LOG%"
dotnet build "%HERE%Server\InfiniteEverythingServer.csproj" -c Release >> "%LOG%" 2>&1
set "RC2=%ERRORLEVEL%"
type "%LOG%" | findstr /I /C:"error" /C:"warning CS" /C:"Deployed" /C:"laid out" /C:"Build succeeded"
if not "%RC1%"=="0" ( echo. & echo CLIENT BUILD FAILED - see %LOG% & pause & exit /b 1 )
if not "%RC2%"=="0" ( echo. & echo SERVER BUILD FAILED - see %LOG% & pause & exit /b 1 )
if not exist "%GAME%\BepInEx\plugins\InfiniteEverything\InfiniteEverything.dll" ( echo Client NOT installed - see %LOG% & pause & exit /b 1 )

set "SRVDLL=%HERE%Server\dist\SPT_Runtime\user\mods\InfiniteEverything\InfiniteEverythingServer.dll"
set "SRV=%GAME%\SPT_Runtime\user\mods\InfiniteEverything"
if not exist "%SRV%" mkdir "%SRV%"
copy /Y "%SRVDLL%" "%SRV%\" >nul || ( echo Server part copy FAILED & pause & exit /b 1 )
echo server part copied to %SRV%>> "%LOG%"
echo.
echo Installed: %GAME%\BepInEx\plugins\InfiniteEverything\InfiniteEverything.dll
echo Installed: %SRV%\InfiniteEverythingServer.dll

rem ---- release zip: dist\BepInEx + dist\SPT_Runtime, extract into the SPT folder ----
set "ZIP=%HERE%dist\SPTMOD-Infinite-Everything-%VER%.zip"
if exist "%HERE%dist\SPT_Runtime" rmdir /S /Q "%HERE%dist\SPT_Runtime"
del /Q "%HERE%dist\*.zip" 2>nul
mkdir "%HERE%dist\SPT_Runtime\user\mods\InfiniteEverything"
copy /Y "%SRVDLL%" "%HERE%dist\SPT_Runtime\user\mods\InfiniteEverything\" >nul
rem Forge rule: the license goes inside the archive (one copy in each mod folder, nothing loose in the SPT root)
copy /Y "%HERE%LICENSE" "%HERE%dist\SPT_Runtime\user\mods\InfiniteEverything\LICENSE" >nul
copy /Y "%HERE%LICENSE" "%HERE%dist\BepInEx\plugins\InfiniteEverything\LICENSE" >nul
powershell -NoProfile -Command "Compress-Archive -Path '%HERE%dist\BepInEx','%HERE%dist\SPT_Runtime' -DestinationPath '%ZIP%' -Force" >> "%LOG%" 2>&1
if exist "%ZIP%" ( echo Packaged:  %ZIP% & echo packaged %ZIP%>> "%LOG%" ) else ( echo Packaging FAILED - see %LOG% )

set "OLD=%GAME%\BepInEx\plugins\InfiniteAmmo"
if not exist "%OLD%\InfiniteAmmo.dll" goto done
echo.
echo The old InfiniteAmmo plugin is still installed: %OLD%
choice /C YN /M "Move it to BepInEx\_disabled\ now"
if errorlevel 2 ( echo Left in place: Infinite Everything will not load until it is removed. & goto done )
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set "TS=%%i"
if not exist "%GAME%\BepInEx\_disabled" mkdir "%GAME%\BepInEx\_disabled"
move "%OLD%" "%GAME%\BepInEx\_disabled\InfiniteAmmo_%TS%" >nul && echo Moved to BepInEx\_disabled\InfiniteAmmo_%TS% || echo Move FAILED.

:done
echo.
echo Start the SPT server (it must show "[Infinite Everything] server part %VER% loaded"), then the game: F12 ^> trappuss-InfiniteEverything.
pause
exit /b 0

rem ---- SPT folder: build.local.cfg, else the default below if it exists, else ask (and save) ----
:getgame
set "GAME="
set "CFG=%HERE%build.local.cfg"
if exist "%CFG%" for /f "usebackq tokens=1,* delims==" %%a in ("%CFG%") do if /I "%%a"=="GAME" set "GAME=%%b"
if not defined GAME if exist "G:\G Games\SPT4.1\SPT4.1 GAME\EscapeFromTarkov.exe" set "GAME=G:\G Games\SPT4.1\SPT4.1 GAME"
if defined GAME if exist "%GAME%\EscapeFromTarkov.exe" goto savegame
echo SPT folder not found. Paste the folder that contains EscapeFromTarkov.exe:
set /p "GAME=> "
set "GAME=%GAME:"=%"
if "%GAME:~-1%"=="\" set "GAME=%GAME:~0,-1%"
if not exist "%GAME%\EscapeFromTarkov.exe" ( echo EscapeFromTarkov.exe not found in "%GAME%". & exit /b 1 )
:savegame
> "%CFG%" echo GAME=%GAME%
exit /b 0
