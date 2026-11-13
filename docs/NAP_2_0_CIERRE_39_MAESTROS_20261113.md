# NAP 2.0 — Cierre técnico local de los 39 retratos históricos de Treskal

**Fecha:** 2026-11-13  
**Estado:** evidencias locales documentadas; integración de consulta SQLite pendiente de prueba física en el MSI.  
**Ámbito:** exclusivamente retratos anteriores a NAP, sin republicación y sin atribuirles un PASS que no existe.

## Fuentes y resultado

- Auditoría local de 39 WebP y su documentación: 39/39 correctos, 0 incidencias, según salida aportada desde el MSI.
- Conciliación de una copia de 39 PNG descargada de TeraBox: 39/39 localizados, huellas SHA-256 registradas.
- 14 PNG: SHA-256 de la copia local igual al indicado en el registro original **PORTRAIT_REGISTRY.json** v1.1.
- 1 PNG (`portrait_treskal_farmer_female_001`): el SHA del registro antiguo `b9d20e27eed3603222d1b69f391c7ae0eb8077e7666039539dd58492afcb11ec` difiere del PNG descargado, pero la comparación local con `git show` del commit `66c2de7dae0625dda21b6073da688e28e1ccbcb8` (26/09/2026) dio **identidad binaria exacta**, SHA `f8710a8ae9ffde7776f19d465b7a5400d26d4aea7f61262f04512f09b2a4d63b`. Ambas huellas quedan conservadas.
- 24 PNG: PNG local válido; nueva huella de referencia **sin comparación histórica independiente**.

La ruta declarada de descarga no es prueba de existencia actual en la nube. **No se verificó el proveedor TeraBox por API, ni una restauración desde la nube.**

## Arquitectura compatible

El catálogo nativo `AssetCatalog.db` v1 sigue siendo exclusivo de activos publicados por la cadena normal NAP. Los 39 WebP históricos viven en `LegacyPortraits.db`. Se añade otro fichero bajo el Workspace local:

`state/LegacyMasterEvidence.db`

Solo almacena **evidencia** de PNG históricos: ID, SHA actual de la copia local verificada, SHA original cuando existe, tipo de contraste, tamaño y dimensiones, ruta relativa a la descarga, y referencia del commit histórico para el caso 001. No contiene los PNG. No afirma verificación remota. No sustituye `PORTRAIT_REGISTRY.json` ni altera el canon.

**Importante:** la evidencia se mantiene en base de datos independiente para que una nueva importación de los WebP no la borre. La importación es explícita, transaccional e idempotente: rechaza hashes distintos en lugar de reemplazarlos silenciosamente.

## Uso en el MSI

1. Mantener en el Workspace definitivo el catálogo nativo inicializado y los 39 retratos históricos importados.
2. En **Catálogo**, pulsar **«Vincular 39 PNG maestros»**.
3. Seleccionar `MAESTROS_39_CONCILIACION_20261113.json` del expediente de conciliación entregado.
4. Seleccionar la carpeta descargada `Videojuego Nimroel - ASSETS MAESTROS PNG`. No seleccionar carpetas internas de la aplicación de TeraBox.
5. NAP relee los 39 PNG, verifica firma PNG, dimensiones, presencia unívoca, límites de tamaño, SHA-256, IDs y las categorías 14/1/24. Solo después escribe `LegacyMasterEvidence.db`.
6. Las fichas del catálogo muestran «PNG local: coincide con SHA histórico», «coincidencia documentada con Git» o «huella nueva, sin SHA histórico», además de «TeraBox remoto: no verificado».

No se mueven ni copian archivos originales; tampoco se registran los históricos como nuevas publicaciones.

## Puertas de aceptación

- Compilación **Windows x64** de la rama de PR #59 con licencia original Six Labors privada.
- Pruebas `LegacyPortraitIndexTests` y `LegacyMasterEvidenceIndexTests`, más regresiones de Catálogo y publicación manual, en el MSI. **No ejecutar GitHub Actions NAP CI**.
- Mostrar 39 históricos y consultar al menos `female_001`, un herrero y un retrato infantil; comprobar el estado de procedencia y que `AssetCatalog.db` no cambie.
- Importar el manifiesto dos veces: segunda vez no debe alterar la evidencia. Un PNG manipulado o manifiesto incompleto debe dar STOP sin escribir en la base.
- Verificar aparte una recuperación real desde TeraBox antes de declarar **archivo remoto operativo**.

## Límites y no objetivos

No se otorga un PASS de Gemini ni una publicación artificial a los retratos históricos. No se cambia `main` hasta prueba física y aceptación. No se migra `PORTRAIT_REGISTRY.json` automáticamente ni se cambia el archivo de PNG maestros. La diferencia del SHA de female_001 no queda borrada ni reinterpretada como corrupción: es una discrepancia del registro frente a la versión de Git comprobada.

**Distinción de cierre:** conciliación local técnica, completa. Integración funcional persistente, propuesta en PR #59 y pendiente de compilación/uso en MSI. Certificación del proveedor remoto, pendiente.
