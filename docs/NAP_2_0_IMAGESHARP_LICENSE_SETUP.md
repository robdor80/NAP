# ImageSharp 3.2.0 — licencia local y validación dirigida

**Estado posterior verificado, 2026-10-20 UTC:** la provisión privada ya funcionó en [Actions 39488901573](https://github.com/robdor80/NAP/actions/runs/39488901573), Windows/Linux x64 Release, con target oficial `SixLabors_ValidateLicense` satisfactorio, cero errores, temporal eliminado y cero pruebas. No está pendiente la primera configuración del secreto. Las instrucciones/afirmaciones siguientes describen la preparación y validación local anteriores, conservadas por trazabilidad; no ejecutar sus pasos para repetir una configuración ni un CI ya verificados. El secreto debe mantenerse privado y renovarse cuando corresponda. Véase [auditoría final](NAP_2_0_FASE_5_FINAL_REVIEW.md).

## Registro histórico anterior a la validación remota

2026-10-20. El responsable aportó una licencia Community oficial limitada a ensamblados. La lectura privada confirma tipo Community, formato assembly-scoped, vigencia y caducidad en enero de 2028. **El build Release completo valida oficialmente la licencia para el ensamblado efectivo `NAP.Core` y termina con exit 0.** No se publica su contenido, identificador, firmas ni hash.

## Configuración aplicada

`src/NAP.Core/NAP.Core.csproj` referencia ImageSharp 3.2.0 y define `SixLaborsLicenseFile` al archivo `sixlabors.lic` de la raíz, solo si el responsable no proporcionó otro valor. Core es el único proyecto que referencia ImageSharp directamente; los otros siete lo reciben transitivamente. No se añade una referencia directa de licencia a otros proyectos ni se elude el target oficial.

La ruta explícita es necesaria porque el mecanismo del paquete busca por defecto desde el directorio del proyecto hacia sus descendientes, y la raíz del repositorio queda por encima de `src/NAP.Core`. El archivo se usa tal como fue entregado, sin editarlo ni incrustarlo en el proyecto. `.gitignore` excluye `sixlabors.lic` y variantes con sufijo. El archivo no está versionado y no hay copias bajo src/tests/tools ni en las salidas de compilación.

## Distribución de la licencia

Según la [documentación oficial de ImageSharp](https://docs.sixlabors.com/articles/imagesharp/index.html), las claves Community limitadas a ensamblados pueden compartirse en un repositorio público para compilar los nombres registrados. Las Community sin limitación y las comerciales deben permanecer privadas. El [anuncio oficial del 9 de octubre de 2026](https://sixlabors.com/posts/security-support-and-license-enforcement/) aclara el uso por colaboradores, forks que conservan esos nombres y CI, y mantiene la caducidad/renovación de la clave. No constituye una clave genérica para otros ensamblados.

La licencia local tiene formato assembly-scoped y el validador oficial la acepta para `NAP.Core`. **Aun permitiéndolo esas condiciones publicadas, la orden vigente prohíbe publicarla y se respeta.** No se ha subido como archivo, secreto de GitHub, contenido de PR, log o artefacto de evidencia. Tampoco se ha enviado a Six Labors ni se han solicitado licencias adicionales.

La biblioteca conserva Six Labors Split License 1.0; el control de compilación no reemplaza sus condiciones de uso y redistribución. Esta revisión confirma las reglas de distribución de la clave; no amplía los ensamblados ni las condiciones concedidas. Fuentes adicionales: [paquete oficial de validación](https://www.nuget.org/packages/SixLabors.Licensing/1.0.3), [licencia de ImageSharp 3.2.0](https://github.com/SixLabors/ImageSharp/blob/v3.2.0/LICENSE).

## Resultado ejecutado

- `dotnet build NAP.sln -c Release`: exit 0, cero errores; validación oficial sin errores de licencia. No se usa una clave de entorno ni un override del validador.
- 220/220 pruebas dirigidas, cero fallos/omisiones: normalización y seguridad, convertidores PNG a WebP de portrait/scene, validadores WebP y thumbnails. Incluyen píxeles/alpha/16 bits/metadatos, entradas corruptas, límites y conservación de fuentes/caché.
- La DLL de tests tiene versión `3.2.0+baa37cd19077b2a2a0c5667535111cf31d48c019` y su SHA256 coincide con la DLL del paquete 3.2.0 resuelto. Se actualizaron directa y transitivas en los ocho proyectos.
- Ningún ZIP de volumen, prueba de carga, benchmark, regresión general, proceso de host/IA, WPF ni CUTOVER se ejecutó en esta actualización. Se preservaron las exclusiones anteriores de volumen.

Los cinco avisos NU1902/NU1903 permanecen visibles. El anuncio oficial confirma que los avisos de los backports corregidos siguen pendientes de revisión en GitHub Advisory Database/NuGet. No se añaden supresiones ni se presenta el scan como limpio. Las pruebas funcionales no equivalen a ejecutar todos los PoC de seguridad.

## CI y auditoría

Las 220 pruebas y la compilación son **locales** sobre la actualización. El CI completo previo en `1bef7ec` corresponde a ImageSharp 3.1.12 y se conserva como histórico; no certifica 3.2.0. La actualización se publica con `[skip ci]` para respetar la orden de ejecutar solo imágenes y mantener privada la licencia. El archivo local no existe en los runners: un futuro CI Release necesitará una decisión expresa del responsable sobre distribución de esta clave o su provisión segura. No se deshabilita ni falsifica el control de licencia.

La observación IPC local de Fase 5 sigue sin causa demostrada y no se investiga en esta tarea. PR #48 permanece Draft. Main, producción, datos reales de Nimroel, cuentas/permisos y servicios no se modifican; no hay fusión.

## Preparación posterior de GitHub Actions (sin ejecución)

La provisión privada mediante el secreto `SIXLABORS_LICENSE_B64` queda preparada
según [el procedimiento de Actions y configuración desde el MSI](NAP_2_0_IMAGESHARP_ACTIONS_LICENSE.md).
Esta autorización posterior permite configurar el secreto desde el original local;
no autoriza publicar la licencia ni ejecutar CI o nuevas pruebas. Los resultados
anteriores siguen siendo locales e históricos: no se declara validada una
compilación remota de ImageSharp 3.2.0.
