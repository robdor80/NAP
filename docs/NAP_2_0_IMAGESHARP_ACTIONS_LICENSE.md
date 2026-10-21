# ImageSharp 3.2.0 — provisión privada en GitHub Actions

**Estado posterior verificado, 2026-10-20 UTC:** la provisión privada ya funcionó en [Actions 39488901573](https://github.com/robdor80/NAP/actions/runs/39488901573), Windows/Linux x64 Release, con target oficial `SixLabors_ValidateLicense` satisfactorio, cero errores, temporal eliminado y cero pruebas. No está pendiente la primera configuración del secreto. Las instrucciones/afirmaciones siguientes describen la preparación y validación local anteriores, conservadas por trazabilidad; no ejecutar sus pasos para repetir una configuración ni un CI ya verificados. El secreto debe mantenerse privado y renovarse cuando corresponda. Véase [auditoría final](NAP_2_0_FASE_5_FINAL_REVIEW.md).

## Registro histórico anterior a la validación remota

Preparación: 2026-10-20, sobre `e18afd5`, rama existente
`codex/nap-2-fase-5-automation-host`, PR #48 Draft.

Esta preparación no ejecuta compilaciones, pruebas, regresiones, ZIP de volumen,
benchmarks, cargas ni CUTOVER. No valida remotamente ImageSharp 3.2.0 ni cambia
la aceptación de Fase 5 o sus evidencias históricas. La licencia Community para
`NAP.Core`, con vencimiento el 18 de enero de 2028, permanece únicamente en el
MSI hasta que Codex local configure el secreto. Cloud no dispone de su contenido.

## Configuración preparada

`NAP.Core.csproj` permanece intacto: `SixLaborsLicenseFile` acepta un valor
externo y conserva como alternativa local `sixlabors.lic` en la raíz. El paquete
sigue en 3.2.0 y el target oficial de Six Labors permanece activo en Release.
No se proporcionan `SixLaborsLicenseKey`, supresiones ni alternativas que eviten
la validación oficial. El aprovisionamiento no valida por sí mismo la licencia:
una clave caducada, inválida o para otro ensamblado debe fallar en el target oficial.

Los comandos que compilan en `ci.yml` y `cutover-laboratory.yml` pasan por
`.github/scripts/Invoke-LicensedDotnet.ps1`, con PowerShell 7 tanto en
`ubuntu-latest` como en `windows-2022`. Incluye los dos `dotnet test` de
coordinación que compilan implícitamente; los comandos `--no-build` no necesitan
la licencia. Los filtros y assertions existentes se conservan. La comprobación
independiente de recuperación Git sigue pudiendo ejecutarse tras otros fallos,
pero requiere que haya terminado correctamente la compilación inicial con licencia
y que no se haya cancelado el job.

El workflow CUTOVER conserva su disparador exclusivamente manual y su entrada
explícita de autorización, que continúa en `false` por defecto. Adaptar su
compilación no autoriza ni ejecuta ese laboratorio. No se añaden disparadores,
jobs, permisos, runners propios ni workflows nuevos.

## Protección del secreto y del archivo

- `SIXLABORS_LICENSE_B64` es un **secreto de Actions del repositorio**, nunca una
  variable pública. Base64 solo transporta los bytes; no los cifra por sí mismo.
- Se inyecta exclusivamente en los pasos que compilan. El helper lo elimina de
  su entorno antes de iniciar `dotnet`, para que MSBuild y los procesos hijos
  reciban únicamente `SixLaborsLicenseFile` con la ruta temporal.
- Falta de secreto (incluidos forks y contextos sin acceso), Base64 incorrecto
  o archivo vacío impiden iniciar `dotnet`. No se busca una licencia alternativa
  en el checkout ni se intenta eludir el validador.
- El archivo exacto se reconstruye en
  `$RUNNER_TEMP/nap-sixlabors-license/sixlabors.lic`, fuera del checkout, salidas,
  fixtures y artefactos. En Linux, el directorio nace con acceso solo para el
  usuario del runner. En Windows hereda la protección del directorio temporal
  del runner hospedado desechable.
- Se registran máscaras para Base64, contenido, líneas y tokens largos antes
  de iniciar la compilación. Los errores de provisión no vuelcan excepciones,
  variables ni contenido. No se escriben secretos en `GITHUB_ENV`, `GITHUB_OUTPUT`,
  parámetros de línea de comandos, resúmenes o archivos de diagnóstico.
- Se usa verbosidad mínima y se deshabilita la reutilización de nodos MSBuild.
  El helper rechaza `RUNNER_DEBUG=1`. No activar trazas de PowerShell, volcado
  de entorno, `--verbosity diagnostic`, `-bl`, logs binarios ni depuración de
  Actions al utilizar la licencia. Las advertencias normales de NuGet no se suprimen.
- `finally` elimina el archivo y directorio al terminar cada comando, también
  cuando `dotnet` devuelve error. Cada job añade una limpieza con `always()`
  antes de la recolección de artefactos. Una interrupción forzada puede impedir
  ejecutar código de limpieza; el runner hospedado desechable es la última
  barrera. No reutilizar este helper en runners persistentes sin revisar su ciclo
  de vida y controles de acceso.
- Los artefactos siguen limitados a sus rutas de resultados originales. No se
  suben el temporal, el checkout completo ni salidas de compilación. `.gitignore`
  sigue excluyendo `sixlabors.lic` y sus variantes. El secreto no debe incluirse
  en commits, comentarios, capturas ni evidencias.

Solo debe facilitarse el secreto a código de confianza y previamente revisado.
No adaptar esta provisión a `pull_request_target` para ejecutar código de forks.
La renovación requiere reemplazar el secreto desde el original renovado antes
del vencimiento, sin cambiar el nombre del ensamblado autorizado.

## Pasos pendientes para Codex LOCAL en el MSI

1. Usar el archivo original privado, sin copiarlo al chat, al repositorio o a un
   archivo Base64 intermedio. No solicitar ni imprimir su contenido.
2. Confirmar que `gh` ya está autenticado con la cuenta autorizada para administrar
   secretos de `robdor80/NAP`: `gh auth status`. Si faltan credenciales o acceso,
   detenerse y comunicarlo; no cambiar permisos, cuentas ni protecciones.
3. En una consola PowerShell local sin transcripción ni depuración, ajustar solo
   la ruta privada y ejecutar este bloque. No sustituir `$licenseB64` por el valor
   literal ni usar `--body` en la línea de comandos:

   ```powershell
   $ErrorActionPreference = 'Stop'
   $licensePath = 'C:\RUTA_PRIVADA\sixlabors.lic'
   $licenseBytes = $null
   $licenseB64 = $null
   try {
       $licenseBytes = [IO.File]::ReadAllBytes($licensePath)
       if ($licenseBytes.Length -eq 0) { throw 'El archivo de licencia está vacío.' }
       $licenseB64 = [Convert]::ToBase64String($licenseBytes)
       if ($licenseB64.Length -gt 49152) { throw 'El valor supera el límite de un secreto de Actions.' }
       $licenseB64 | gh secret set SIXLABORS_LICENSE_B64 --repo robdor80/NAP
       if ($LASTEXITCODE -ne 0) { throw 'No se pudo configurar el secreto; no ejecutar Actions.' }
   }
   finally {
       if ($null -ne $licenseBytes) { [Array]::Clear($licenseBytes, 0, $licenseBytes.Length) }
       $licenseBytes = $null
       $licenseB64 = $null
   }
   ```

   `gh secret set` lee Base64 por entrada estándar y cifra el secreto para GitHub.
   No se crea una copia pública ni un archivo auxiliar. No usar `Write-Host`,
   `echo`, `Get-Content` ni portapapeles para mostrar o transferir la licencia.
4. Comprobar únicamente nombre y fecha de actualización:

   ```powershell
   gh secret list --repo robdor80/NAP
   if ($LASTEXITCODE -ne 0) { throw 'No se pudo consultar la lista de secretos.' }
   ```

   Debe aparecer `SIXLABORS_LICENSE_B64`. GitHub no devuelve su valor. Esta
   comprobación confirma su registro, no su validez ni una compilación remota.
5. Informar de la configuración sin contenido, identificadores, firmas ni hash
   de la licencia. Configurar un secreto no dispara por sí solo GitHub Actions.
   No lanzar CI, reejecutar runs anteriores ni publicar un commit de prueba.

## Publicación y validación pendiente

La revisión de esta preparación se limita a análisis estático: `actionlint`
1.7.12 sobre ambos workflows, parser AST de PowerShell sobre el helper y los
21 comandos `pwsh` de los workflows, lectura YAML/XML y `git diff --check`.
Se comprobó que se mantienen disparadores, permisos, jobs, matrices, rutas de
artefactos y configuración del proyecto. No se ejecutó el helper ni ningún
comando de los workflows, y no se usó una licencia real o simulada. Las ramas
de fallo, la limpieza y la compilación con el secreto no están validadas en ejecución.

`ci.yml` se activa con `push` a `main` y con `pull_request` contra `main`.
Actualizar esta rama normalmente produciría `pull_request/synchronize` y
ejecutaría regresiones fuera de lo autorizado. Todo commit de esta preparación
debe llevar **`[skip ci]`**, reconocido por GitHub para esos eventos. Revisar de
nuevo todos los workflows y el estado remoto inmediatamente antes del push.
No publicar si aparecen otros disparadores automáticos que puedan ejecutar
pruebas y no respeten esa omisión. No usar un push normal posterior ni marcar
la PR como lista para revisión para intentar validar esta configuración.

La PR #48 debe seguir abierta y Draft, en la misma rama; no se toca `main` ni se
crea una segunda PR. No hay nuevas evidencias de compilación remota. Después de
registrar el secreto, falta autorización explícita de una ejecución concreta y
su alcance. La prohibición vigente de volumen, benchmarks, cargas, regresiones
generales y CI completa sigue activa; disponer del secreto no la levanta. Una
eventual validación limitada a compilación requiere un procedimiento separado
que no active las baterías del workflow principal.
