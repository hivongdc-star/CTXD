@echo off
setlocal EnableDelayedExpansion
set "PROJECT=%~dp0..\Client\Unity"
set "UNITY="
for /f "delims=" %%D in ('dir /b /ad /o-n "C:\Program Files\Unity\Hub\Editor" 2^>nul') do if not defined UNITY if exist "C:\Program Files\Unity\Hub\Editor\%%D\Editor\Unity.exe" set "UNITY=C:\Program Files\Unity\Hub\Editor\%%D\Editor\Unity.exe"
if not defined UNITY (echo [ERROR] Unity Editor not found under Unity Hub. & exit /b 1)
"%UNITY%" -batchmode -quit -projectPath "%PROJECT%" -executeMethod CTXD.Client.Editor.CTXDBuild.BuildWindows -logFile "%~dp0..\Build\unity_windows.log"
if errorlevel 1 (echo [ERROR] Unity Windows build failed. See Build\unity_windows.log & exit /b 1)
echo [OK] Windows build: Client\Unity\Build\Windows\CTXD.exe
