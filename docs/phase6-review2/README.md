# NAP 2.0 — Galería de revisión visual 2

**Pendiente de aprobación humana.** Fuente verificada: `22134f4059cadfd7d31e2a222dbd985086b0854b`. [Workflow Windows: builds y 33/33 pruebas](https://github.com/robdor80/NAP/actions/runs/39701832746). [Informe de implementación y límites](../NAP_2_0_FASE_6_REVIEW_2.md). [Manifiesto de procedencia, hashes y resultados](evidence.json). [TRX final original](phase6-review2.trx).

29 capturas nativas WPF de las vistas de producto, con datos e imágenes ilustrativos en memoria. No son capturas del MSI ni el resultado del asset funcional de prueba. Los adaptadores del renderizado rechazan todas las escrituras; no conectan al host ni leen settings/activos. El layout fuera de pantalla fija el área cliente lógica y conserva el ancestro Window y sus bindings. Cada proceso falla ante un error de binding o una precondición visual incumplida.

## Pantallas

| Estado | 1440 × 900 | 1200 × 700 |
| --- | --- | --- |
| Inicio y ajustes | [Abrir](inicio-review-1440x900.png) | [Abrir](inicio-review-1200x700.png) |
| Producción | [Abrir](flujo-production-1440x900.png) | [Abrir](flujo-production-1200x700.png) |
| Historial | [Abrir](flujo-history-1440x900.png) | [Abrir](flujo-history-1200x700.png) |
| Historial: actividad | [Abrir](flujo-history-detalle-1440x900.png) | [Abrir](flujo-history-detalle-1200x700.png) |
| Corrección: selección compatible | [Abrir](flujo-choice-1440x900.png) | [Abrir](flujo-choice-1200x700.png) |
| Corrección: selección, zona inferior | [Abrir](flujo-choice-detalle-1440x900.png) | [Abrir](flujo-choice-detalle-1200x700.png) |
| Corrección: propuesta | [Abrir](flujo-exception-1440x900.png) | [Abrir](flujo-exception-1200x700.png) |
| Corrección: comparación y aprobación | [Abrir](flujo-exception-detalle-1440x900.png) | [Abrir](flujo-exception-detalle-1200x700.png) |
| Corrección: decisión guardada | [Abrir](flujo-approved-1440x900.png) | [Abrir](flujo-approved-1200x700.png) |
| Corrección: continuación individual | [Abrir](flujo-approved-detalle-1440x900.png) | [Abrir](flujo-approved-detalle-1200x700.png) |
| Auditoría bloqueada | [Abrir](flujo-blocked-1440x900.png) | [Abrir](flujo-blocked-1200x700.png) |
| Sin registros | [Abrir](flujo-empty-1440x900.png) | [Abrir](flujo-empty-1200x700.png) |
| Error de lectura | [Abrir](flujo-read-error-1440x900.png) | [Abrir](flujo-read-error-1200x700.png) |
| Operaciones manuales | [Abrir](flujo-manual-1440x900.png) | [Abrir](flujo-manual-1200x700.png) |

[Herramientas compactas desplazadas hasta RoDo](inicio-review-herramientas-1200x700.png). Esta comprobación exige altura acotada y desplazamiento efectivo; conserva las cinco secciones principales fijas.

En compacto, lista y detalle tienen desplazamientos independientes. Las imágenes «zona inferior» muestran los controles tras desplazarse; no se han recortado, recompuesto ni retocado. La selección compatible inicial requiere elegir en el desplegable; por eso Preparar vista previa aparece deshabilitado hasta elegir. Los estados de propuesta y aprobación muestran después las acciones reales correspondientes. Una auditoría FAIL no ofrece aprobación ni continuación.

## Flujo de revisión

1. Abrir una incidencia desde Inicio o seleccionar un asset en Producción.
2. Si existe una corrección permitida, elegir perfil/tamaño de las opciones calculadas por Core.
3. Preparar la vista previa y comparar original, propuesta, dimensiones y bordes.
4. Aprobar explícitamente esa corrección para ese asset; la decisión queda guardada.
5. Continuar el asset. El host vuelve a verificar autorización, política y evidencia; no se concede PASS ni publicación.

Los casos sin solución autorizada permanecen bloqueados. Cambiar una corrección invalida la aprobación anterior; no instala una regla global. El cierre del detalle y Esc permiten volver a la lista sin afectar a la automatización.

## Reproducción sintética en Windows

Sobre el commit de fuente indicado, compilar Release mediante la validación oficial existente, con licencia autorizada y privada. No deshabilitar ese target ni copiar la licencia a las evidencias.

```powershell
$appPath = '.\src\NAP.App\bin\Release\net8.0-windows\NAP.App.exe'
$out = Join-Path $PWD 'artifacts\phase6-review2'
foreach ($mode in @('review','production','history','choice','exception','approved','blocked','empty','read-error','manual')) {
  foreach ($compact in @($false,$true)) {
    $captureArguments = @("--phase6-preview=$mode", "--phase6-output=$out")
    if ($compact) { $captureArguments += '--compact' }
    $process = Start-Process $appPath -ArgumentList $captureArguments -PassThru
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Preview timed out' }
    if ($process.ExitCode -ne 0) { throw "Preview failed: $mode" }
  }
}
```

No añadir `--automation-config`, no cargar universos reales y no ejecutar pruebas ZIP de volumen. Solo se escribe el directorio de capturas solicitado. El workflow exacto y el filtro pequeño están en [phase6-review.yml](../../.github/workflows/phase6-review.yml).

El ZIP final descargado coincide con el digest declarado por GitHub: `e0cb1da8c995c7c4c5a637b4e7a56c0b3f4183fb554ba56777700dc794d554ce`. Los 29 PNG coinciden con [captures.sha256](captures.sha256); el SHA de [source-sha.txt](source-sha.txt) coincide con la fuente verificada. Los TRX y metadatos de origen conservan sus bytes mediante `.gitattributes`.

## Cronología conservada

| Ejecución | Fuente | Pruebas | Resultado |
| --- | --- | --- | --- |
| [39699898292](https://github.com/robdor80/NAP/actions/runs/39699898292) | `b7c87da` | 30/31 | Falló el hash de lectura por muestrear antes del arranque del propietario; [TRX original](preliminary-39699898292.trx). |
| [39700491027](https://github.com/robdor80/NAP/actions/runs/39700491027) | `04540dc` | 33/33 | Correcta, 18 capturas preliminares; [TRX original](preliminary-39700491027.trx). |
| [39701372593](https://github.com/robdor80/NAP/actions/runs/39701372593) | `57ce62b` | 32/33 | El host rechazó el original alterado; la prueba esperaba otra clase de excepción; [TRX original](preliminary-39701372593.trx). |
| [39701612672](https://github.com/robdor80/NAP/actions/runs/39701612672) | `2b53623` | 33/33 | Correcta, 28 capturas preliminares; [TRX original](preliminary-39701612672.trx). |
| [39701832746](https://github.com/robdor80/NAP/actions/runs/39701832746) | `22134f4` | 33/33 | Fuente final, 29 capturas y cero errores de binding; [TRX original](phase6-review2.trx). |

No se alteran fallos ni evidencias anteriores. Los resultados pertenecen a su SHA específico; los commits documentales posteriores no reciben por atribución los checks de otro commit. Los PNG y el TRX finales quedan en este repositorio, independientes de la retención del artefacto de Actions.

**Detenido en revisión visual 2.** No autoriza P6.5, despliegue, producción ni CUTOVER. La aceptación funcional y visual en el MSI continúa pendiente.
