> Actualización 25/10/2026: el responsable autorizó el paquete público de aceptación sintética. Esta preparación de 24/10 queda como histórico; consulte [la entrega actual comprobada](../phase6-public-acceptance/README.md).

# NAP 2.0 — Preparación privada de aceptación MSI

24/10/2026 · Fase 6, sin aceptación humana ni Fase 7.

**Estado: programa no entregable todavía. No ejecutar NAP en el MSI.** Se verificó una publicación autónoma Windows x64 dentro de Actions y se destruyó sin subir binarios. La carpeta documental de Drive es privada; falta transporte binario runner→Drive, aislamiento interactivo y cierre de avisos/dependencias.

## Resultado comprobado

- Código compilado: `b2e9413f0d3b9d3eb4df79f07fce74685d861d7c`.
- [Actions 40123851505](https://github.com/robdor80/NAP/actions/runs/40123851505): correcto.
- .NETCore y WindowsDesktop 8.0.32 incluidos; SDK efectivo 10.0.402 registrado sin confundirlo con el objetivo net8.
- 49/49 pruebas focalizadas; 14 escenarios y 15 capturas únicas WPF; 0 errores de binding.
- PNG/WebP ImageSharp y SQLite en memoria correctos después de retirar licencia temporal; bibliotecas comparadas por hash con publicación.
- 478 archivos / 174.800.044 bytes en candidato; manifiesto y SHA-256 por archivo conservados en runs/40123851505. No hay ejecutables en esta entrega ni artefactos de Actions.
- No se modificó src/, tests/ ni config/ respecto al código previamente verificado `ed556bbbdf8a6971cd22d38f96ff88885a366225`.
- No se garantiza contención del arranque ordinario: recordatorios y perfiles en LocalAppData y coordinación en ProgramData. No se ha cambiado esa autoridad.

Los commits de evidencia y cierre documental son posteriores al SHA compilado; no se presentan como builds nuevos. La entrega anterior `66fc2ff7b007aa760eef95931491482daf15d3e2` permanece en la historia.

## Drive privado

[Carpeta completa](https://drive.google.com/drive/folders/1WMXcrTRYbjxk8hP0dm8yK_QGQ-ajZHDu)

- [A. Informe](https://drive.google.com/drive/folders/12FqoVimPSItCY8_VBhwFSbYsycJG0jK1): [ejecutivo PDF](https://drive.google.com/file/d/1u5coRtx2VocmiGMcG7OTzcXLLaonQUs1/view?usp=drivesdk), informe técnico y auditorías.
- [B. Programa](https://drive.google.com/drive/folders/1zUf9Qh8rqJZGDykI4tED_zyITUMsdGqv): estado de bloqueo, manifiesto y hashes documentales, sin ejecutables.
- [C. Pruebas](https://drive.google.com/drive/folders/1fUiFMIVaoARdaW0cqpn2ZnnOBW240qoU): TRX, logs completos con avisos, PNG/sidecars, [galería PDF](https://drive.google.com/file/d/1YapVtujOlUmCRh3LEEsHLKenGxBnGY_0/view?usp=drivesdk), verificaciones y procedencia.
- [D. Guía de usuario](https://drive.google.com/drive/folders/1RgxzasCpUu_sJLAhmdPuUl90bnJSlPXy): [guía PDF](https://drive.google.com/file/d/1mwx9Z-8XBA-L4nnAbfoDuL4KuZCx0z5V/view?usp=drivesdk), lista de comprobación y ficha de incidencias.

72 archivos principales leídos por metadata, con tamaño/padre y propietario único comprobados; siete documentos completos descargados de nuevo con SHA-256 coincidente. Ver [Verificacion-Drive.md](Verificacion-Drive.md), [Drive-files.json](Drive-files.json) y [Drive-readback.json](Drive-readback.json). Se añade aparte el cierre y su índice, verificado por metadata. No se han cambiado permisos. Los enlaces requieren autenticación y son consultables por ChatGPT mediante la conexión de Drive mientras conserve acceso.

## Siguiente decisión antes de ejecutar

Ver [Canal-privado.md](Canal-privado.md) y [Mapa-almacenamiento.md](Mapa-almacenamiento.md). La propuesta mínima de producto es una edición de aceptación exclusivamente sintética y de solo lectura, sin arranque ordinario ni escritores alternativos. Cambia el alcance de la sesión y se deja pendiente de decisión de producto conforme al apartado 5 del encargo. La identidad de transferencia también debe ser autorizada/provista; no se extrae la del conector ni se cambian permisos para suplirla.

El cierre de avisos legales y auxiliares debe preceder a regenerar un paquete entregable. #49 sigue abierto con los cinco avisos NU1902/NU1903; #47 y CUTOVER permanecen sin activar. No se ejecutó NAP en el MSI, host, servicios, instalación, ZIP de assets, volumen, estrés o producción. No se creó PR ni Release ni se fusionó.
