@echo off
setlocal
title NAP08 - Ejecutar auditoria del usuario de laboratorio desde andro
echo.
echo NAP08 - Auditoria aislada desde el escritorio habitual
echo.
echo Este lanzador NO instala NAP ni modifica configuraciones.
echo Usa la cuenta estandar NAP08_Lab para obtener su AppData propio.
echo.
set "LAB=C:\Users\Public\NAP08"
if not exist "%LAB%\NAP08.PhysicalAudit.ps1" (
  echo ERROR: falta auditor original en C:\Users\Public\NAP08
  goto :end
)
if not exist "%LAB%\base\output\NAP-Setup-2.0.0.exe" (
  echo ERROR: falta instalador de laboratorio.
  goto :end
)
if not exist "%LAB%\NAP08.Ejecutar-Auditoria.cmd" (
  copy /Y "%~dp0NAP08.Ejecutar-Auditoria.cmd" "%LAB%\NAP08.Ejecutar-Auditoria.cmd" >nul
  if errorlevel 1 (
    echo ERROR: no se pudo copiar el lanzador publico.
    goto :end
  )
)
echo.
echo Windows solicitara ahora la contrasena de NAP08_Lab (no se mostrara mientras escribes).
echo NO introduzcas la contrasena de la cuenta andro.
echo.
runas /profile /user:%COMPUTERNAME%\NAP08_Lab "cmd.exe /c C:\Users\Public\NAP08\NAP08.Ejecutar-Auditoria.cmd"
if errorlevel 1 (
  echo.
  echo No se pudo iniciar la auditoria bajo NAP08_Lab. No se ha instalado NAP.
) else (
  echo.
  echo Se ha solicitado la auditoria a la cuenta de laboratorio.
  echo Consulta la ventana que se abrira con su resultado.
)
:end
echo.
pause
endlocal
