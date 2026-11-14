# NAP — AUDITORÍA INTEGRAL FACTUAL PARA CODEX
**Fecha:** 14 de noviembre de 2026 · **Responsable de la auditoría:** ChatGPT (solo inventario y diagnóstico) · **Ejecutor y decisor de la reparación:** Codex
**Proyecto:** `robdor80/NAP` · **Plataforma:** Windows 11 x64, WPF, C#/.NET 10 LTS · **Equipo:** MSI Raider GE78 HX · **Producto:** NAP 2.0.4 instalado en la sesión Windows habitual del propietario.
**Naturaleza:** evidencia del repositorio + conversaciones y capturas físicas del propietario + análisis del ZIP aportado. No es una certificación física ni una intervención en su ordenador. **No autoriza fusión ni borrado.**

## 0. OBJETIVO REAL DEL PROPIETARIO — CONDICIÓN INNEGOCIABLE

**NAP debe funcionar de principio a fin como una aplicación personal, sencilla y eficaz.** El propietario quiere echar un ZIP válido de Nimroel (y, cuando esté configurado, Star Trek) a una bandeja, y que NAP:
1. detecte el ZIP sin trucos, lo asocie al universo y al perfil correctos, y muestre su estado;
2. lo admita, valide y prepare conservando el original;
3. utilice la IA **real (Gemini)** cuando el propietario haya configurado y autorizado su uso, y explique claramente cuando falte configuración o falle la llamada;
4. convierta la imagen a WebP conforme a su perfil, archive el maestro y registre/publicite el resultado local en el catálogo y destino ya configurados;
5. permita consultar en WPF qué ha pasado, con resultados, incidencias comprensibles, sin duplicados y sin historias de errores antiguos presentadas como trabajos nuevos;
6. conserve los 39 retratos históricos y 39 evidencias de maestros PNG sin repetir sus importaciones;
7. se instale y actualice como **una aplicación normal de un solo usuario**: sin otras cuentas de Windows, servicios, políticas de empresa, elevaciones, ACL organizativas, nuevos sistemas de coordinación ni infraestructura paralela.

**TODO O NADA FUNCIONAL:** que arranque, que el Host se conecte, que se encienda el universo o que pase una CI no equivale a que funcione. Se necesita una operación completa real con un recurso adecuado y la IA prevista en el uso final, con salida verificable en pantalla/catálogo.

### Prohibiciones expresas para este encargo de Codex
- **NO** pruebas exhaustivas, suites/baterías de miles de tests, pruebas masivas de ZIP, estrés, benchmarks, matrices Windows/Linux, sanitizers ni nuevas infraestructuras de testing.
- **NO** certificaciones profesionales/de empresa, CUTOVER corporativo, auditorías de permisos/ACL, pruebas de múltiples usuarios, servicios, elevación o actividades regulatorias. Las exigencias históricas de la incidencia #47 se entienden como antecedentes, **no como obligaciones nuevas impuestas por esta auditoría**; prima la decisión vigente del propietario de usar NAP bajo su cuenta normal.
- **NO** gastar cuota de Codex preparando documentación burocrática, escenarios hipotéticos, ensayos repetidos de instalación/desinstalación o matrices de regresión sin relación con el fallo.
- **NO** usar la CI general `NAP CI`; no reimportar retratos históricos, borrar bases de datos, restaurar por fuerza, reiniciar todo el proyecto ni cambiar de tecnología.
- **NO** exigir al propietario que haga otra batería de pruebas en su MSI. Para acreditar funcionamiento, basta **una comprobación práctica focalizada y comprensible**, realizada al final, de la cadena completa con un recurso apropiado. Codex decide los cambios y las verificaciones **mínimas imprescindibles** para implementarlos.
- No publicar el ZIP de prueba ficticio como contenido canónico. No activar facturación Gemini inesperada ni guardar secretos en GitHub.

**Codex es quien decide cómo reparar el proyecto.** Las observaciones siguientes separan hechos demostrados, hipótesis y límites de acceso. No constituyen una arquitectura de solución prescrita.

## 1. Estado EXACTO del repositorio (GitHub consultado el 14/11/2026)

| Elemento | Estado verificado |
|---|---|
| Repo | `robdor80/NAP`; rama por defecto `main` |
| `main` HEAD | `3edf82cd5a8035389111108a022c164eafec68d1` (último mensaje: NAP07 actualizaciones verificadas, 13/11/2026) |
| PR de instalación personal | [Draft #73](https://github.com/robdor80/NAP/pull/73) abierta, base `main`; rama `feature/nap09-preflight-retratos-treskal-20261114` |
| HEAD de código NAP 2.0.4 auditado | `58e404f5204bbc6bc56235af3be69f85e964ac70` (antes de añadir este documento) |
| Otras PR abiertas | [Draft #72](https://github.com/robdor80/NAP/pull/72) NAP08 física; [Draft #62](https://github.com/robdor80/NAP/pull/62) auditor de diversidad, históricamente desfasada respecto a `main`; ninguna fusionada |
| Issues abiertas | [#47](https://github.com/robdor80/NAP/issues/47) CUTOVER histórico; [#49](https://github.com/robdor80/NAP/issues/49) IPC/StorageUnavailable/ImageSharp |
| CI Host personal | [Run #43556615302](https://github.com/robdor80/NAP/actions/runs/43556615302): PASS, compilación y 3 pruebas focalizadas |
| CI instalador 2.0.4 | [Run #43556612568](https://github.com/robdor80/NAP/actions/runs/43556612568): PASS, compilación, instalación temporal, WPF/Host |
| Instalador reproducido en Actions | [Artefacto exacto](https://github.com/robdor80/NAP/actions/runs/43556612568/artifacts/14098707638), `NAP-Setup-2.0.4.exe`, SHA-256 `cd6e777e0c4f7b27f658ad73297376c440aaaa3dc1ed68671f3b0a77c2fed678` |
| Estado físico mostrado por el propietario | NAP 2.0.4 arranca y conecta; Nimroel = «En marcha»; no hay ninguna producción terminada acreditada |
| Fusión | **Prohibido declarar NAP09 terminada y fusionarla basándose en esas CI** |

`main` **no** contiene los cambios personales de NAP09: Codex debe empezar por inspeccionar la rama Draft #73 y decidir su integración posterior. La PR contiene 26 archivos añadidos/modificados en el HEAD examinado. La documentación `docs/NAP09_MODO_PERSONAL_20261114.md` quedó redactada para 2.0.3; el código y el instalador más recientes son 2.0.4. Algunos informes históricos se contradicen porque describen fases anteriores, no el estado vivo.

### Fases realizadas antes de NAP09
- NAP01 y NAP01-B: consolidación y migración .NET 10 LTS, en `main`.
- NAP02: conexión Host/WPF, propietario de colas único y IPC, en `main`.
- NAP03: pipeline de assets, distribuidor de bandeja común y cliente Gemini Host, en `main`.
- NAP04: RobStyle y diversidad; NAP05: correcciones localizadas de recuperación, en `main`.
- NAP06: instalador Inno per-user; NAP07: mecanismo de actualizaciones, en `main`.
- NAP08: revisión física documental, PR #72 Draft, sin equivaler a funcionamiento productivo.
- NAP09: importación/vinculación histórica ya realizada en MSI; modo personal y arranque 2.0.4 en PR #73, **cadena end-to-end pendiente**.

### Otros riesgos anteriores que constan en GitHub, no causas confirmadas del problema actual
- Issue #49: timeout IPC de 20 segundos tras cancelación/reinicio, `StorageUnavailable` histórico e informes NuGet ImageSharp; ninguno probado como causa de la presente ausencia del ZIP nuevo.
- PR #62: trabajo de diversidad IA que puede duplicar funcionalidades integradas en NAP04; no fusionar automáticamente.
- Issue #47: restricciones de un antiguo modelo empresarial con cuentas/ACL/migración de raíces, **incompatible con la decisión posterior y vigente de modo personal**. No convertirlo en una lista de requisitos para Codex.
- No se ha verificado la conexión remota a TeraBox ni se necesita hacerlo para demostrar el procesamiento local de un único asset.

## 2. Estado físico de NAP en el MSI: hechos frente a lo NO demostrado

Capturas del propietario, 14/11/2026:
1. NAP 2.0.3 llegó a abrir WPF tras corregirse el fallo de `LaboratoryCheckpoint`. En Inicio/Producción veía «Automatización sin activar», Nimroel configurado y 0 trabajos.
2. Actualización NAP 2.0.4: aparece «Iniciar automatización». Ventana de confirmación autoriza usar carpetas del usuario con Gemini cuando esté configurado.
3. Tras confirmar: pantalla «En marcha: 1 universo»; Nimroel «En marcha»; botones «Detener» y «Pausar · Nimroel».
4. Se listan dos ZIP antiguos en «Necesitan revisión», ambos con aviso «Perfil sin reconocer»:
   - `scenetreskal_official_city_plan_001.zip`
   - `scenetreskal_mercantile_transit_map_001.zip`
5. Los contadores físicos: pendientes 0, procesando 0, completados 0, necesitan revisión 2. Aparece también «Último análisis de bandeja compartida: 0 distribuidos, 0 ya existentes» y «Paquetes ya presentes en la bandeja mixta: 0». El último análisis no prueba toda la historia de procesamiento.
6. El propietario eliminó/mandó a Papelera el mapa oficial porque era incorrecto. No encuentra físicamente el segundo ZIP. **Las dos incidencias permanecen visibles**.
7. El propietario copió un ZIP válido desde Descargas a la entrada indicada por ChatGPT e informó que **no pasaba nada**; seguían las mismas incidencias antiguas. No hay evidencia de que se admitiese, se convirtiese ni se catalogase ese archivo.
8. La instrucción repetida «pulsa Procesar pendientes» resultó confusa. El código lo coloca arriba en **Inicio** (`DashboardView.xaml`) y cuando se desplaza la página puede no verse; no es un botón de Producción. Además, la automatización activa **ya debería escanear** sin pulsarlo.

**Lo que NO sabemos sin acceso al MSI**: contenido del ZIP antiguo en la cola, ubicación actual del ZIP copiado, si quedó en bandeja compartida o específica, si los scripts de copia/instalación sustituyeron archivos inesperadamente, estado y errores efectivos de la admisión, variables de entorno reales que recibe el proceso Host, configuración y consumo real de Gemini, contenido de las bases SQLite, estado final de la última lectura de pantalla. No afirmar causas demostradas a partir de estos huecos. Codex podrá comprobar de modo focalizado las que afecten directamente a la reparación.

## 3. Carpetas reales e identidades: lo que está confirmado documentalmente

En la configuración aprobada por el propietario para Nimroel:
- Binarios WPF: `%LOCALAPPDATA%\Programs\NAP`.
- Configuración de Windows del producto: `%LOCALAPPDATA%\NAP\settings.json`.
- Configuración de inicio del Host: `%LOCALAPPDATA%\NAP\Host\launch.json` (contiene **secreto IPC**; jamás copiar/publicar el JSON completo).
- Directorio del Host personal: `%LOCALAPPDATA%\NAP\PersonalHost`.
- Bandeja mixta: `C:\Users\andro\Documents\NAP\Entrada` (documentada en la activación personal).
- Workspace Nimroel: `C:\Users\andro\Documents\NAP\Nimroel` y bandeja específica `...\Nimroel\inbox`.
- ProductionRoot: `C:\Users\andro\Documents\GitHub\Videojuego_Nimroel\Worldbuilding\Direccion artistica\Assets`.
- ArchiveRoot: `C:\Users\andro\Documents\Nimroel_Maestros_PNG`.
- Solo universo `nimroel` registrado hasta la última captura; Star Trek sigue sin configurarse en el Host personal.
- Mismo usuario Windows para App y Host. No hay segundo escritor por SID ni servicio. `AutomationPipeServer` mantiene el protocolo de autenticación local.

Estos paths figuran en las actas NAP09, pero **no son una lectura en directo de todo el sistema de archivos**. No alterar rutas ni originales por deducción.

## 4. Cadena de ejecución ACTUAL: inventario por componente

| Etapa | Código principal | Observación factual |
|---|---|---|
| Inicio WPF | `src/NAP.App/App.xaml.cs` | Si existe `launch.json` usa Host remoto en lugar de la composición local tradicional; un error de Host no inicia silenciosamente otro escritor |
| Proceso Host | `src/NAP.App/ManagedAutomationHost.cs`, `src/NAP.AutomationHost/Program.cs` | Lanza/conecta `NAP.AutomationHost.exe --personal-config` bajo el usuario existente; hay evidencia de conexión real |
| Contrato de lanzamiento | `AutomationHostLaunchConfiguration.cs`, `scripts/NAP09.ActivarHostPersonal.ps1` | El perfil completo se copia **como JSON embebido** en `launch.json` solo cuando se genera por primera vez; el script no lo sobrescribe si ya existe |
| Autorización | `AutomationHost.ActivatePersonalAsync`, `AutomationControlViewModel` | El botón de Inicio creó `nap_personal` y puso Nimroel «En marcha»; autorización real confirmada por la UI |
| Entrada compartida | `MixedUniverseInboxRouter.cs` | Examina ZIP en `Entrada`, exige UN manifiesto v2 en raíz del ZIP, identifica universo y hace copia por contenido a `workspace\inbox\nap-<SHA256>.zip`. **Conserva la fuente original**. El nombre en la bandeja específica cambia |
| Reescaneo | `AutomationHost.TickMixedInbox`, `RequestPendingScan` | Barridos automáticos temporizados. «Procesar pendientes» solo marca que se vuelva a escanear; no asegura éxito ni muestra un recibo individual |
| Admisión | `AutomationHost.Tick`, `InboxAdmissionService.cs` | Detecta la bandeja específica, espera estabilidad, registra ZIP/hash y manifiesto; `ProfileUnknown` se emite si no encuentra `(asset_type,production_profile)` en el perfil del Host |
| Preparación | `DurablePreparationService`, `PackageSemanticValidator`, `PackagePreparationService` | Valida paquete, clasificación, ratio, normalización, límites y prepara persistencia |
| IA | `src/NAP.AI/HostGeminiAuditor.cs`, `src/NAP.AutomationHost/Program.cs`, `AutomationHost.WorkflowStep` | **Sin** `NAP_GEMINI_API_KEY` **y** `NAP_GEMINI_MODEL` en entorno del Host no hay auditor; etapa `audit-unconfigured`; no concede PASS automático. Las pruebas previas usaron auditor sintético/mocks, no prueban IA real del MSI |
| Publicación | `DurableExecutionService`, `ProductionAssetExecutor`, `ArchiveMasterExecutor`, `AssetCatalog.db` | Hay código y casos sintéticos de producción WebP/maestro/catálogo, pero **ninguna ejecución nueva completa acreditada físicamente en MSI** |
| Interfaz | `AutomationOverview.cs`, `AutomationControlViewModel.cs`, `DashboardView.xaml`, `WorkflowView.xaml`, `RemoteDesktopService.cs` | Inicio muestra totales del ledger, no necesariamente archivos que hoy siguen en Entrada. Historial/Producción usan Work/Workflow |
| Legado | `LegacyPortraitIndex`, `LegacyMasterEvidenceIndex`, `RemoteDesktopService` | 39 retratos WebP históricos ya importados y 39 evidencias PNG vinculadas según confirmación física anterior; son distintos del nuevo ledger de trabajos |

**Atención a la doble bandeja:** `Documents\NAP\Entrada` es fuente compartida; `Documents\NAP\Nimroel\inbox` es destino propio del universo. El router **renombra** las copias a `nap-<hash>.zip`. El usuario puede esperar ver el nombre original, pero no lo verá en la específica; los estados de UI deben explicarlo, no ocultar fallos de admisión.

## 5. Hallazgos confirmados en código

### H1 — Las «incidencias actuales» incluyen incidencias históricas por diseño
**Evidencia:** `src/NAP.Core/AutomationOverview.cs`, método `AutomationQueueStore.Overview()`: `NeedsReview` suma todos los items persistidos en estados `NeedsReview`, `Rejected` y `MissingSource`, y la lista carga las últimas 8 filas de esos estados **sin comprobar que el ZIP original exista ahora en Entrada**. `AutomationQueueBrowse.cs` aplica lo mismo al filtro de revisión. `AutomationControlViewModel` titula «N necesitan revisión», aunque sean registros antiguos. Por ello eliminar un ZIP no limpia la UI de historial. **Esto explica la persistencia visual, NO demuestra por qué falló la admisión de los dos mapas.**

### H2 — «Procesar pendientes» es una señal, no un resultado
**Evidencia:** `AutomationHost.RequestPendingScan()` se limita a poner `slot.ScanRequested=true`, `_mixedScanRequested=true` y despertar el scheduler. `AutomationDesktopBridge` devuelve un `bool`; `AutomationControlViewModel.Change()` actualiza el overview posteriormente. No emite una explicación individual «detectado / copiado / rechazado / procesando / audit-unconfigured». Para el usuario, puede no pasar nada.

### H3 — El reporte visible de bandeja compartida es SOLO el último barrido
**Evidencia:** `MixedUniverseInboxRouter.RouteAsync` devuelve `Examined, Forwarded, AlreadyPresent, NeedsReview`; `AutomationHost.TickMixedInbox` sustituye `_mixedReport` en cada barrido, y el ViewModel dice «Último análisis». Un barrido posterior de 0 oculta que uno anterior distribuyera archivos. El resultado no sirve como historial durable por archivo.

### H4 — Existe un bloqueo SILENCIOSO posible de IA real
**Evidencia:** `Program.cs` crea Gemini desde `Environment.GetEnvironmentVariable("NAP_GEMINI_API_KEY")` y `NAP_GEMINI_MODEL`. `HostGeminiAuditor.Create` retorna `null` si ninguna está presente y lanza excepción si falta solo una. En `AutomationHost.WorkflowStep`, si el asset necesita auditoría y `_auditor is null`, devuelve `audit-unconfigured`, no una producción completada. La pantalla Inicio reduce el estado a «En marcha» y recuentos; **no informa con claridad de este bloqueo**. No conocemos el entorno Gemini real del MSI; por tanto es **condición pendiente de confirmar, no causa demostrada del ZIP invisible**.

### H5 — «Perfil sin reconocer» contradice el único manifiesto aportado y el perfil del Host
**Evidencia:** el propietario subió `scene_treskal_official_city_plan_001_manifest.json` con `asset_type:"scene"`, `production_profile:"scene_cartography"`, clasificación `culture:norgard`, `location:treskal`. La configuración `config/universes/nimroel/profile.json` contiene exactamente esa regla. Además, PowerShell 5.1 del MSI leyó `launch.json` y confirmó en `Universes[0].ProfileJson`: `portrait/portrait_npc`, `scene/scene_cartography`, `scene/scene_environment`.
**Código:** `InboxAdmissionService.Continue` emite `QueueIncident.ProfileUnknown` solamente cuando `_context.Profile.TryGetAssetRule(manifestFinal.AssetType,manifestFinal.ProductionProfile)` falla. **No se ha abierto el ZIP exacto que generó el registro viejo**, así que no puede afirmarse que su manifiesto coincida con el archivo JSON presentado aparte. Posibilidades pendientes: ZIP diferente, contenido declarado distinto, error ya persistido de otra ejecución; no decidir sin evidencia.

### H6 — El ciclo de vida de una incidencia no se corresponde con la expectativa del usuario
**Evidencia:** `AutomationQueueStore` persiste estados y observaciones por hash de ZIP. `InboxAdmissionService.Reconcile` no convierte automáticamente en «resuelto» un `NeedsReview` histórico solo porque se borre el origen. No se ha acreditado una acción sencilla para archivar/desestimar incidencias antiguas sin perder historia. Para el usuario, eso tapa las novedades. **No borrar filas SQLite ni archivos internos sin comprender su relación con maestros y recibos.**

### H7 — La importación histórica está probada, pero el nuevo pipeline no
**Evidencia:** `docs/NAP09_PREFLIGHT_39_RETRATOS_TRESKAL_20261114.md`: importación WebP visible y ventana «39 evidencias de PNG históricos incorporadas» confirmada 14/11, con 14 coincidencias SHA, 1 coincidencia Git y 24 líneas base nuevas. `RemoteDesktopService` fusiona resultados nativos e históricos. Eso NO acredita que `portrait_treskal_farmer_male_901.zip` haya pasado jamás por la automatización.

### H8 — La interfaz confunde actualización visual y estado de ejecución
**Evidencia:** botón «Procesar pendientes» está en `DashboardView.xaml` (Inicio), NO en Producción. `MainWindow.xaml` tiene «Actualizar» de pantalla y controles inferiores de Host. El número «Completados» en Inicio cuenta SOLO trabajos del ledger, no los 39 WebP históricos. La propia UI muestra «RobDev - NAP 2.0» aun con instalador 2.0.4, sin aclarar la versión operativa. Prioridad de UX según Codex.

### H9 — La documentación y los requisitos históricos contienen decisiones superadas
`docs/NAP09_P02_ARQUITECTURA_CUTOVER_REAL_20261114.md` habla del estado previo a implementar `PersonalMode`; `docs/NAP09_MODO_PERSONAL_20261114.md` cita 2.0.3; #47 conserva una perspectiva empresarial de cuentas escritoras, ACL y CUTOVER. **El propietario ha rechazado expresamente ese modelo.** Codex no debe imponerlo ni reabrirlo; el objetivo operativo es la app personal.

## 6. ZIP real disponible para inspección por Codex (subido al chat, NO al repositorio)

**Nombre:** `portrait_treskal_farmer_male_901.zip`
**Tamaño:** 8.430 bytes · **SHA-256 exacto:** `6d853ea6cd6cecc1f2c97fec0aba7b35560f4319d86530be3c95545dfbc6ee90`.
**Análisis realizado en este entorno:** ZIP legible; CRC de todas las entradas correcto (`ZipFile.testzip()==None`); 5 entradas planas sin subdirectorios:
- `portrait_treskal_farmer_male_901.png` (9.049 bytes; PNG RGB 768×960, se abre y verifica).
- `portrait_treskal_farmer_male_901_manifest.json`.
- `portrait_treskal_farmer_male_901_prompt.md`.
- `portrait_treskal_farmer_male_901_info.md`.
- `portrait_treskal_farmer_male_901_visual_identity.json`.

**Manifiesto v2:** `universe_id=nimroel`, `asset_id=portrait_treskal_farmer_male_901`, `asset_type=portrait`, `production_profile=portrait_npc`; clasificación `culture=norgard`, `location=treskal`, `role=farmer`, `sex=male`. Coincide en tipo/perfil y dimensiones 4:5 del perfil Nimroel. Una inspección de estructura no equivale a ejecutar el pipeline NAP real.

**NO ES CONTENIDO CANÓNICO:** el propio `_prompt.md` dice «Prueba funcional de NAP 2.0. Retrato geométrico ficticio ... No es asset canónico», `_info.md` dice «Paquete sintético ... No incluir en repositorios reales», y el JSON visual dice `TEST - fictional example, not canon`. Es apto como muestra técnica **aislada**, no debe publicarse en la carpeta del videojuego como contenido definitivo. Codex decidirá si utilizarlo de modo aislado o elegir un ZIP canónico para el único comprobante real. No subir ZIP de prueba a GitHub ni editarlo para forzar PASS.

**Nota importante para Codex:** este adjunto está en la conversación y no garantizado como archivo local de su entorno. Si necesita exactamente sus bytes, solicitarlos o derivar un fixture sintético equivalente sin escribir en ProductionRoot real.

## 7. Análisis de los dos ZIP de mapas viejos

- `scenetreskal_official_city_plan_001.zip`: marcado «Perfil sin reconocer» en NAP; el propietario afirma que el mapa es incorrecto, lo descartó y lo mandó a Papelera. Su **manifest JSON enviado por separado** parece coherente con `scene_cartography`, pero no es prueba del ZIP archivado dentro del ledger.
- `scenetreskal_mercantile_transit_map_001.zip`: marcado «Perfil sin reconocer»; el propietario no lo encuentra; no hay manifiesto ni ZIP disponibles para contrastar.
- El prefijo de nombre de ZIP mostrado es `scenetreskal_` mientras que el `asset_id` del manifiesto separado comienza `scene_treskal_`. La diferencia **no demuestra** la causa de `ProfileUnknown`: el cargador evalúa los campos del manifiesto interno, no una conjetura por el nombre exterior.
- No retirar datos internos sin autorización y sin localizar la causa de su aparición visual. No hacer de la reparación de mapas descartados el objetivo principal de Codex.

## 8. Qué ha demostrado la CI y qué NO

El run Windows `#43556615302` demuestra que el Host personal compila y pasa tres pruebas focalizadas de arranque/control. El run Windows `#43556612568` demuestra el instalador 2.0.4, invocación WPF con configuración personal y desinstalación temporal conservadora. **Ninguna prueba garantiza que el ZIP nuevo aparezca en la pantalla real del MSI ni que Gemini real lo audite y se termine la publicación/catálogo.**

Los informes NAP03 documentan procesamiento sintético de ZIP y catálogo, no la misma ejecución en `C:\Users\andro\...`. El `main` actual está a NAP07 y la personalización vive en PR #73. Usar esas pruebas existentes como antecedentes, **no duplicar suites**.

### Hechos no verificados, sin inventar
- ¿El ZIP nuevo está en la bandeja compartida, en la específica, o en ninguna? No hay inventario leído del MSI tras su copia.
- ¿Qué informó el router para ese archivo específico, qué error tuvo, o en qué barrido? Falta observabilidad durable por ZIP.
- ¿El archivo quedó renombrado como `nap-<hash>.zip`? El router lo haría si lo distribuyó; no consta captura de esa copia.
- ¿Hay API key/model Gemini accesibles en el entorno del Host iniciado por WPF? Sin evidencia.
- ¿Se puede completar publicación nativa con las reglas del ZIP de prueba y carpeta real tal como están? Sin ejecución física.
- ¿El aviso `ProfileUnknown` histórico corresponde al JSON aportado? No demostrado.
- ¿Hubo cambios manuales/borrados en bases? No consta evidencia; **no presumirlos**.

## 9. Condiciones de entrega a Codex (NO es un diseño técnico)

Codex recibe permiso para estudiar y **decidir** el arreglo integral de NAP. Lo que debe quedar funcionando, sin excusas ni fragmentos:
- Un ZIP correcto entra en la carpeta que el usuario conoce; el software lo detecta automáticamente sin esconderlo, valida universo/perfil, y **muestra en lenguaje humano su evolución**.
- Si falta IA real, configuración de Gemini o consentimiento, la app lo muestra con claridad y permite configurarla/usarla con la intención del propietario, sin simular éxito.
- Con IA real autorizada, la cadena produce y registra un recurso aceptado, conserva el maestro/original, crea WebP de acuerdo con perfil y deja una ficha/resultado localizable.
- Los errores antiguos no se confunden con un ZIP actual ni impiden identificarlo. El usuario puede entender/gestionar una incidencia sin manipular SQLite o mover archivos a mano.
- Los 39 retratos históricos y las evidencias PNG permanecen, las rutas vigentes no cambian por sorpresa y los recursos reales de Nimroel no se sobrescriben por el ensayo.
- Aplicación normal 1 usuario; experiencia personal sencilla; eventual Star Trek se configura según perfil cuando se necesite, sin inventar universo ni crear infraestructura anticipada.
- **Una sola aceptación funcional final, no exhaustiva:** un recurso apropiado desde la bandeja al resultado visual y sus archivos/catálogo, con Gemini realmente conectado si se pretende usar esa capacidad. Codex decide cómo prepararlo, comprobando lo necesario **sin baterías de pruebas ni certificaciones**. No pedir al propietario repetidos tests sobre MSI; si requiere una única actuación, que sea concreta y justificada.

Si Codex considera que una parte no puede repararse, que lo explique con el bloqueo REAL, no lo sustituya por más pruebas, más frameworks o una nueva hoja de ruta burocrática.

## 10. Archivos prioritarios para la lectura de Codex (inventario, NO orden de modificación)

**Arranque/config:** `src/NAP.App/App.xaml.cs`, `src/NAP.App/ManagedAutomationHost.cs`, `src/NAP.AutomationHost/Program.cs`, `src/NAP.Core/AutomationHostLaunchConfiguration.cs`, `scripts/NAP09.ActivarHostPersonal.ps1`, `config/universes/nimroel/profile.json`.

**Host y transporte:** `src/NAP.Core/AutomationHost.cs` (reescaneo, TickMixedInbox, Tick y WorkflowStep), `src/NAP.Core/MixedUniverseInboxRouter.cs`, `src/NAP.Core/InboxPackageDetector.cs`, `src/NAP.Core/InboxAdmissionService.cs`, `src/NAP.Core/AutomationHostControlStore.cs`, `src/NAP.Presentation/AutomationDesktopBridge.cs`.

**Perfiles y pipeline:** `src/NAP.Core/AssetManifestV2Loader.cs`, `src/NAP.Core/UniverseProfileLoader.cs`, `src/NAP.Core/PackageSemanticValidator.cs`, `src/NAP.Core/AutomationPolicyContracts.cs`, `src/NAP.Core/DurablePreparationService*.cs`, `src/NAP.Core/DurableExecutionService*.cs`, `src/NAP.AI/HostGeminiAuditor.cs`.

**Información de usuario/cola:** `src/NAP.Core/AutomationOverview.cs`, `src/NAP.Core/AutomationQueueBrowse.cs`, `src/NAP.Core/AutomationQueueStore.cs`, `src/NAP.Presentation/AutomationControlViewModel.cs`, `src/NAP.Presentation/WorkflowViewModel.cs`, `src/NAP.App/Views/DashboardView.xaml`, `src/NAP.App/Views/WorkflowView.xaml`, `src/NAP.Presentation/RemoteDesktopService.cs`.

**Documentación y antecedentes:** `docs/NAP_PLAN_MAESTRO_AMPLIADO_SIN_CODEX_20261113.md`, `docs/NAP03_PROCESAMIENTO_ASSETS_20261113.md`, `docs/NAP09_PREFLIGHT_39_RETRATOS_TRESKAL_20261114.md`, `docs/NAP09_MODO_PERSONAL_20261114.md`; [PR #73](https://github.com/robdor80/NAP/pull/73), [#47](https://github.com/robdor80/NAP/issues/47) y [#49](https://github.com/robdor80/NAP/issues/49).

## 11. Responsabilidades y límites tras esta auditoría

- ChatGPT **no ha cambiado código ni ejecutado nada en el MSI** durante esta auditoría. Solo ha leído el repositorio, CI/documentos y adjuntos, y añadido este documento en la rama Draft #73.
- **Codex decide reparación, alcance mínimo, código, diagnósticos y verificación.** No usar este informe como una obligación de ejecutar tareas opcionales.
- `main` no se modifica, la PR #73 permanece Draft y **NAP no se declara funcional** hasta que la cadena de ZIP + IA + WebP + maestro + catálogo sea comprobada de verdad.
- Prioridad absoluta: que la aplicación personal **FUNCIONE**. Todo lo que no contribuya directamente a ello queda fuera de este encargo.
