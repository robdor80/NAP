# Informe técnico — paquete de aceptación

La autorización del 25/10/2026 sustituye el transporte privado: el paquete de prueba puede descargarse públicamente desde Actions. Los documentos siguen en Drive con sus permisos actuales; no se utilizan credenciales de Google en GitHub.

## Identidad y procedencia

Repositorio robdor80/NAP, rama codex/nap-2-fase-6-ux. Commit compilado d35a8c60ba2318f4950edfed339692f259aed8ed. [Ejecución 40150398814](https://github.com/robdor80/NAP/actions/runs/40150398814), correcta, Windows Server 2022. SDK efectivo 10.0.402; destino net8.0-windows, win-x64, Release, autónomo, sin trimming ni archivo único. No se confunde el SDK del runner con el runtime distribuido 8.0.32.

Artefacto NAP-2.0-Fase6-Aceptacion-Windows-x64, ID 12582294018; [enlace de descarga verificado](https://github.com/robdor80/NAP/actions/runs/40150398814/artifacts/12582294018). Caduca 2027-01-23T01:22:51Z. El SHA-256 del ZIP que descarga el responsable es **f15626624e0a88b3d7d796f83690fbffca1f21ca8f86687547f232d86e64ea7e**. GitHub requiere inicio de sesión normal para descargar artefactos desde el navegador.

491 archivos: ejecutable, ensamblados NAP del mismo checkout, runtime y bibliotecas nativas, recursos WPF/RobStyle, perfiles públicos, avisos, iniciador, instrucciones, PROCEDENCIA.json y SHA256SUMS.txt. No se mezclan salidas de otros commits. No se incluye el ZIP en Git. No se incluyen AutomationHost, Git externo, PDB, obj, archivos personales ni licencias privadas.

## Aislamiento implementado

Phase6AcceptanceOnly=true define NAP_ACCEPTANCE_ONLY. App.OnStartup compila únicamente la entrada de aceptación; el ejecutable directo tampoco tiene disponible el arranque ordinario. Reutiliza Phase6Preview y MainWindow. Solo acepta los parámetros cerrados de aceptación/autoprueba; rechaza configuración productiva, perfiles y destinos arbitrarios. El manifiesto del ejecutable solicita asInvoker.

La sesión compone servicios sintéticos en memoria, sin almacén de perfiles, lector de diagnósticos locales, host, red ni escritores productivos. Los comandos de la interfaz y los flags de mutación de flujo se cierran para esta sesión. Los adaptadores subyacentes rechazan operaciones y confirmaciones. Explorer no carga settings y rechaza guardado de raíces suministradas. Las exportaciones a archivos se detienen antes del diálogo. El informe Aislamiento.md detalla las rutas, los controles y sus límites.

La sesión interactiva no persiste cambios. Solo la autoprueba explícita escribe evidencias nuevas en package/laboratorio, sin rutas de salida elegibles y con rechazo de reparse points, UNC y ubicaciones habituales de NAP. La unión de directorios de la prueba negativa no produjo archivos en su destino.

## Verificaciones ejecutadas

49/49 pruebas focalizadas, cero omitidas/fallos. No se ejecutó regresión general, volumen ZIP de assets, estrés ni benchmarks. El ZIP del programa sí se comprimió, descomprimió y verificó.

La autoprueba del ejecutable descomprimido recorrió Inicio, Catálogo, Producción, Historial y Ajustes, además de Objetivos, Estadísticas, Backups, Git y RoDo. Verificó búsqueda de filas, detalles y comparación sintética, filtros diagnósticos, renovación de licencia en memoria, búsqueda entre 39 referencias históricas, cambio de universo, bandeja/restauración y bloqueo de comandos. Capturó nueve superficies WPF completas de 1200 × 700, con cero errores de binding.

ETW registró 206 accesos únicos de archivos del proceso; 10 escrituras, todas dentro de laboratorio. Cero accesos a LocalAppData/NAP o coordinación compartida; cero procesos hijos, cero eventos perdidos. Se exigió observar la escritura final aceptacion.json como control positivo. Los canarios de configuración/perfiles/licencias/coordinación permanecieron intactos. Este control se ejecutó únicamente en el runner desechable.

Se comprobaron arranque directo, iniciador CMD y cierre normal, cuatro familias de argumentos inseguros y una unión de directorios que intentaba sacar el laboratorio del paquete. Tras retirar la evidencia de la copia descomprimida, todos los hashes del programa seguían intactos.

## Descarga y cadena de integridad

La publicación solo se abre tras las comprobaciones de dependencias, avisos, secretos, runtime y aceptación. upload-artifact crea el ZIP directamente sobre los archivos del programa, sin un ZIP anidado. Una etapa posterior lo descarga desde la API oficial de GitHub usando la identidad temporal del job; comprueba SHA-256 contra artifact-digest, todas las entradas del manifiesto, cantidad de archivos y sourceCommit. download-verification.json conserva el enlace de usuario y la caducidad observados.

verification.json también registra el hash de un ZIP intermedio previo a la subida; ese contenedor tiene los mismos archivos, pero su compresión difiere del ZIP de GitHub. El hash definitivo del archivo entregado figura en ZIP-SHA256.txt y download-verification.json. El manifiesto enumera 490 archivos; se excluye a sí mismo para evitar autorreferencia. El digest del ZIP protege también el manifiesto.

## Dependencias y seguridad

Runtime .NET/WindowsDesktop 8.0.32; SQLite nativo 3.53.3; ImageSharp 3.2.0. Se compararon hashes de las bibliotecas nativas/gestionadas con el paquete y se probaron PNG y WebP sin pérdida y una consulta SQLite exclusivamente en memoria. La validación oficial de Six Labors se mantuvo en Release y el temporal se eliminó antes del sondeo. No se publica sixlabors.lic, su contenido ni su hash.

El grafo de ocho paquetes tiene una puerta cerrada que rechaza dependencias nuevas sin revisar. Se conservan MIT, Apache-2.0, copyrights de Microsoft, Six Labors y SourceGear, y avisos completos de runtime/WPF/Windows Forms. El NUSPEC histórico de System.Memory se revisó expresamente. No hacen falta auxiliares externos para la aceptación; Git operativo necesitaría Git externo y permanece bloqueado.

Los cinco avisos ImageSharp permanecen en nuget-vulnerabilities.json y en #49; no se suprimieron. Los backports publicados no se confunden con metadatos limpios. Permanecen sin resolver las reservas operativas de IPC y StorageUnavailable. El runtime autónomo requiere nueva preparación para actualizarlo; .NET 8 termina soporte el 10/11/2026 según Microsoft. El binario no está firmado por un editor de confianza; no se modifica SmartScreen ni Defender.

## Límites de esta entrega

La verificación corresponde al runner Windows Server 2022. El diseño impide rutas productivas de esta edición; no es una garantía frente a manipulación del paquete, del CLR o del sistema por un atacante/administrador. No sustituye el uso real ni la aceptación humana del MSI. No se cambió Core, config, tests ni la CI general respecto de 1728e10. Main permanece intacta; #47 y #49 siguen abiertos; no hay PR, fusión, Release, instalador, updater, Fase 7 ni acceso al MSI.

## Evidencias

runs/40150398814/ conserva TRX, trazas ETW, stdout/stderr, JSON de aceptación y descarga, inventario de vulnerabilidades, sondeo de runtime, manifiesto, procedencia, capturas y logs. Las ejecuciones preliminares 40149703893 y 40149785005 detectaron respectivamente un nombre de espacio WPF incorrecto y el orden de inicialización del observador ETW; se corrigieron antes de publicar un paquete aprobado. 40149970601 y 40150158408 conservaron comprobaciones previas correctas. Esta entrega identifica exclusivamente la revisión final 40150398814; los paquetes anteriores no se mezclan con ella.
