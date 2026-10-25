"""Derive Spanish delivery reports from the final immutable Actions evidence."""
import json,sys,shutil,hashlib,collections
from pathlib import Path
D=Path(__file__).resolve().parent
run=sys.argv[1];R=D/'runs'/run
load=lambda n:json.loads((R/n).read_text(encoding='utf-8-sig'))
v=load('verification.json');d=load('download-verification.json');p=load('PROCEDENCIA.json');io=load('file-io.json');runtime=load('runtime-probe.json')
sha=d['sourceCommit'];link=d['userDownloadUrl'];url=f'https://github.com/robdor80/NAP/actions/runs/{run}'
name=d['artifactName'];digest=d['archiveSha256'];expiry=d['expiresAt']
assert d['downloadedZipMatchesGitHubDigest'] and d['allManifestFilesVerified'] and v['focusedPassed']==49 and io['eventsLost']==0
assert not io['forbiddenNapAccess'] and not io['writesOutsideLab'] and not io['childProcesses']
assert sha==p['sourceCommit']==v['sourceCommit']
write=lambda n,s:(D/n).write_text(s.strip()+'\n')
write('delivery.json',json.dumps(dict(runId=run,commit=sha,artifact=name,downloadUrl=link,runUrl=url,sha256=digest,expiresAt=expiry,files=d['files']),indent=2))
write('ZIP-SHA256.txt',f'{digest}  {name}.zip')
write('Informe-ejecutivo.md',f'''# NAP 2.0 — listo para aceptación sintética

25/10/2026. El ZIP descargable está preparado y comprobado. Permite abrir la interfaz real de NAP con RobStyle y datos ficticios, sin activar producción ni utilizar los datos habituales del ordenador. No es el cierre de Fase 6.

[Descargar el programa desde GitHub]({link}). Inicia sesión normal en GitHub, descarga el artefacto **{name}**, descomprime todo y haz doble clic en **Iniciar-prueba-NAP.cmd**. No hacen falta comandos, administrador ni herramientas de programación.

## Resultado verificado

- [Ejecución {run}]({url}), Windows Server 2022, correcta.
- Commit compilado: {sha}.
- Windows x64, autónomo, .NET y WindowsDesktop 8.0.32 incluidos; 491 archivos.
- 49/49 pruebas focalizadas, 9 capturas WPF de 1200 × 700, cero errores de binding.
- Arranque directo y mediante CMD, cinco secciones, filtros, detalles, herramientas sintéticas, bandeja y cierre comprobados.
- {io['capturedFileEvents']} eventos únicos de archivos observados: cero accesos a datos habituales de NAP, cero escrituras fuera de laboratorio, cero procesos hijos y cero eventos perdidos. Canarios sin cambios y unión de directorios rechazada.
- ZIP descargado de nuevo desde GitHub: digest y 490 entradas del manifiesto verificados; el archivo 491 es el propio manifiesto. Procedencia consistente con el commit.
- ImageSharp PNG/WebP y SQLite {runtime['sqliteVersion']} probados en memoria, sin archivo privado de licencia. Ocho paquetes y avisos de redistribución revisados.

## Qué queda pendiente

La aceptación visual humana en Windows 11 y las pruebas operativas reales siguen pendientes. Catálogo está vacío; Producción e Historial muestran filas ficticias; Diagnóstico e Histórico son sintéticos; los cambios de Licencias se pierden al cerrar. Publicación, normalización, borrado, restauración, importación de perfiles, AutomationHost y Git reales están bloqueados.

Siguen abiertos #49 — cinco avisos NU1902/NU1903, timeout IPC y StorageUnavailable — y #47/CUTOVER. No se declara un análisis de seguridad limpio. El ejecutable no tiene firma de editor; si Windows lo bloquea, conserva el mensaje y consúltalo con ChatGPT, sin desactivar protecciones. .NET 8 mantiene soporte hasta el 10/11/2026: hay que revisar el runtime antes de reutilizar este paquete después de esa fecha.

## Entrega y límites

[Informes y guía PDF en la carpeta existente de Drive](https://drive.google.com/drive/folders/1WMXcrTRYbjxk8hP0dm8yK_QGQ-ajZHDu). Permisos conservados; ejecutable solo en GitHub. Retención de GitHub comprobada hasta {expiry}. No se creó Release, instalador, updater ni PR. Main permanece en 5f81950275098881fd0befac0a13cde08c9d751f. No se accedió al MSI, no se tocaron assets reales ni se inició Fase 7.
''')
write('Informe-tecnico.md',f'''# Informe técnico — paquete de aceptación

La autorización del 25/10/2026 sustituye el transporte privado: el paquete de prueba puede descargarse públicamente desde Actions. Los documentos siguen en Drive con sus permisos actuales; no se utilizan credenciales de Google en GitHub.

## Identidad y procedencia

Repositorio robdor80/NAP, rama codex/nap-2-fase-6-ux. Commit compilado {sha}. [Ejecución {run}]({url}), correcta, Windows Server 2022. SDK efectivo {p['sdk']}; destino net8.0-windows, win-x64, Release, autónomo, sin trimming ni archivo único. No se confunde el SDK del runner con el runtime distribuido 8.0.32.

Artefacto {name}, ID {d['artifactId']}; [enlace de descarga verificado]({link}). Caduca {expiry}. El SHA-256 del ZIP que descarga el responsable es **{digest}**. GitHub requiere inicio de sesión normal para descargar artefactos desde el navegador.

491 archivos: ejecutable, ensamblados NAP del mismo checkout, runtime y bibliotecas nativas, recursos WPF/RobStyle, perfiles públicos, avisos, iniciador, instrucciones, PROCEDENCIA.json y SHA256SUMS.txt. No se mezclan salidas de otros commits. No se incluye el ZIP en Git. No se incluyen AutomationHost, Git externo, PDB, obj, archivos personales ni licencias privadas.

## Aislamiento implementado

Phase6AcceptanceOnly=true define NAP_ACCEPTANCE_ONLY. App.OnStartup compila únicamente la entrada de aceptación; el ejecutable directo tampoco tiene disponible el arranque ordinario. Reutiliza Phase6Preview y MainWindow. Solo acepta los parámetros cerrados de aceptación/autoprueba; rechaza configuración productiva, perfiles y destinos arbitrarios. El manifiesto del ejecutable solicita asInvoker.

La sesión compone servicios sintéticos en memoria, sin almacén de perfiles, lector de diagnósticos locales, host, red ni escritores productivos. Los comandos de la interfaz y los flags de mutación de flujo se cierran para esta sesión. Los adaptadores subyacentes rechazan operaciones y confirmaciones. Explorer no carga settings y rechaza guardado de raíces suministradas. Las exportaciones a archivos se detienen antes del diálogo. El informe Aislamiento.md detalla las rutas, los controles y sus límites.

La sesión interactiva no persiste cambios. Solo la autoprueba explícita escribe evidencias nuevas en package/laboratorio, sin rutas de salida elegibles y con rechazo de reparse points, UNC y ubicaciones habituales de NAP. La unión de directorios de la prueba negativa no produjo archivos en su destino.

## Verificaciones ejecutadas

49/49 pruebas focalizadas, cero omitidas/fallos. No se ejecutó regresión general, volumen ZIP de assets, estrés ni benchmarks. El ZIP del programa sí se comprimió, descomprimió y verificó.

La autoprueba del ejecutable descomprimido recorrió Inicio, Catálogo, Producción, Historial y Ajustes, además de Objetivos, Estadísticas, Backups, Git y RoDo. Verificó búsqueda de filas, detalles y comparación sintética, filtros diagnósticos, renovación de licencia en memoria, búsqueda entre 39 referencias históricas, cambio de universo, bandeja/restauración y bloqueo de comandos. Capturó nueve superficies WPF completas de 1200 × 700, con cero errores de binding.

ETW registró {io['capturedFileEvents']} accesos únicos de archivos del proceso; {collections.Counter(e['operation'] for e in io['events'])['write']} escrituras, todas dentro de laboratorio. Cero accesos a LocalAppData/NAP o coordinación compartida; cero procesos hijos, cero eventos perdidos. Se exigió observar la escritura final aceptacion.json como control positivo. Los canarios de configuración/perfiles/licencias/coordinación permanecieron intactos. Este control se ejecutó únicamente en el runner desechable.

Se comprobaron arranque directo, iniciador CMD y cierre normal, cuatro familias de argumentos inseguros y una unión de directorios que intentaba sacar el laboratorio del paquete. Tras retirar la evidencia de la copia descomprimida, todos los hashes del programa seguían intactos.

## Descarga y cadena de integridad

La publicación solo se abre tras las comprobaciones de dependencias, avisos, secretos, runtime y aceptación. upload-artifact crea el ZIP directamente sobre los archivos del programa, sin un ZIP anidado. Una etapa posterior lo descarga desde la API oficial de GitHub usando la identidad temporal del job; comprueba SHA-256 contra artifact-digest, todas las entradas del manifiesto, cantidad de archivos y sourceCommit. download-verification.json conserva el enlace de usuario y la caducidad observados.

verification.json también registra el hash de un ZIP intermedio previo a la subida; ese contenedor tiene los mismos archivos, pero su compresión difiere del ZIP de GitHub. El hash definitivo del archivo entregado figura en ZIP-SHA256.txt y download-verification.json. El manifiesto enumera 490 archivos; se excluye a sí mismo para evitar autorreferencia. El digest del ZIP protege también el manifiesto.

## Dependencias y seguridad

Runtime .NET/WindowsDesktop 8.0.32; SQLite nativo {runtime['sqliteVersion']}; ImageSharp 3.2.0. Se compararon hashes de las bibliotecas nativas/gestionadas con el paquete y se probaron PNG y WebP sin pérdida y una consulta SQLite exclusivamente en memoria. La validación oficial de Six Labors se mantuvo en Release y el temporal se eliminó antes del sondeo. No se publica sixlabors.lic, su contenido ni su hash.

El grafo de ocho paquetes tiene una puerta cerrada que rechaza dependencias nuevas sin revisar. Se conservan MIT, Apache-2.0, copyrights de Microsoft, Six Labors y SourceGear, y avisos completos de runtime/WPF/Windows Forms. El NUSPEC histórico de System.Memory se revisó expresamente. No hacen falta auxiliares externos para la aceptación; Git operativo necesitaría Git externo y permanece bloqueado.

Los cinco avisos ImageSharp permanecen en nuget-vulnerabilities.json y en #49; no se suprimieron. Los backports publicados no se confunden con metadatos limpios. Permanecen sin resolver las reservas operativas de IPC y StorageUnavailable. El runtime autónomo requiere nueva preparación para actualizarlo; .NET 8 termina soporte el 10/11/2026 según Microsoft. El binario no está firmado por un editor de confianza; no se modifica SmartScreen ni Defender.

## Límites de esta entrega

La verificación corresponde al runner Windows Server 2022. El diseño impide rutas productivas de esta edición; no es una garantía frente a manipulación del paquete, del CLR o del sistema por un atacante/administrador. No sustituye el uso real ni la aceptación humana del MSI. No se cambió Core, config, tests ni la CI general respecto de 1728e10. Main permanece intacta; #47 y #49 siguen abiertos; no hay PR, fusión, Release, instalador, updater, Fase 7 ni acceso al MSI.

## Evidencias

runs/{run}/ conserva TRX, trazas ETW, stdout/stderr, JSON de aceptación y descarga, inventario de vulnerabilidades, sondeo de runtime, manifiesto, procedencia, capturas y logs. Las ejecuciones preliminares 40149703893 y 40149785005 detectaron respectivamente un nombre de espacio WPF incorrecto y el orden de inicialización del observador ETW; se corrigieron antes de publicar un paquete aprobado. 40149970601 y 40150158408 conservaron comprobaciones previas correctas. Esta entrega identifica exclusivamente la revisión final {run}; los paquetes anteriores no se mezclan con ella.
''')
write('Guia-Windows11.md',f'''# Abrir NAP de prueba en Windows 11

Esta guía es para la aceptación visual con datos ficticios. No utiliza tus datos habituales. No necesitas que otra persona acceda al MSI.

## 1. Entra en GitHub

Abre el navegador y entra en GitHub con tu cuenta normal. No necesitas un token ni instalar programas. GitHub pide iniciar sesión para descargar este tipo de archivo, aunque el repositorio sea público.

## 2. Descarga el programa

[Pulsa aquí para abrir la descarga]({link}). Si GitHub muestra la ejecución en lugar de descargar directamente, baja a **Artifacts** y pulsa **{name}**. [Esta es la ejecución correcta]({url}). Debe figurar como correcta, con marca verde.

Se descargará **{name}.zip**, de unos 75 MB. Usa este artefacto; el que contiene la palabra evidencias es para los informes técnicos.

## 3. Descomprime todo

En Descargas, pulsa con el botón derecho sobre el ZIP y elige **Extraer todo…**. Elige una carpeta nueva de tu usuario y pulsa **Extraer**. Espera a que termine. Abre la carpeta extraída; no inicies NAP desde la vista del ZIP.

## 4. Identifica el iniciador seguro

Busca **Iniciar-prueba-NAP.cmd** y haz doble clic. Si Windows oculta las extensiones, puede aparecer como **Iniciar-prueba-NAP** y su tipo será archivo de comandos de Windows. No hace falta escribir nada en una consola.

No elijas Ejecutar como administrador. No instales Visual Studio, VS Code ni .NET: el paquete lleva lo necesario. Si Windows muestra un bloqueo o advertencia de seguridad, conserva el mensaje y una captura y consulta con ChatGPT. No desactives Defender ni SmartScreen ni fuerces el bloqueo.

## 5. Comprueba la modalidad

La ventana debe indicar **ACEPTACIÓN AISLADA** en el título y **ACEPTACIÓN · DATOS FICTICIOS · SOLO MEMORIA** en la cabecera. Si no aparece ese aviso, cierra la ventana y comunícalo a ChatGPT. La consola del iniciador puede permanecer abierta mientras usas NAP; es normal.

## 6. Recorre las pantallas

Pulsa **Inicio**, **Catálogo**, **Producción**, **Historial** y **Ajustes**. Catálogo está vacío a propósito. En Producción e Historial hay cuatro filas ficticias: selecciona una, abre sus detalles y prueba el buscador y los filtros. Las acciones productivas estarán desactivadas. La indicación Procesando es una muestra; no se procesa ningún archivo real.

En Ajustes consulta **Diagnóstico**, **Licencias** e **Histórico sintético**. Puedes filtrar registros, probar una renovación ficticia y buscar/abrir referencias históricas. La renovación se pierde al cerrar. Los informes se consultan en pantalla; no se exportan a archivos en esta modalidad.

En Herramientas puedes ver Objetivos, Estadísticas, Backups, Git y RoDo. Algunas muestran información vacía o reservada. No crean backups ni ejecutan Git. Inicio muestra automatización sin configurar: es intencionado, no se conecta ningún host.

## 7. Minimiza y recupera

Pulsa **Minimizar a bandeja**. Busca el icono NAP junto al reloj de Windows; si no se ve, pulsa la flecha de iconos ocultos. Haz doble clic sobre NAP para recuperar la ventana, o pulsa con el botón derecho y elige **Abrir NAP**. También puedes probar el botón normal de minimizar: esta sesión está configurada para ir a bandeja.

## 8. Cierra correctamente

Pulsa la **X** de la ventana o elige **Salir** en el menú del icono de bandeja. La ventana y su icono deben desaparecer. No hay cambios de sesión que guardar.

## 9. Cuenta el resultado a ChatGPT

Indica qué pantallas has visto, si pudiste minimizar/recuperar/cerrar y qué problemas aparecieron. Si hubo un error, añade los pasos, el mensaje exacto y una captura. Usa Ficha-incidencia.txt si te ayuda. No hacen falta contraseñas ni tokens.

## Referencia de esta prueba

Commit {sha}. Ejecución {run}. Descarga disponible hasta {expiry}. No es aceptación de producción ni cierre definitivo de Fase 6. Si vas a repetir la prueba después del 10/11/2026, consulta con ChatGPT para revisar el runtime incluido antes de reutilizarla.
''')
write('Resultados.md',f'''# Resultados de la entrega {run}

- Estado Actions: correcto; [ejecución]({url}). Commit {sha}.
- Compilación Release win-x64 autónoma: correcta. .NET/WindowsDesktop 8.0.32; SDK {p['sdk']}.
- Pruebas focalizadas: 49 aprobadas, cero fallos y cero omitidas.
- Capturas: nueve, 1200 × 700; cero errores de binding.
- ETW: {io['capturedFileEvents']} eventos únicos; cero accesos a NAP habitual, cero escrituras fuera de laboratorio, cero procesos hijos y cero eventos perdidos. Control positivo de escritura observado.
- Canarios intactos; comandos productivos y argumentos inseguros rechazados; unión de directorios del laboratorio rechazada.
- Arranque directo, iniciador CMD, cinco secciones, cinco herramientas, filtros, detalles, licencia en memoria, histórico de 39 referencias, bandeja y cierre: comprobados.
- ImageSharp PNG/WebP sin pérdida y SQLite {runtime['sqliteVersion']} en memoria: correctos, sin licencia privada disponible en ejecución.
- Ocho paquetes revisados; bibliotecas nativas y recursos obligatorios presentes; controles de nombres y patrones de secretos correctos.
- Artefacto publicado y descargado de nuevo: 491 archivos; 490 entradas de manifiesto y el propio manifiesto; todos los hashes coinciden.
- SHA-256 del ZIP final: {digest}.
- Caducidad observada: {expiry}. Requiere cuenta GitHub normal para descargar en navegador.
- No se ejecutaron pruebas de volumen de assets, estrés, benchmarks ni regresiones generales; no hubo acceso al MSI.

Las capturas y trazas son del runner, no del MSI. Las reservas de #49 y el CUTOVER de #47 siguen abiertos. El soporte de .NET 8 termina el 10/11/2026; la retención del ZIP no amplía esa vigencia.
''')
write('Estado-del-programa.md',f'''# Programa descargable preparado

La prohibición anterior de entrega pública quedó sustituida por la autorización del responsable de 25/10/2026. [Descarga el artefacto {name}]({link}), de la [ejecución {run}]({url}). Commit compilado {sha}.

Es un único ZIP directamente extraíble. Incluye 491 archivos y el iniciador Iniciar-prueba-NAP.cmd. Está comprobado con runtime autónomo, sin licencias privadas ni datos personales. El hash del ZIP definitivo es {digest}; todas las entradas del manifiesto se verificaron tras descargarlo otra vez de GitHub.

La sesión solo usa datos ficticios en memoria y no activa producción. La aceptación humana del MSI sigue pendiente. No es una Release ni un instalador. La carpeta Drive conserva los manifiestos históricos del candidato retenido de 24/10, que no corresponden a esta versión. Para la entrega actual usa SHA256SUMS-aceptacion.txt, PROCEDENCIA-aceptacion.json y ZIP-SHA256.txt.

Caduca {expiry}; GitHub pide iniciar sesión normal para descargar. Antes de reutilizarlo después del 10/11/2026, revisar el runtime .NET 8.
''')
write('README.md',f'''# NAP 2.0 — aceptación sintética descargable

25/10/2026. **Paquete preparado, comprobado y documentado.** No se declara cerrada la Fase 6.

[Descargar ZIP]({link}) · [Ejecución Actions correcta]({url}) · [Informes y guía en Drive](https://drive.google.com/drive/folders/1WMXcrTRYbjxk8hP0dm8yK_QGQ-ajZHDu).

- Artefacto exacto: {name}.
- Commit compilado: {sha}.
- SHA-256 del ZIP: {digest}.
- Caducidad: {expiry}; inicio de sesión GitHub normal necesario.
- Windows x64 autónomo, .NET 8.0.32, 491 archivos. Descomprimir todo y abrir Iniciar-prueba-NAP.cmd. Un solo ZIP, sin comandos ni administrador.
- 49/49 pruebas focalizadas; nueve capturas WPF de 1200 × 700; cero errores de binding; ETW sin accesos a NAP habitual ni escrituras fuera de laboratorio, sin procesos hijos ni eventos perdidos.
- Interfaz real y RobStyle, cinco secciones, filtros/detalles, Diagnóstico, Licencias e Histórico sintéticos; bandeja/restauración. Sesión sin persistencia. Catálogo vacío y herramientas productivas bloqueadas.
- Pendientes: aceptación visual humana en Windows 11, aceptación operativa real, #47/CUTOVER y reservas de #49. .NET 8 termina soporte el 10/11/2026: revisar antes de reutilizar después de esa fecha. Sin Release, instalador, updater ni acceso al MSI; main intacta.

Informes: Informe-ejecutivo.pdf, Informe-tecnico.pdf, Aislamiento.pdf, Dependencias-y-licencias.pdf, Guia-Windows11.pdf, Capturas-WPF.pdf y Resultados.md. Evidencia original identificada en runs/{run}/. delivery.json registra la identidad de la entrega. La carpeta antigua phase6-msi-preparation conserva la preparación histórica del 24/10, sustituida por esta autorización y entrega.
''')
