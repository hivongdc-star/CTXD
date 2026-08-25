@echo off
setlocal
cd /d "%~dp0.."
where dotnet >nul 2>nul || (echo [ERROR] .NET SDK not found & pause & exit /b 1)
dotnet run --project Server\CTXD.Server\CTXD.Server.csproj
