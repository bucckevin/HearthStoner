@echo off
cd /d "%~dp0"
echo === dotnet info === > build.log
dotnet --list-sdks >> build.log 2>&1
echo === build === >> build.log
dotnet build HearthStoner.slnx -c Release -nologo >> build.log 2>&1
set RC=%ERRORLEVEL%
echo EXIT %RC% >> build.log
rem A picture of the overlay at turn 12 of your latest game, so changes to the look can be checked.
if %RC%==0 bin\Release\net10.0-windows\HearthStoner.exe preview screenshots\preview.png >> build.log 2>&1
