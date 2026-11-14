@echo off
setlocal
title NAP08 - Conectar AutomationHost
cd /d "%~dp0"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0NAP08.ConectarHostLab.ps1"
if errorlevel 1 (
  echo.
  echo No se ha podido abrir el Host de laboratorio. El error aparece arriba.
  echo Copia el mensaje o envianos una captura. No vuelvas a instalar NAP.
  echo.
  pause
)
endlocal
