# NAP 2.0 — P6.3/P6.4, pulido final

Fuente verificada: `91f94b972ec5a1daa7efc718509ffb3ee97e7410`. [Builds Release y 43/43 pruebas](https://github.com/robdor80/NAP/actions/runs/39839852706). [Informe completo](../NAP_2_0_FASE_6_PULIDO_FINAL.md) · [PDF](Informe-revision.pdf) · [TRX original](phase6-polish.trx) · [Manifiesto y hashes](evidence.json).

25 PNG nativos del árbol WPF de producto a 96 DPI, con datos e imágenes exclusivamente sintéticos y adaptadores sin escritor. El layout fuera de pantalla no está limitado por el escritorio físico del runner. No se han recortado, recompuesto ni retocado las capturas. No son una certificación del MSI.

| Zona | 1440 × 900 | 1200 × 700 |
| --- | --- | --- |
| Comparación ampliada | [Abrir](comparacion-ampliada-1440x900.png) | [Abrir](comparacion-ampliada-1200x700.png) |
| Zoom al 200% | [Abrir](comparacion-zoom-200-1440x900.png) | [Abrir](comparacion-zoom-200-1200x700.png) |
| Producción: actividad e incidencia simultáneas | [Abrir](flujo-production-1440x900.png) | [Abrir](flujo-production-1200x700.png) |
| Resolución: detalle inicial | [Abrir](flujo-exception-1440x900.png) | [Abrir](flujo-exception-1200x700.png) |
| Resolución: zona inferior | [Abrir](flujo-exception-detalle-1440x900.png) | [Abrir](flujo-exception-detalle-1200x700.png) |
| Actividad: 24 eventos y 3 decisiones | [Abrir](flujo-activity-1440x900.png) | [Abrir](flujo-activity-1200x700.png) |
| Actividad: decisiones conservadas al paginar eventos | [Abrir](flujo-activity-decisiones-1440x900.png) | [Abrir](flujo-activity-decisiones-1200x700.png) |
| Actividad: últimos 4 eventos | [Abrir](flujo-activity-actividad-1440x900.png) | [Abrir](flujo-activity-actividad-1200x700.png) |
| Actividad: 24 eventos y 24 decisiones | [Abrir](flujo-activity-decisions-1440x900.png) | [Abrir](flujo-activity-decisions-1200x700.png) |
| Decisiones: 1–20 con continuidad propia | [Abrir](flujo-activity-decisions-decisiones-1440x900.png) | [Abrir](flujo-activity-decisions-decisiones-1200x700.png) |
| Eventos: 21–24 sin más eventos | [Abrir](flujo-activity-decisions-actividad-1440x900.png) | [Abrir](flujo-activity-decisions-actividad-1200x700.png) |
| Inicio conservado | [Abrir](inicio-review-1440x900.png) | [Abrir](inicio-review-1200x700.png) |

[Herramientas compactas hasta RoDo](inicio-review-herramientas-1200x700.png).

La comparación conserva las proporciones. En zoom al 200% se consulta una parte del lienzo mediante las barras horizontal y vertical; los datos geométricos y Sin recorte quedan fijos. Las franjas amarillas son guías opcionales de márgenes declarados, no píxeles modificados. La ampliación utiliza las miniaturas verificadas de hasta 256 píxeles por eje y no recupera detalle original adicional.

Producción e Historial comparten el mismo conjunto sintético para las filas y el resumen: un pendiente, uno procesando, uno completado y una incidencia. «Procesando imágenes» tiene prioridad. Las páginas de eventos y decisiones tienen posiciones, continuidad y controles propios; los límites superiores mantienen estable una consulta abierta ante registros nuevos.

## Reproducción acotada

Usar el SHA de fuente indicado y la validación oficial existente para compilar Release en Windows. No copiar licencias a las evidencias ni deshabilitar la validación. El [workflow de rama](../../.github/workflows/phase6-review.yml) contiene el filtro exacto, que excluye el caso funcional ZIP previo. No ejecutar `dotnet test NAP.sln` ni fixtures de volumen.

```powershell
$appPath = '.\src\NAP.App\bin\Release\net8.0-windows\NAP.App.exe'
$captureRoot = Join-Path $PWD 'artifacts\phase6-polish'
foreach ($mode in @('review','production','exception','activity','activity-decisions')) {
  foreach ($compact in @($false,$true)) {
    $captureArguments = @("--phase6-preview=$mode", "--phase6-output=$captureRoot")
    if ($compact) { $captureArguments += '--compact' }
    $process = Start-Process $appPath -ArgumentList $captureArguments -PassThru
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Preview timed out' }
    if ($process.ExitCode -ne 0) { throw "Preview failed: $mode" }
  }
}
```

No añadir configuración de automatización ni cargar universos reales. El modo de captura solo escribe el destino solicitado. Cada proceso falla ante bindings erróneos, datos contradictorios, paginación incoherente, recorte del renderizado, ausencia de guías, falta de desplazamiento al ampliar o cambios en los buffers.

## Cronología íntegra

| Ejecución | Fuente | Pruebas | Resultado de render/revisión |
| --- | --- | --- | --- |
| [39836835384](https://github.com/robdor80/NAP/actions/runs/39836835384) | `8428cb3` | 40/43 | Tres fixtures invocaban el detalle con intentos sin recibo de admisión. Guarda la evidencia original; se corrigió el nivel de prueba, sin relajar admisión. |
| [39837383866](https://github.com/robdor80/NAP/actions/runs/39837383866) | `05e8992` | 43/43 | CI pasó, pero la inspección visual detectó recorte físico en la comparación. Capturas preliminares rechazadas para entrega. |
| [39838149909](https://github.com/robdor80/NAP/actions/runs/39838149909) | `38b1b43` | 43/43 | La nueva aserción detectó recorte y el render terminó Failed. |
| [39838735578](https://github.com/robdor80/NAP/actions/runs/39838735578) | `1256f98` | 43/43 | Comparación completa; faltaba completar el ciclo de layout antes de comprobar los desplazamientos de zoom. Render Failed. |
| [39839437551](https://github.com/robdor80/NAP/actions/runs/39839437551) | `1e1c1ea` | 43/43 | Correcta, 25 PNG. Se conserva como preliminar; después se afinó el patrón sintético para coincidir con los márgenes declarados. |
| [39839852706](https://github.com/robdor80/NAP/actions/runs/39839852706) | `91f94b9` | 43/43 | Fuente final: 25 PNG, diez stderr vacíos y cero errores de binding. |

Los cinco ZIP de artefactos y TRX preliminares se conservan con prefijo `preliminary-<run>`, sin modificar sus bytes ni atribuir éxitos a fuentes distintas. Son archivos de evidencia de CI, no paquetes de assets ni pruebas ZIP.

Los [hashes de capturas originales](captures.sha256), [SHA de fuente original](source-sha.txt) y TRX mantienen sus finales de línea originales mediante `.gitattributes`. [SHA256SUMS.txt](SHA256SUMS.txt) permite contrastar el conjunto publicado. El digest del ZIP final original es `dd24dcf4ec906aa43ee1c7821ce684cc40d1e3535729e621bb60586d4ab6c1bf`.

**Trabajo detenido tras A/B/C.** No autoriza P6.5–P6.8, producción ni CUTOVER. #47/#49 siguen abiertas y los avisos ImageSharp siguen visibles.
