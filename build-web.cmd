@echo off
REM Builds the Blazor web project using the user-local .NET 8 SDK.
REM The system-wide C:\Program Files\dotnet only ships runtimes (no SDK), so we
REM resolve the SDK-bearing dotnet from %LOCALAPPDATA% to make builds work without admin.
set "DOTNET_EXE=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if not exist "%DOTNET_EXE%" set "DOTNET_EXE=dotnet"
"%DOTNET_EXE%" --list-sdks
"%DOTNET_EXE%" build "%~dp0src\DBAIAzure.Web\DBAIAzure.Web.csproj"
exit /b %ERRORLEVEL%
