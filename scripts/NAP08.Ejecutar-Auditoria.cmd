@echo off
setlocal
title NAP08 - Auditoria de seguridad (sin instalar)
cd /d "%~dp0"
echo.
echo ============================================================
echo   NAP08 - AUDITORIA PREVIA EN WINDOWS 11
echo   No instala NAP, no inicia Host ni modifica bibliotecas.
echo   Se genera un informe JSON en Descargas del usuario actual.
echo ============================================================
echo.
if not exist "%~dp0NAP08.PhysicalAudit.ps1" (
  echo ERROR: falta NAP08.PhysicalAudit.ps1 en esta misma carpeta.
  echo No se ha ejecutado ninguna prueba.
  goto :end
)
if not exist "%~dp0base\output\NAP-Setup-2.0.0.exe" (
  echo ERROR: falta base\output\NAP-Setup-2.0.0.exe
  echo No se ha ejecutado ninguna prueba.
  goto :end
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0NAP08.PhysicalAudit.ps1" -Etapa Antes -SetupPath "%~dp0base\output\NAP-Setup-2.0.0.exe" -TrustedSha256 "acba41477de7b266fa8203977f9461986b88c5bb0aa5659f4852de9c52ac940a"
set "resultado=%ERRORLEVEL%"
echo.
if "%resultado%"=="0" (
  echo AUDITORIA AUTOMATICA SIN BLOQUEOS. No significa autorizacion para instalar.
) else (
  echo AUDITORIA BLOQUEADA O FALLIDA. No instales NAP.
)
echo.
echo Resultado de PowerShell: %resultado%
echo Archivo de evidencia: %%USERPROFILE%%\Downloads\NAP08_Evidencias
:end
echo.
echo Pulsa cualquier tecla para cerrar esta ventana.
pause >nul
endlocal
