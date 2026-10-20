# NAP 2.0 — aceptación técnica de Fase 5

Fecha de validación: 2026-10-20, Europe/Madrid. **P5.10 en validación; todavía no aceptada.** Este informe distingue las pruebas actuales de las históricas y no autoriza producción, instalación ni CUTOVER.

## Versión y límites de la validación

Se recuperó la rama existente `codex/nap-2-fase-5-automation-host` en `08894c03ac6738abab1eec705c9326fae52ec141`, limpia y sincronizada. Los cambios posteriores de la reanudación hasta iniciar la regresión son documentales; los binarios Release se compilaron del código recuperado. Entorno: Windows x64, SDK 10.0.400, .NET y WindowsDesktop 8.0.8. No se extrapolan los resultados anteriores de Linux a esta ejecución.

Se conserva por decisión expresa el contrato de validación global mediante SHA-256 fresco de todos los bytes. Las pruebas reutilizables de validación pura siguen vinculadas a contenido, ruta y raíces exactas; no sustituyen ownership, política vigente, receipts, CAS ni comprobaciones nativas. No hay validación incremental autenticada nueva ni elevación de límites de las pruebas.

`main` continúa en `cd7532f011eea8a72e89848234b259f1f5293e15`; GitHub informa `protected=false` y reglas efectivas vacías. La configuración no se ha cambiado. Es una condición pendiente de gobernanza antes de cualquier fusión.

## Evidencia actual

| Área | Ejecución actual | Resultado |
|---|---|---|
| Compilación completa Release | `resume-20261020-build.log` | Correcta, cero errores/avisos sin restaurar; no resuelve los advisories históricos |
| Recuperación, almacenamiento, cleanup, ledger y hashing | `resume-head-directed.trx` | 56/56, cero fallos/omisiones |
| Manual, aceptación específica y WPF real | `resume-windows-manual-wpf.trx` | 16/16, cero fallos/omisiones |
| Contratos JSON Schema | `resume-20261020-schema.log` | 436 comprobaciones correctas |
| Host 1/10/100 dentro de la regresión | Métricas `161933`, `162154`, `163509` | Casos terminados, 1/10/100 completados; confirmación consolidada TRX pendiente |
| Regresión Windows completa con ambos grupos de volumen | `resume-complete-windows.trx` | En ejecución; no certificada |

Los archivos anteriores están bajo `artifacts/phase5` y sus manifests se mantienen en `NAP_2_0_FASE_5_TEST_EVIDENCE.json` y `NAP_2_0_FASE_5_RESOURCE_EVIDENCE.json`. Las selecciones se solapan; sus contadores no se suman como pruebas distintas. Los informes históricos y archivos de evidencia permanecen conservados.

Las 22 pruebas de recuperación contenidas en la selección dirigida comprueban el mismo Job/Attempt tras cortes de proceso, conservación de originales/receipts, autorización persistida o de sesión, pausa/cancelación/revocación, resultado remoto sin respuesta vinculada y Completed inconsistente sin reparación automática. No certifican pérdida eléctrica ni exactly-once de un proveedor remoto.

La selección manual ejecuta preparación/auditoría/publicación en raíces sintéticas, catálogo/documentos/estadísticas/objetivos, normalización, thumbnails, backup/restore/retención y Git explícito contra bare local desechable. Incluye presupuesto de buffers ocultos del bridge y limpieza Windows por identidad con ancestros fijados. WPF se abrió y renderizó; la captura fue inspeccionada y el caso remoto verificó cero errores de binding y cierre del cliente con host aún activo.

Medición actual del host 100: 793,53 s, pico privado 75.280.384 bytes, 374 handles, máximo intervalo de progreso 31,76 s, último muestreo CPU 622,28 s; contadores Windows 197.882.443.413 bytes lógicos leídos. Es carga sintética durante regresión, no un benchmark productivo ni una medida de bytes físicos de disco. El host 1000 sigue en ejecución. Se conservan las consultas diagnósticas adicionales por IPC; añaden carga y no sustituyen las muestras de la propia prueba.

Los presupuestos de datos retenidos del bridge (128 MiB para handles, incluyendo buffers ocultos; 128 MiB independientes para blobs) no constituyen un límite absoluto de memoria residente. El ensayo de volumen comprueba por separado el proceso del host con el envelope original de 1 GiB privado, menos de 2048 handles y menos de 128 hilos. No se han elevado esos límites.

## Incidencias que requieren evidencia final

- `StorageUnavailable` histórico de Windows: los lotes Linux de 100 no establecieron su causa. El laboratorio conserva ahora excepción original y sidecars al fallar; no se declara corregido por inferencia.
- `ConnectionReset` histórico en cleanup y QueuePage tras reinicio: el fuente final acotado del fixture ya compila y pasa la selección actual. Solo reintenta consultas, con nueva autenticación dentro del mismo deadline, y exige salida real exitosa durante cleanup. No se presume una causa de infraestructura ni se reintentan efectos inciertos.
- Symlinks: la regresión local vuelve a registrar falta de privilegio de creación. Los cinco éxitos históricos Linux no acreditan Windows. No se cambian cuentas/permisos ni se omiten artificialmente estos casos.
- Procesamiento integral de 1000, fairness prolongada, crecimiento de ledgers y presupuestos de recursos del último código: pendientes del resultado completo. No se usa admisión independiente como sustituto del pipeline integral.

## Requisitos reservados para CUTOVER

Permanecen separados conforme a `NAP_2_0_FASE_4_SOFTWARE_ACCEPTANCE.md` y `NAP_2_0_FASE_4_CUTOVER.md`: distribución y dependencias protegidas, identidad definitiva de escritor, arranque administrado, retirada de escritores/handles anteriores, permisos/ownership de raíces y padres, pruebas de denegación entre identidades, backup e inventarios reales y autorización productiva expresa. El canal de laboratorio del mismo principal no certifica esa barrera operativa.

No se han instalado servicios, creado cuentas, modificado permisos Windows ni intervenido activos reales de Nimroel. No se ha ejecutado el workflow manual de CUTOVER. La futura PR permanecerá Draft para auditoría; no se realizará merge ni activación productiva.
