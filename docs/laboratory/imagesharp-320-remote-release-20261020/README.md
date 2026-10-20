# ImageSharp 3.2.0 — validación remota Release aislada

Validación terminada el 20 de octubre de 2026 para `robdor80/NAP`, PR #48.

**Resultado: dos compilaciones Release x64 correctas de `NAP.sln`, cero errores y validación oficial de licencia completada para `NAP.Core` en Windows y Linux. Cero pruebas ejecutadas.**

| Plataforma | Resultado | Job | Artefacto público |
|---|---|---|---|
| Windows x64 (`windows-2022`) | success | [118856456781](https://github.com/robdor80/NAP/actions/runs/39488901573/job/118856456781) | [12293959147](https://github.com/robdor80/NAP/actions/runs/39488901573/artifacts/12293959147) |
| Linux x64 (`ubuntu-24.04`) | success | [118856457075](https://github.com/robdor80/NAP/actions/runs/39488901573/job/118856457075) | [12294203375](https://github.com/robdor80/NAP/actions/runs/39488901573/artifacts/12294203375) |

[Ejecución 39488901573](https://github.com/robdor80/NAP/actions/runs/39488901573), intento 1, commit de laboratorio `cd211415cf292c17dc3ab212d6f89465a4f1b6fc`. Código de la PR: `fd7c4ff664cb0a945bf53e6898b75352faf1d479`. Rama: `lab/imagesharp-320-release-fd7c4ff-20261020`, sin segunda PR.

## Procedimiento y evidencia

Cada runner x64 ejecutó exactamente una invocación `dotnet build NAP.sln -c Release -p:PlatformTarget=x64` a través del helper original `.github/scripts/Invoke-LicensedDotnet.ps1`, con el secreto existente `SIXLABORS_LICENSE_B64`. La restauración está incluida en `build`. Se importó adicionalmente el observador de laboratorio `.github/laboratory/ImageSharpValidation.targets` mediante `CustomAfterMicrosoftCommonTargets`.

El observador se ejecuta después de `SixLabors_ValidateLicense` y exige Release, x64 y compilación real. No redefine el target, la tarea, la clave pública ni sus condiciones. El target oficial de ImageSharp 3.2.0 ejecuta `ValidateLicenseTask` antes de `CoreCompile`; en Release no permite continuar tras un error. En ambos logs aparece una vez `LAB EVIDENCE: SixLabors_ValidateLicense completed successfully for NAP.Core in Release x64.`, seguido de `Build succeeded.` y `0 Error(s)`.

Los pasos posteriores comprobaron el paquete `SixLabors.ImageSharp/3.2.0` resuelto, su import oficial y la ausencia del directorio temporal de licencia. El helper elimina el secreto de su entorno antes de invocar MSBuild, enmascara su contenido y elimina el archivo temporal en `finally`; cada job ejecutó además la limpieza explícita con `always()`. Los artefactos incluyen únicamente `official-validator.txt` y `result.json`; este último registra versión/hash de la DLL de ImageSharp, SDK, arquitectura y procedencia. No se archivaron licencias, binlogs, salidas de compilación ni logs completos.

El SDK efectivo fue **10.0.401** en ambos runners (selección efectiva del entorno hospedado, aunque setup-dotnet instaló 8.0.x); los proyectos mantienen `net8.0` y `net8.0-windows`. La compilación usa `PlatformTarget=x64`, sin `publish` ni RID de distribución. En Linux, WPF solo se compiló mediante `EnableWindowsTargeting`; no se ejecutó. Este resultado certifica compilación y validador, sin ampliar la aceptación funcional, de distribución o productiva.

## Aislamiento comprobado

Antes de cada push se revisaron el estado remoto y los disparadores. La CI general solo acepta `push` a `main` y `pull_request` contra `main`. El workflow CUTOVER solo acepta `workflow_dispatch` y no fue invocado. No hay disparadores `create` ni `workflow_run`. El workflow de laboratorio solo acepta `push` a su rama exacta y cambios en su propio archivo; el job repite la restricción de evento y rama.

Comparando los inventarios de runs anterior y posterior solo hay dos runs nuevos, ambos del workflow de laboratorio: el preflight fallido y la validación correcta. Solo dos pasos de compilación se iniciaron en total. No se ejecutaron pruebas ZIP, regresiones, las 3784 pruebas, CUTOVER, benchmarks ni estrés.

`main` permanece en `cd7532f011eea8a72e89848234b259f1f5293e15`. La PR #48 permanece abierta, Draft y en `fd7c4ff664cb0a945bf53e6898b75352faf1d479`, sin fusión ni cierre. Código, pruebas, solución, helper y workflows heredados son idénticos al HEAD de la PR. Los únicos archivos añadidos antes de compilar fueron el workflow y el observador de laboratorio. Este informe se incorpora después, en la misma rama; su ruta no coincide con el filtro del workflow.

## Incidencia de preparación conservada

[Run inicial 39488764661](https://github.com/robdor80/NAP/actions/runs/39488764661): ambos jobs fallaron en la comprobación de procedencia porque `checkout` tenía profundidad 1 y no incluía el commit fuente. **Ambos pasos de compilación quedaron `skipped`**; no hubo compilaciones ni pruebas en ese intento. Se corrigió únicamente `fetch-depth: 0` antes de lanzar el run satisfactorio. El fallo inicial no se borra ni se presenta como validación.

## Avisos y conservación

Cada compilación muestra diez apariciones NU1902/NU1903, correspondientes a los cinco advisories distintos ya conocidos, repetidos durante restore/build. No se añadieron supresiones ni se declara limpio el análisis de vulnerabilidades. Los identificadores figuran en `validation.json`. La comprobación remota de `gh secret list` devolvió 403 por el token de integración; la ejecución real del helper y el target oficial comprobó la disponibilidad del secreto sin solicitar su valor.

Los artefactos tienen retención de 90 días, con caducidad registrada en `validation.json`. Los extractos públicos de logs y los metadatos de jobs/artefactos se conservan también en este directorio. No se incluye contenido, identificador, firma ni hash de la licencia. La rama se conserva como evidencia de laboratorio; la PR y `main` no reciben cambios.
