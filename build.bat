@echo off
rem Rebuilds "Chest Counter.exe" and app\ChestStats.exe from this source folder. Needs the .NET 9 SDK.
rem Close Chest Counter (tray icon > Exit) first, or the exe can't be replaced.
setlocal
set "ROOT=%~dp0.."
set "OUT=%~dp0publish"
dotnet publish "%~dp0ChestCounter\ChestCounter.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o "%OUT%\app" || goto :fail
dotnet publish "%~dp0SquadChests.Cli\SquadChests.Cli.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o "%OUT%\cli" || goto :fail
copy /y "%OUT%\app\ChestCounter.exe" "%ROOT%\Chest Counter.exe" >nul || goto :fail
if not exist "%ROOT%\app" mkdir "%ROOT%\app"
copy /y "%OUT%\cli\ChestStats.exe" "%ROOT%\app\ChestStats.exe" >nul || goto :fail
copy /y "%~dp0dist\README.txt" "%ROOT%\README.txt" >nul || goto :fail
copy /y "%~dp0dist\Chest Stats.bat" "%ROOT%\Chest Stats.bat" >nul || goto :fail
echo Done.
exit /b 0
:fail
echo Build failed. If Chest Counter is running, exit it from the tray icon and try again.
exit /b 1
