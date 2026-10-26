# Informe técnico — candidato retenido de Fase 6

## Procedencia y reproducción acotada

Rama autorizada: codex/nap-2-fase-6-ux. Entrega de entrada: 66fc2ff7b007aa760eef95931491482daf15d3e2. Referencia previa: ed556bbbdf8a6971cd22d38f96ff88885a366225.

Actions 40123851505 compiló exactamente **b2e9413f0d3b9d3eb4df79f07fce74685d861d7c**, con checkout por SHA. La diferencia de src/, tests/ y config/ respecto a ed556bbb es vacía. No se modificó código de producto tras esa compilación. c163eca es el commit posterior que conserva evidencia, no el código compilado.

Se revisaron antes de escribir los disparadores existentes: ci.yml ejecuta sobre main/PR a main; phase6-review.yml sobre rutas de código/pruebas concretas en la rama y dispatch; CUTOVER requiere dispatch con booleano explícito. El nuevo workflow solo se activa sobre sus tres rutas de infraestructura/sonda en la rama autorizada. No se abrieron PR ni se invocó CUTOVER. Los commits de informes no disparan compilaciones generales.

Publicación: `dotnet publish src/NAP.App/NAP.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true`, a RUNNER_TEMP, mediante Invoke-LicensedDotnet.ps1 sin cambios. La licencia temporal se limpia después de cada invocación y explícitamente antes de ejecutar sonda, pruebas y WPF.

**SDK efectivo: 10.0.402**, registrado por dotnet --version. Setup-dotnet instaló/confirmó 8.0.426, pero sin global.json el runner seleccionó el SDK superior instalado. No se presenta como build realizado con SDK 8. El objetivo y ambos runtimes distribuidos sí son .NET 8.0.32. Antes de una entrega binaria futura conviene fijar y verificar la selección efectiva del SDK y la resolución completa de dependencias; el manifiesto describe esta ejecución, no promete reproducibilidad bit a bit.

## Árbol resultante y completitud

478 archivos, 174.800.044 bytes, 253 en raíz. Incluye NAP.App.exe, NAP.App/Core/Presentation/AI.dll, deps/runtimeconfig, SixLabors.ImageSharp.dll, Microsoft.Data.Sqlite, SQLitePCLRaw, e_sqlite3.dll, coreclr/hostfxr/hostpolicy, bibliotecas WPF y recursos localizados, perfil público nimroel y algunos avisos de terceros. RobStyle y el recurso RoDo se cargan desde el ensamblado WPF.

El manifiesto enumera todos los archivos con longitud y SHA-256, requisitos y limitaciones. Es una evidencia lateral de un árbol efímero: no se añade al propio árbol ni se produce un ZIP. Se conserva después de eliminar el candidato. No hay paquete transferido cuyo hash se pueda verificar en Drive.

SHA-256 de NAP.App.exe de esta ejecución: **64a16ba7f06cd5bbf74f269dd53840d0f72dff053c571d5a7206fea44978f868**. No usar este hash para aceptar otro build o un ejecutable aislado.

No se distribuyen AutomationHost, CLI, sonda, Git, instalador, updater ni configuraciones de laboratorio. El arranque sin host no exige esos ejecutables; las herramientas Git de NAP invocan `git` externo. Por eso no se certifica completitud para todas las herramientas ni uso sin requisitos técnicos. No se instala Git en el MSI ni se introduce un host para corregirlo.

## Verificación funcional y de runtime

- Sonda net8.0 transitoria, referencia Core con su validación oficial; no dependencia Six Labors directa nueva. Tras retirar archivo/variables de licencia, codifica/decodifica PNG y WebP lossless de 2×2 píxeles en memoria. Verifica que ImageSharp coincide por SHA-256 con la DLL del árbol publicado.
- SQLite abre una base `:memory:` y consulta su versión, 3.53.3. La DLL e_sqlite3 del probe coincide por SHA-256 con la publicada. No crea una base persistente ni un catálogo.
- 49/49 pruebas: vistas de resumen y workflow con dobles, paginación de UI sin host, histórico documental, umbrales puros de licencias y diagnóstico offline/respuestas tardías. Se excluyen casos que crean colas/host, registro administrativo persistente, coordinación con ACL y el caso de ZIP sintético. TRX incluye nombres exactos y 0 omitidas.
- 14 procesos del **NAP.App.exe publicado**, ejecutados con directorio de trabajo sintético fuera del checkout, en modos cerrados existentes. Cubren Inicio/local, Catálogo, Ajustes, Producción, Historial, Diagnóstico sin host, seis estados de licencia e Histórico con exportación. Producen 15 PNG y sidecars con 0 bindingErrors. Cada preview comprueba ocultar/restaurar bandeja, cinco secciones y captura WPF real; todos terminan con código 0.
- Lectura visual de las nuevas capturas de Diagnóstico, Licencias e Histórico: RobStyle presente, cinco secciones visibles y avisos explícitos de muestra/fecha administrativa/simulación; no se observan errores visibles en esas capturas. No certifica DPI, GPU, notificaciones o interacción humana del MSI.

Los procesos se cierran y vuelven a abrir en nube para otros escenarios, no como prueba de persistencia de una sesión humana. Las fechas de licencias son inyectadas por las fixtures; no se cambia el reloj. El histórico conserva 39 referencias ficticias en ocho grupos y no verifica originales.

## Límites de seguridad

El script comprueba ausencia de archivo temporal/variables de licencia antes de ejecutar; enumera extensiones privadas, settings, registros, PDB y obj; busca patrones conocidos de tokens, claves privadas y estructura de licencia en UTF-8/UTF-16, sin emitir contenido. El resultado es favorable dentro de ese alcance. Un escaneo por patrones no es una prueba matemática de ausencia de cualquier secreto posible; se añade la selección de origen (publish de checkout público limpio, sin copiar perfiles del usuario).

Se compararon antes/después archivos y existencia de `%LOCALAPPDATA%/NAP` y `%PROGRAMDATA%/NAP-coordination-v2-*`. No cambiaron. No se hizo trazado de todas las llamadas de archivo del proceso ni comprobación negativa de todas las rutas ordinarias. No se declara aislamiento global ni aptitud para MSI.

La revisión completa de rutas está en Mapa-almacenamiento.md y persistence-source-inventory.txt. El defecto material es anterior a esta tarea: el arranque normal guarda recordatorios y mantiene perfiles y coordinación fuera de `--settings`. Resolverlo con una edición sintética exclusiva evita cambiar la autoridad productiva; se documenta para decisión, sin implementarla unilateralmente.

## Licencias y avisos

La validación oficial se mantuvo. La sonda demuestra que estos caminos reales no requieren sixlabors.lic en runtime. Ningún archivo privado se copia, ni se cambian validadores. El indicador administrativo de vencimiento de NAP no es una validación del proveedor.

La licencia del paquete ImageSharp 3.2.0 establece criterios de elegibilidad para la rama Apache-2.0 de la Split License. Se respeta el contexto Community declarado por el titular sin leer su clave; no se infiere una autorización comercial o pública. Bajo Apache-2.0 deben conservarse el texto de licencia y atribuciones/NOTICE aplicables; si se modificase una biblioteca, sus avisos de cambio también. La DLL usada no se modifica.

El inventario de NuGet tiene ocho paquetes; copia el LICENSE de ImageSharp y avisos de System.Memory. Todavía faltan textos completos verificables para paquetes con licencia por expresión MIT/Apache y para todos los componentes del runtime. System.Memory declara metadata de licencia antigua no normalizada por el script. NAP tampoco tiene un archivo LICENSE en la raíz del checkout inspeccionado. Esto es un bloqueo de cierre del paquete, no se suple con la mera lista de expresiones SPDX. No se declara certificado para distribución pública.

Los logs conservan cinco GHSA únicos: gwg2-r3hj-4w44, j3p4-wp97-rph4, j9gm-c75j-xc9q, jjfr-hcj7-qf5w y wmxv-xphr-5c9g; NU1902/NU1903 siguen visibles. #49 continúa abierto. No se decide aquí su corrección/aceptación pública.

Fuentes: https://docs-v3.sixlabors.com/articles/imagesharp/index.html ; https://raw.githubusercontent.com/SixLabors/ImageSharp/v3.2.0/LICENSE ; https://www.apache.org/licenses/LICENSE-2.0

## Transporte, evidencias y restricciones

Ver Canal-privado.md. Drive privado comprobado; transferencia binaria no establecida. Actions registra cero artefactos. Solo se conservan públicamente texto, TRX, hashes y capturas sintéticas. El log del job se recuperó por el conector GitHub; la CLI recibió una denegación al seguir su URL de descarga y no se eludió la política de red. El log recuperado conserva su contenido y avisos; se escanearon patrones sensibles antes de archivarlo.

No se ejecutaron ZIP de assets, volumen, estrés, regresiones generales, instalación, operaciones productivas, servicios ni host. El único procesamiento de imágenes fue el minúsculo roundtrip en memoria y la renderización WPF de datos cerrados. La aceptación humana, la contención interactiva y el transporte permanecen pendientes. No se tocó el MSI ni se pidió encenderlo.
