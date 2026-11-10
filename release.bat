@echo off
rem Builds the version to share with friends: ..\release\ChestCounter-win-x64.zip (portable)
rem and ..\release\ChestCounter-Setup.exe (installer, only when Inno Setup 6 is installed).
rem Self-contained (no .NET needed), with an EMPTY data folder: your own games stay on your PC.
setlocal
set "ROOT=%~dp0.."
set "OUT=%ROOT%\release\Chest Counter"
set "OPTS=-c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none"
if exist "%ROOT%\release" rmdir /s /q "%ROOT%\release"
dotnet publish "%~dp0ChestCounter\ChestCounter.csproj" %OPTS% -o "%~dp0publish\rel-app" || goto :fail
dotnet publish "%~dp0SquadChests.Cli\SquadChests.Cli.csproj" %OPTS% -o "%~dp0publish\rel-cli" || goto :fail
mkdir "%OUT%\app" || goto :fail
copy /y "%~dp0publish\rel-app\ChestCounter.exe" "%OUT%\Chest Counter.exe" >nul || goto :fail
copy /y "%~dp0publish\rel-cli\ChestStats.exe" "%OUT%\app\ChestStats.exe" >nul || goto :fail
copy /y "%~dp0dist\Chest Stats.bat" "%OUT%\" >nul || goto :fail
copy /y "%~dp0dist\README.txt" "%OUT%\" >nul || goto :fail
copy /y "%~dp0vendor\FortniteReplayDecompressor\LICENSE" "%OUT%\app\LICENSE-FortniteReplayDecompressor.txt" >nul || goto :fail
rmdir /s /q "%~dp0publish"
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%' -DestinationPath '%ROOT%\release\ChestCounter-win-x64.zip' -Force" || goto :fail
echo Done: release\ChestCounter-win-x64.zip

rem The installer (same files, plus a Start menu entry and an Uninstall button). Needs Inno Setup 6,
rem https://jrsoftware.org/isinfo.php ; without it this step is skipped and only the zip is built.
set "ISCC="
for %%P in ("%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe") do if not defined ISCC if exist "%%~P" set "ISCC=%%~P"
if not defined ISCC goto :noinno
"%ISCC%" /Qp "%~dp0installer\ChestCounter.iss" || goto :fail
echo Done: release\ChestCounter-Setup.exe
exit /b 0
:noinno
echo Inno Setup 6 isn't installed: skipped release\ChestCounter-Setup.exe
exit /b 0
:fail
echo Release build failed.
exit /b 1
