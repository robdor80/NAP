# Auditoría de persistencia y aislamiento — preparación MSI

Fecha: 24/10/2026. Lectura de src/NAP.App, NAP.Presentation, NAP.Core, NAP.AI, NAP.AutomationHost y NAP.Cli. El inventario adjunto conserva las referencias por archivo y línea. No se han consultado carpetas del MSI ni raíces productivas. Las rutas siguientes proceden del código, no del ordenador del responsable.

| Ubicación o ámbito | Lectura/escritura y componente | Consecuencia para la aceptación |
|---|---|---|
| `%LOCALAPPDATA%\NAP\settings.json` | LocalUniverseSettingsStore, raíces de universos; guardado atómico con temporal y mutex | `--settings` solo cambia este archivo; no sirve como sandbox |
| `%LOCALAPPDATA%\NAP\universes` | UniverseProfileStore descubre/importa perfiles; temporales de publicación | El arranque normal sigue descubriendo perfiles locales aunque se cambie settings |
| `%LOCALAPPDATA%\NAP\license-reminders.json` | LicenseRegister.Check escribe ya durante Shell.InitializeAsync; recordatorios, atención y renovación | No hace falta pulsar Guardar para tocar datos habituales |
| `%PROGRAMDATA%\NAP-coordination-v2-<SID>` | ExecutionFileLease.PrivateDirectory; archivos de exclusión vacíos; crea directorio con ACL explícita del usuario y SYSTEM | Cambiar TEMP/LOCALAPPDATA no mueve esta autoridad. No cambiarla ni crear otra para la aceptación |
| WorkspaceRoot | inbox, staging, state, cache; catálogos AssetCatalog.db y auxiliares SQLite, journals JSON, objetivos, receipts `state/git-production`, normalización con snapshot/candidate/control/failures | No cargar una configuración ordinaria, ni aceptar raíces reales. Cambiar settings no encierra operaciones manuales |
| WorkspaceRoot/state/automation | AutomationQueue.db; preparation/PreparationLedger.db, execution/ExecutionLedger.db, runtime/RuntimeState.db, temporales y auxiliares SQLite | Solo aplicables cuando se habilitan explícitamente los adaptadores/host. No se inician en esta tarea |
| ProductionRoot | Publicación de derivados, catálogo/proyecciones y operaciones Git; `.git`, hooks/attributes/control Git según operación | Mantener completamente fuera de la prueba; no invocar herramientas manuales |
| ArchiveRoot | `_nap/master_index.json`, namespaces NAP_DATABASE_BACKUPS, NAP_REPOSITORY_SNAPSHOTS, NAP_GIT_BUNDLES y publicación temporal | No probar backup/restauración/retención/Git |
| Directorio de control del host y solicitudes desktop | HostControl.db y almacenamiento de operaciones/recibos explícitos de AutomationDesktopBridge; configuración de lanzamiento con raíces y conexión | No cargar `--automation-config`, no iniciar servicio o proceso host |
| `%TEMP%\nap-host-client-<GUID>` | AutomationThumbnailCache descarga vistas temporales del host | Ausente en vistas sin host; TEMP por sí solo no aísla el resto |
| Directorios explícitos de CLI y herramientas | Extracciones, salidas y logs elegidos por sus argumentos | No se distribuyen/ejecutan CLI ni escritores auxiliares en esta aceptación |
| Destino de exportación elegido en diálogo | Diagnóstico/Histórico usan DiagnosticReportWriter: archivo .txt nuevo, límites y exclusión de raíces conocidas | El diálogo permite escribir fuera de un futuro laboratorio; debe restringirse en un modo de aceptación |
| Portapapeles | Copia explícita del informe y vistas sintéticas de exportación | Efecto de sesión de Windows; no acredita contención de archivos |
| Directorio base del programa | Perfiles públicos config/universes y recursos WPF/RobStyle embebidos | Deben formar parte del árbol completo de publicación. No necesita rutas del checkout para el preview |
| Entorno | NAP_GEMINI_API_KEY/NAP_GEMINI_MODEL se leen solo en arranque ordinario; configuración IPC explícita; Git externo usa `git` | No copiar entorno personal. Las vistas cerradas retornan antes de estos lectores |
| Sistema y runtime Windows | Fuentes, WPF, bandeja, cachés del SO y .NET | La comparación de rutas NAP no es trazado completo de todas las escrituras del sistema |

No se encontró una vía ordinaria de NAP.App que instale servicios, cambie cuentas o escriba el Registro. Esto no convierte su arranque en inocuo: LicenseRegister usa coordinación de archivos con ACL. No se han ejecutado esos caminos para esta preparación.

## Resultado y decisión requerida

**No está garantizado el aislamiento del arranque ordinario en el MSI. No ejecutar el candidato.** Tampoco basta lanzar bajo otro nombre, descomprimir en otra carpeta o cambiar variables de entorno: GetFolderPath y la coordinación común son independientes.

Las vistas `--phase6-preview` existentes tienen un límite distinto: crean el Shell con adaptadores de lectura sintéticos, perfiles y raíces ficticias, licencias en memoria, sin registro local, proveedor IA ni host. Capturan y terminan automáticamente; no constituyen una sesión interactiva para el responsable. Usarlas en la nube comprueba WPF, pero no sustituye el aislamiento del producto.

La alternativa de menor alcance de producto es una **edición de aceptación exclusivamente sintética**: entrada que no pueda caer en el arranque ordinario, reutilización de los adaptadores existentes y reglas Core de licencias, bloqueo de importar/configurar raíces y de operaciones manuales, exportaciones limitadas a una carpeta nueva validada contra enlaces/reparse points, informe/cierre/reapertura y pruebas negativas de todos los argumentos. Debe permitir navegar y bandeja, sin escritor ni segunda autoridad. Persistir recordatorios de la prueba mediante LicenseRegister invocaría la coordinación común y requeriría revisar esa decisión; usar su evaluación en memoria evita tocarla, con la limitación explícita de no probar persistencia administrativa entre sesiones.

Esto cambia el alcance de la prueba humana (de aplicación ordinaria a edición sintética de solo lectura). Se documenta para decisión de producto antes de implementarlo; no se ha cambiado la aplicación ni el protocolo de coordinación.

Alternativas descartadas dentro del alcance: cuentas separadas, Windows Sandbox/VM instalada o habilitada en MSI, ACL nuevas, redirección de ProgramData, instalar servicios o parchear ExecutionFileLease. Requieren cambios de sistema o decisiones expresamente excluidas. No se aconseja ejecutar el programa actual y confiar en no pulsar botones.
