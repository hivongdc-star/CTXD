@echo off
setlocal EnableDelayedExpansion
set "PROJECT=%~dp0..\Client\Unity"
set "UNITY="
for /f "delims=" %%D in ('dir /b /ad /o-n "C:\Program Files\Unity\Hub\Editor" 2^>nul') do if not defined UNITY if exist "C:\Program Files\Unity\Hub\Editor\%%D\Editor\Unity.exe" set "UNITY=C:\Program Files\Unity\Hub\Editor\%%D\Editor\Unity.exe"
if not defined UNITY (echo [ERROR] Unity Editor not found under Unity Hub. & exit /b 1)
"%UNITY%" -batchmode -quit -projectPath "%PROJECT%" -executeMethod CTXD.Client.Editor.CTXDBuild.BuildAndroid -logFile "%~dp0..\Build\unity_android.log"
if errorlevel 1 (echo [ERROR] Unity Android build failed. See Build\unity_android.log & exit /b 1)
echo [OK] Android build: Client\Unity\Build\Android\CTXD.apk
