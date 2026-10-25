# Informe de aislamiento — edición de aceptación

## Diseño comprobable

El paquete se compila con Phase6AcceptanceOnly=true. En App.OnStartup, la selección NAP_ACCEPTANCE_ONLY excluye de la compilación la rama de arranque ordinario. No depende del iniciador CMD ni de recordar un argumento: abrir NAP.App.exe directamente también entra en aceptación. Cualquier parámetro de configuración, perfil, host o destino de captura arbitrario se rechaza antes de crear la ventana.

Se reutilizan MainWindow, los controles reales, RobStyle y Phase6Preview.Create. No existe una segunda aplicación de demostración. El ensamblado App tiene un manifiesto asInvoker; no pide elevación. Core no se modifica.

La composición de sesión solo construye dos perfiles ficticios, configuraciones suministradas en memoria, PreviewExplorer, un servicio de presentación que rechaza escritores, ReadOnlyNormalization, Phase6WorkflowPreview, Phase6SettingsPreview y MemoryRegister. No se construyen UniverseProfileStore, ProfessionalUiService, AutomationHostClient, RemoteDesktopService, un auditor HTTP ni un servicio operativo. El lector de diagnósticos locales permanece desactivado. Automation es null en la sesión interactiva; sus filas de producción e historial vienen del cliente de muestra en memoria.

Explorer recibe hostConfigurations, por lo que InitializeAsync no invoca LocalUniverseSettingsStore.Load. Las rutas NAP-SYNTHETIC-NOT-A-WORKSPACE son etiquetas absolutas inertes: solo pasan validación léxica de estructura, no se crean ni se comprueban físicamente. El almacén de settings se instancia como descriptor de una ruta ficticia; nunca se carga. SaveAsync rechaza las configuraciones suministradas antes de llamar al almacén. No hay selector real de carpetas ni importador de perfiles.

El cierre de comandos sintéticos de ShellViewModel añade una restricción exclusiva de la sesión; las sesiones ordinarias conservan sus condiciones anteriores. Rechaza guardar raíces, importar, preparar/normalizar/publicar, objetivos, backups, restauración, retención e inspección/operaciones Git. Los flags CanPropose/CanApprove/CanContinue del flujo sintético están desactivados. Además, el adaptador de confirmación siempre rechaza y los servicios de mutación no tienen escritor. Los manejadores de exportación devuelven un aviso antes de abrir SaveFileDialog o consultar rutas administrativas. Copiar diagnóstico solo transfiere texto sintético al portapapeles.

## Persistencia y laboratorio

La sesión interactiva no persiste datos ni crea laboratorio. Licencias y sus renovaciones se evalúan en memoria y se restablecen al cerrar. No se leen ni se modifican %LOCALAPPDATA%/NAP ni la coordinación compartida de %PROGRAMDATA%.

Solo la autoprueba técnica explícita puede guardar capturas y JSON en la subcarpeta fija laboratorio del programa. No admite rutas de salida como argumento. Comprueba los antecesores de la carpeta y rechaza puntos de reanálisis, enlaces, rutas UNC, ProgramData y LocalAppData/NAP. Cada archivo se abre con CreateNew, sin sobrescritura, y nombres internos que no admiten separadores. Las evidencias de prueba se eliminan de la copia descomprimida antes de volver a contrastarla; no se distribuye un laboratorio preexistente.

El control de rutas protege la ejecución prevista en un directorio local controlado por el usuario. No se presenta como una defensa frente a un administrador o un atacante que altere concurrentemente el sistema de archivos, el CLR o el propio paquete. Windows y sus servicios pueden mantener sus registros de sistema. Esto no equivale a habilitar operaciones de NAP sobre datos personales.

## Verificación focalizada

La autoprueba usa el ejecutable Release autónomo descomprimido. Navega por las cinco secciones, las cinco herramientas, filtros y detalles, Diagnóstico, Licencias, Histórico de 39 referencias, cambio de universo y bandeja/restauración. Ejecuta intentos de comandos bloqueados y el guardado heredado de Explorer; exige rechazo. Se observan los errores de binding y se guardan las capturas WPF.

Un observador ETW exclusivo del runner registra aperturas, lecturas, escrituras, borrados y renombrados del proceso NAP. La puerta exige cero accesos a las ubicaciones habituales de NAP, cero escrituras fuera de laboratorio, cero procesos hijos y cero eventos perdidos. Como control positivo exige observar la escritura final laboratorio/aceptacion.json. No basta una ausencia de cambios o un aviso visual.

Antes de abrir el programa se colocan settings, un perfil y un registro de licencias deliberadamente inválidos y sintéticos en el espacio habitual del usuario del runner, más un canario de coordinación en ProgramData. Se comparan sus hashes antes y después. Esto se realiza únicamente en la máquina desechable de Actions; no se accede al MSI ni se toca ningún archivo real del responsable.

También se prueban el arranque directo, el CMD real y el cierre normal. Los parámetros productivos se rechazan con exit 1. Una unión de directorios dirigida fuera del laboratorio debe provocar parada segura antes de escribir. Finalmente se descarga el artefacto ya publicado desde GitHub, se compara su SHA-256 con el digest de GitHub y se valida cada entrada del manifiesto y el commit de procedencia.

Resultado final: Actions 40150398814 correcto, commit d35a8c60ba2318f4950edfed339692f259aed8ed. ETW registró 206 eventos únicos (189 aperturas, 7 lecturas y 10 escrituras): cero accesos a NAP habitual, cero escrituras fuera de laboratorio, cero procesos hijos y cero eventos perdidos. Pasaron la prueba negativa de unión de directorios, el iniciador real y el cierre normal. Hay 49/49 pruebas focalizadas y nueve capturas completas de 1200 × 700 sin errores de binding. Las trazas y los resultados se conservan en Resultados.md y runs/40150398814/. Esta verificación acredita esta edición sintética sobre Windows Server 2022. La aceptación humana visual en Windows 11 y todas las pruebas operativas reales permanecen pendientes; no se declara cerrada Fase 6.
