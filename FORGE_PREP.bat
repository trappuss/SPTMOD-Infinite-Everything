@echo off
setlocal EnableExtensions
rem FORGE_PREP.bat v1.0 (Infinite Everything) - run AFTER BUILD_AND_INSTALL.bat and publish.bat.
rem 1. checks the release zip and that the GitHub download link works
rem 2. copies the download link to the clipboard and opens VirusTotal with the zip selected in Explorer
rem 3. asks for the VirusTotal result link, saves both links to _build\forge-links.txt
rem 4. opens The Forge and the listing text (docs\FORGE_LISTING.md)
set "HERE=%~dp0"
if not exist "%HERE%_build" mkdir "%HERE%_build"
set "VER="
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content -Raw '%HERE%Directory.Build.props')).Project.PropertyGroup.Version"`) do set "VER=%%v"
if not defined VER ( echo Could not read the version from Directory.Build.props. & pause & exit /b 1 )
set "ZIP=%HERE%dist\SPTMOD-Infinite-Everything-%VER%.zip"
if not exist "%ZIP%" ( echo %ZIP% not found - run BUILD_AND_INSTALL.bat first. & pause & exit /b 1 )
set "URL=https://github.com/trappuss/SPTMOD-Infinite-Everything/releases/download/v%VER%/SPTMOD-Infinite-Everything-%VER%.zip"

echo Version %VER%
echo Checking the GitHub download link...
powershell -NoProfile -Command "try { $r = Invoke-WebRequest -Uri '%URL%' -Method Head -MaximumRedirection 5 -UseBasicParsing; $local=(Get-Item '%ZIP%').Length; $remote=[int64]$r.Headers['Content-Length']; if ($remote -ne $local) { Write-Host ('  WARNING: GitHub file is ' + $remote + ' bytes, local zip is ' + $local + ' bytes - run publish.bat again'); exit 2 } else { Write-Host '  OK: link works and matches the local zip'; exit 0 } } catch { Write-Host ('  NOT FOUND or check failed (' + $_.Exception.Message + ') - run publish.bat first, it creates the v%VER% release'); exit 1 }"
if errorlevel 1 ( pause & exit /b 1 )

echo %URL%| clip
echo.
echo Download link copied to the clipboard:
echo   %URL%
echo.
echo VirusTotal opens now. Upload the zip that Explorer shows, wait for the scan, then copy the page address.
start "" "https://www.virustotal.com/gui/home/upload"
explorer /select,"%ZIP%"
echo.
set "VT="
set /p "VT=Paste the VirusTotal result link here and press Enter: "
> "%HERE%_build\forge-links.txt" echo Infinite Everything %VER%
>> "%HERE%_build\forge-links.txt" echo Download: %URL%
>> "%HERE%_build\forge-links.txt" echo VirusTotal: %VT%
echo Saved to _build\forge-links.txt
echo.
echo Opening The Forge and the listing text. Fill in the fields from docs\FORGE_LISTING.md and the two links above.
start "" "https://forge.sp-tarkov.com"
start "" notepad "%HERE%docs\FORGE_LISTING.md"
start "" notepad "%HERE%_build\forge-links.txt"
pause
