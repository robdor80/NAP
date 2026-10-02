# Fase 1 · Capítulo 1.5 — Extracción ZIP segura

`StagedPackageExtractor` trabaja después de staging. Recibe la ruta del ZIP
staged, la raíz de extracción y un `CancellationToken`. No llama a Inbox,
detección o readiness, ni cambia esos componentes.

```csharp
var extractor = new StagedPackageExtractor(new StagedPackageExtractionOptions
{
    MaxEntries = 10_000,
    MaxEntryBytes = 512L * 1024 * 1024,
    MaxTotalBytes = 2L * 1024 * 1024 * 1024
});
var result = await extractor.ExtractAsync(stagedZipPath, extractionRoot, token);
```

Los valores del ejemplo son los valores predeterminados: 10.000 entradas,
512 MiB por archivo y 2 GiB totales. Todos deben ser positivos. Se comprueban
los tamaños declarados y los bytes leídos durante la extracción. El número
declarado de entradas se limita antes de cargar los objetos de `ZipArchive`.

El destino es `<extractionRoot>/<nombre del ZIP sin extensión>`. Esta es
una decisión operativa del extractor, no el naming definitivo del asset.
Un ZIP vacío se admite como estructura segura; la validación semántica
corresponde a capítulos posteriores.

El resultado distingue `Extracted`, `Collision`, `InvalidArchive` y
`Rejected`. Solo `Extracted` incluye `FinalPath`. `Reason` explica un rechazo
o fallo del ZIP. La cancelación propaga `OperationCanceledException`;
errores del sistema de archivos o argumentos incorrectos también propagan
su excepción. Nunca se convierten en éxito. Si falla la limpieza, su excepción
se propaga para que el llamador sepa que puede quedar un temporal.

## Política de seguridad

- El ZIP se abre exclusivamente para lectura. El ZIP de Inbox no se toca.
- Se validan todas las rutas antes de crear el temporal. Se normalizan ambos
  separadores y se comprueba la contención con `Path.GetFullPath` y un prefijo
  terminado en separador, tanto para el destino final como para el temporal.
- Se rechazan rutas absolutas, UNC, unidades/ADS, segmentos `.` y `..`,
  segmentos vacíos, controles, nombres reservados Windows y nombres con
  espacios o puntos finales. Incluso un `..` que vuelva dentro del árbol se
  rechaza: no se corrigen rutas dudosas automáticamente.
- Se detectan duplicados, diferencias de mayúsculas/minúsculas y conflictos
  archivo/directorio, incluidos directorios implícitos. La política es la
  misma en plataformas que distinguen mayúsculas/minúsculas.
- Se rechazan enlaces y tipos Unix especiales identificables por atributos,
  reparse points y atributos de directorio incompatibles con el nombre.
  La raíz de extracción y sus ancestros tampoco pueden atravesar enlaces o
  junctions. Las entradas no pueden crear enlaces: solo archivos normales
  y directorios.
- Se verifican la longitud y el CRC-32 de cada archivo. .NET 8 no expone el
  CRC de `ZipArchiveEntry`; un lector interno pequeño obtiene esos campos
  del directorio central, con soporte para registros ZIP64 y comentarios.
  La descompresión sigue usando `ZipArchive`. CRC no es autenticación
  criptográfica. Los ZIP divididos en varios volúmenes se rechazan.
- Se extrae en `.nap-<GUID>.extracting`, usando `FileMode.CreateNew`.
  Al completar y verificar todas las entradas, `Directory.Move` publica el
  árbol en la misma raíz, sin sobrescribir un destino existente. Se comprueba
  la colisión antes de extraer y antes de publicar.
- Un fallo o cancelación elimina únicamente el temporal de esta operación.
  No se eliminan destinos finales, otros temporales ni contenido ajeno.

## Condiciones operativas

La raíz debe ser un área controlada por NAP. Las APIs portables usadas aquí
no garantizan protección contra otro proceso con permisos que sustituya
directorios por enlaces durante la operación. Las comprobaciones rechazan
enlaces existentes; no se ofrece aislamiento frente a modificaciones hostiles
concurrentes del sistema de archivos.

La publicación mediante movimiento evita un directorio final parcial ante
errores y cancelación gestionados. Un cierre abrupto del proceso o corte de
alimentación puede dejar un temporal; no se implementa recuperación global
ni se borran temporales de otras operaciones.

Los límites son configurables y deberán revisarse con paquetes reales de
assets. Este capítulo no implementa manifest, validación PNG, routing,
ProcessingPlan, conversiones, SQLite, integraciones ni UI.

## Validación

`dotnet build` y `dotnet test` desde la raíz de la solución. La suite cubre
extracción simple y anidada, ZIP staged intacto, traversal con ambos
separadores, rutas absolutas/UNC, colisiones externas e internas, corrupción
de compresión y CRC, tamaños declarados falsos, cancelación antes y durante
la escritura, limpieza aislada, límites, enlaces y tipos especiales,
comentarios ZIP y registros ZIP64. La prueba de junctions se ejecuta en Windows.
