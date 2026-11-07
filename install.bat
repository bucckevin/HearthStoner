@echo off
rem Installs HearthStoner for your Windows user (no admin): builds one HearthStoner.exe, then lets it install itself
rem to %LOCALAPPDATA%\Programs\HearthStoner with Start menu and desktop shortcuts. Run it again to update.
rem --no-start: don't start the app afterwards. Your data in %LOCALAPPDATA%\HearthStoner is not touched.
setlocal
cd /d "%~dp0"
set "PUB=%TEMP%\HearthStoner-publish"
rmdir /s /q "%PUB%" 2>nul
echo Building HearthStoner ...
dotnet publish HearthStoner.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%PUB%" -nologo > install.log 2>&1
if errorlevel 1 (
  echo The build failed. Details are in install.log.
  exit /b 1
)
"%PUB%\HearthStoner.exe" install %*
set "RC=%errorlevel%"
rmdir /s /q "%PUB%" 2>nul
exit /b %RC%
