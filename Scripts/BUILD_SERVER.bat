@echo off
setlocal
cd /d "%~dp0.."
where dotnet >nul 2>nul || (echo [ERROR] .NET SDK not found & exit /b 1)
dotnet build Server\CTXD.Server\CTXD.Server.csproj -c Release || exit /b 1
dotnet build Server\CTXD.Admin\CTXD.Admin.csproj -c Release || exit /b 1
echo [OK] Server + Admin build completed.
