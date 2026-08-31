@echo off
setlocal
where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK not found. Install from https://dotnet.microsoft.com/download/dotnet/8.0
    exit /b 1
)
dotnet restore PortTerminator.sln
dotnet build PortTerminator.sln -c Release
if errorlevel 1 exit /b 1
echo.
echo Build succeeded. Run:
echo   dotnet run --project src\PortTerminator.UI\PortTerminator.UI.csproj
endlocal
