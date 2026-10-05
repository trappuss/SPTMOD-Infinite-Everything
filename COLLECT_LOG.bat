@echo off
setlocal EnableExtensions
rem COLLECT_LOG.bat v2.0 (Infinite Everything) - run after testing (game/server may still be open). Only reads the game folder.
rem Copies the Infinite Everything lines + errors from BepInEx\LogOutput.log and the newest SPT server log into _build\ingame.log.
rem Uses the SPT folder saved by BUILD_AND_INSTALL.bat (build.local.cfg).
set "HERE=%~dp0"
set "GAME="
if exist "%HERE%build.local.cfg" for /f "usebackq tokens=1,* delims==" %%a in ("%HERE%build.local.cfg") do if /I "%%a"=="GAME" set "GAME=%%b"
if not defined GAME ( echo Run BUILD_AND_INSTALL.bat first - it saves your SPT folder. & pause & exit /b 1 )
set "SRC=%GAME%\BepInEx\LogOutput.log"
set "OUT=%HERE%_build\ingame.log"
if not exist "%HERE%_build" mkdir "%HERE%_build"
> "%OUT%" echo Infinite Everything logs collected %DATE% %TIME%
>> "%OUT%" echo ==== client: BepInEx\LogOutput.log ====
if exist "%SRC%" (
  findstr /I /C:"Infinite Everything" /C:"InfiniteEverything" /C:"Infinite " /C:"Patch" /C:"Refill" /C:"Turret belt" "%SRC%" >> "%OUT%"
  >> "%OUT%" echo ---- client errors / exceptions ----
  findstr /I /C:"[Error" /C:"Exception" "%SRC%" >> "%OUT%"
) else ( >> "%OUT%" echo no LogOutput.log )
>> "%OUT%" echo ==== server: newest SPT_Runtime\user\logs\spt\*.log ====
set "SLOG="
for /f "delims=" %%f in ('dir /b /o-d "%GAME%\SPT_Runtime\user\logs\spt\*.log" 2^>nul') do if not defined SLOG set "SLOG=%GAME%\SPT_Runtime\user\logs\spt\%%f"
if defined SLOG (
  >> "%OUT%" echo %SLOG%
  findstr /I /C:"Infinite Everything" /C:"infiniteeverything" /C:"modloader" "%SLOG%" >> "%OUT%"
) else ( >> "%OUT%" echo no server log found )
echo Saved %OUT%
pause
