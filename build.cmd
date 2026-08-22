@echo off
setlocal
set CONFIGURATION=%~1
if "%CONFIGURATION%"=="" set CONFIGURATION=Staging

dotnet clean Icod.LineEditor.sln -c %CONFIGURATION% || exit /b %errorlevel%
dotnet restore Icod.LineEditor.sln || exit /b %errorlevel%
dotnet build Icod.LineEditor.sln -c %CONFIGURATION% --no-restore || exit /b %errorlevel%
dotnet test Icod.LineEditor.sln -c %CONFIGURATION% --no-build || exit /b %errorlevel%
endlocal