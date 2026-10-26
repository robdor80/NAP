@echo off
setlocal
if not exist "%~dp0NAP.App.exe" (
  echo No se encuentra NAP. Descomprime todo el ZIP antes de iniciar la prueba.
  pause
  exit /b 1
)
start "" /wait "%~dp0NAP.App.exe" --phase6-preview=acceptance
if errorlevel 1 (
  echo La prueba no pudo iniciarse de forma segura. No se ha activado produccion.
  echo Comunica el problema a ChatGPT junto con una captura de esta ventana.
  pause
  exit /b 1
)
exit /b 0
