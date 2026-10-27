# NAP 2.0 · F8.0 · Auditoría y contrato de laboratorio Git

Fecha: 2026-10-27 UTC. Base verificada directamente en GitHub: **`ae6aa5b05a47917e7c53f854c4506725cc7c1bf8`**. Rama de trabajo: `chatgpt/nap-2-f8-safe-msi-lab`.

## Dictamen

Es viable preparar en Cloud un laboratorio visual de la interfaz Git auténtica con una composición cerrada, exclusivamente sintética. No es seguro lanzar el arranque normal con otras rutas: existen configuraciones y coordinadores independientes de las raíces del universo. La variante F6 tampoco muestra la gestión F7 completa. Se implementa una distribución F8.1 que excluye el arranque normal en compilación y aloja el `GitView` original con su `GitViewModel` original mediante un adaptador específico. No copia ni redibuja sus controles.

**F8.0 completada como auditoría técnica de las rutas indicadas; F8.1 Cloud preparada, aceptación física y auditoría independiente pendientes.** No se certifica MSI, Windows visual, aislamiento del SO, producción, CUTOVER ni resolución de #47/#49.

## Clasificación de conclusiones

V = **Verificada en código o evidencia**; H = **Hipótesis pendiente de comprobación**; B = **Bloqueo técnico**; L = **Comprobación reservada a Codex Local**. Las verificaciones estáticas acreditan el código inspeccionado, no todos los efectos posibles del sistema operativo o de binarios ajenos.

| ID / clase | Conclusión y referencia concreta |
|---|---|
| 01 V | GET GitHub `/repos/robdor80/NAP/branches?per_page=100`: única rama `main`, SHA `ae6aa5b...`, `protected=true`. Checkout inicial limpio en ese mismo SHA, rama local inicial `work`. |
| 02 V | PR #57 CLOSED/MERGED, merge SHA indicado, `merged_at=2026-10-27T04:46:09Z`; búsqueda de PR abiertas vacía. #47 y #49 OPEN. Ninguna de ellas se modifica. |
| 03 V | F7.1: `NAP_2_0_F7_1_CIERRE.md`, arquitectura y matriz presentes; el acta conserva el estado histórico anterior a F7.2. No se inventa PR independiente. |
| 04 V | F7.2: `GitPublicationModels`, `GitPublicationPolicyStore`, `GitOutboxStore`, `GitBatchPublicationService` y documentación `F7_2_IMPLEMENTACION/PRUEBAS`. Ledger Outbox separado de recibos nativos. |
| 05 V | F7.3: `GitBatchPublicationPush`, `GitProductionService`, documentos F7.3/A1/A2; presupuestos de reintento y reconciliación durable. No se reejecutan estos escenarios históricos. |
| 06 V | F7.4/A1: `GitView.xaml`, `GitManagementViewModel`, `ShellGitManagement`, `GitManagementUiService`, `GitOutboxGuidance`, `GitRepositoryIdentity`. F7.5 auditó estos contratos y evidencias. |
| 07 V | F7.5: acta `NAP_2_0_F7_5_AUDITORIA_Y_CIERRE_TECNICO.md` presente. Su portada anterior al merge no contradice el merge posterior de #57. |
| 08 V | `App.OnStartup`: rama F6 compilada, preview por argumento, rama `--automation-config`, después arranque ordinario. `App.xaml` carga RobStyle; no tiene `StartupUri`. |
| 09 V | Arranque ordinario: `LocalUniverseSettingsStore.DefaultPath`, `UniverseProfileStore` instalado y local; `ExplorerViewModel` carga configuración y valida/discover/lee catálogo. `ShellViewModel` crea comandos, páginas y adaptadores; no basta cambiar cwd. |
| 10 V | `ProfessionalUiService` construye por defecto `GitProductionService`/`GitProductionInspector`; usa dispatcher local si falta uno. Sus lecturas pueden recorrer Inbox/journals/catálogo/backups; inspeccionar Git consulta remoto. |
| 11 V | Si existen `NAP_GEMINI_API_KEY` y `NAP_GEMINI_MODEL`, arranque ordinario no-smoke compone `HttpClient`/auditor Gemini. La variante F8.1 no consulta esas variables. |
| 12 V | `LicenseRegister.DefaultPath`: LocalAppData/NAP/license-reminders.json; es un registro de recordatorios, distinto de la licencia Six Labors. MainWindow programa comprobación diaria y refresco host mediante timers, y crea `TrayHost`. Se evita instanciar MainWindow en F8.1. |
| 13 V | `LocalUniverseSettingsStore`: LocalAppData/NAP/settings.json; `UniverseProfileStore`: LocalAppData/NAP/universes y mutex `NAP.UniverseProfiles.*`; `GitPublicationPolicyStore` predetermina LocalAppData/NAP/git-publication/<universo-binding>. |
| 14 V | `UniverseStorageConfig`: workspace/inbox, staging, state, cache y AssetCatalog.db; producción/archivo independientes. Outbox `StateRoot/git-outbox-v1`; otros journals, ZIP, assets y recibos requieren contextos operativos. F8.1 solo crea objetos de rutas ficticias, nunca estos almacenes. |
| 15 V | `ExecutionMutex.Acquire` también abre `ExecutionFileLease`. En Windows `PrivateDirectory` usa CommonApplicationData/NAP-coordination-v2-<SID>, valida/puede crear directorio con ACL. No se aísla cambiando solo StateRoot. F8.1 no construye ni adquiere coordinadores. |
| 16 V | `AutomationProtocol`: cliente/servidor named pipes CurrentUserOnly, autenticación y operaciones tipadas. `RemoteDesktopService` compone caché temporal `nap-host-client-*`; un error remoto no habilita fallback escritor local. F8.1 no crea estos clientes, servidor ni caché. |
| 17 V | `NAP.AutomationHost/Program.cs` exige `--laboratory-config`, valida contextos y construye host/bridge/servidor. `AutomationHost` contiene owner/scheduler, admisión, ejecución y turnos Git. No debe arrancarse para este ensayo visual. |
| 18 V | Búsqueda focalizada de Process.Start/ProcessStartInfo, pipes y coordinación: procesos Git en `GitProductionProcess`/`GitBackupProcess`; host/scheduler en AutomationHost. No se encontró instalación de servicio/tarea en la composición visual inspeccionada. Esto no inventaría los arranques existentes del MSI. |
| 19 V | `MainWindow` normal inicializa shell al Loaded, integra tray y timers. Preview F6 deshabilita contenido inicialmente y captura; AcceptanceOnly tiene navegación limitada, fixtures F6 y recibos Git vacíos (`Phase6Preview`). Smoke conserva componentes reales y no constituye barrera F8.1. |
| 20 V | `GitView` usa DataContext GitViewModel y comandos del DataContext del Window. En producción son de ShellViewModel: autorizaciones, revocación, commit, Recover (puede escribir receipt), retry y push. `UiAsyncCommand.ExecuteAsync` consulta CanExecute incluso ante invocación directa. |
| 21 V | F8.1 mantiene sin cambios `GitView.xaml/.cs`, `GitViewModel`, comandos productivos y temas. `RobStyle.Templates.xaml` mapea GitViewModel al GitView original. `RobTitledFrame` y conversores utilizados son visuales. El recurso RoDo carga PNG empaquetado; no solicita red. |
| 22 V | `AutomationReadViews.Json` ya permite proyecciones de lectura GitRepositoryStatus/plan/receipt y las marca en `ConditionalWeakTable`; `RequireNative` rechaza estas proyecciones. Construir opciones JSON no crea pipes. Se reutiliza este mecanismo, sin modificar Core ni constructores de autoridad. |
| 23 V | `NAP.Core.csproj`: ImageSharp **3.2.0** directa, `SixLaborsLicenseFile` externo por defecto ../../sixlabors.lic; Sqlite 8.0.31. NAP.App Windows/WPF net8.0-windows; Presentation/Core net8.0; NAP.AI transitivo. Ninguna versión cambia. |
| 24 V | Target oficial del paquete restaurado `SixLabors_ValidateLicense` BeforeTargets=CoreCompile: Debug permite continuar con avisos, Release exige licencia; no se modifica, desactiva ni sustituye. NAP.Core mantiene AssemblyName, importante para licencia Community acotada a ensamblado. |
| 25 V | `NAP_2_0_IMAGESHARP_ACTIONS_LICENSE.md`, `NAP_2_0_FASE_5_FINAL_REVIEW.md` y evidencia Fase 5 registran validación oficial previa Windows/Linux Release, Community y 220 pruebas dirigidas. `.github/scripts/Invoke-LicensedDotnet.ps1` es exclusivo de Actions, consume secreto temporal y limpia; no corresponde ejecutarlo en MSI/Cloud. No se solicita nueva licencia. |
| 26 B | Cloud no tiene licencia privada seleccionada ni credenciales configuradas para ella; **Release F8.1 no se ejecuta ni acredita**. El SDK .NET 8.0.427 se instaló solo en /tmp para build Debug oficial y pruebas sin secretos. |
| 27 V | Pruebas nuevas limitadas a contrato, fixtures, ViewModel y denegaciones; compilación WPF cruzada y lectura de IL. Construir proyectos de host/probe como dependencias del proyecto de tests no los ejecuta. No se ejecutó NAP, hosts, ZIP, Git de motor ni suites históricas. Véase evidencia F8.1. |
| 28 L | Ejecutar binario real WPF en Windows 11 MSI, bindings/rendering, DPI, selección, scroll, teclado y navegación. Cloud Linux no puede demostrarlo. |
| 29 H/L | Confirmar en el MSI que runtime/antivirus/políticas del equipo no producen errores adicionales y que observación por PID no muestra escrituras/IPC/red NAP inesperadas. Si no se puede observar con herramientas ya disponibles, documentar límite y no certificar aislamiento físico. |
| 30 L | Existencia actual de la carpeta histórica F7, ubicación/disponibilidad/validez privada de licencia original, pantallas y estado real de servicios/procesos del equipo. No se accede al MSI desde Cloud ni se presupone su estado. |

## Riesgos y decisión arquitectónica

1. **Arranque accidental normal**: riesgo alto con flags de preview/smoke o binario equivocado. Nueva propiedad `Phase81GitLabOnly=true`, nombre distinto `NAP.GitVisualLab`, rama de arranque exclusiva antes de composición. El binario normal rechaza argumentos `--f81*`; el binario lab jamás selecciona producción al faltar argumentos.
2. **Fallback a rutas globales/coordinación**: riesgo alto si se reutiliza ProfessionalUiService o shell completo. El adaptador no construye Shell/Explorer/MainWindow, policy store, LicenseRegister, dispatchers, host ni coordinadores. La aplicación no necesita persistencia; deniega acciones, no redirige motor productivo.
3. **UI inventada o estados incongruentes**: se conserva la vista y ViewModel de F7.4, se adaptan únicamente los bindings del Window. Los cuatro modos son exactamente Disabled, Manual, AutoCommitBatch y AutoCommitAndPushBatch; Outbox usa Pending/Committed/Blocked/Pushed; receipts Prepared/Committed/Pushed. Los escenarios adicionales son nombres del catálogo visual, nunca nuevos modos productivos.
4. **Fixtures confundidos con autorización**: títulos y banner explícitos, proyecciones no nativas, ninguna evidencia durable persistida, todos los comandos operativos siempre denegados y el adaptador rechaza invocación directa de sus mutaciones. La inspección remota también queda bloqueada.
5. **Enlaces/reutilización de carpeta**: ruta temporal directa con GUID, marcador exacto, ejecutable en app, rechazo de reparse points/ancestros Git/entradas inesperadas. El preparador crea una carpeta nueva. El marcador acredita intención, **no es una credencial ni una frontera contra un atacante de la misma cuenta**.
6. **Garantía excesiva**: no hay sandbox, ACL nueva, contenedor ni confinamiento NTFS. Existen DLL de producto transitivas en distribución porque los modelos las necesitan; la prueba IL y revisión acreditan la ruta de composición, no resistencia ante binarios modificados, inyección, sustitución concurrente de archivos o actividad del SO. El laboratorio no escribe y nunca consume contenido de repos/ZIP/assets, de modo que no requiere locks productivos.

La solución es reversible eliminando en una futura revisión los archivos específicos y la propiedad/guardas de arranque. Este trabajo **no borra** ninguna carpeta, evidencia ni dato. No modifica Core, IPC, esquema, licencias, workflows, motor Git ni comportamiento operativo autorizado de producción.

## Contrato obligatorio A–G

| Garantía | Implementación y criterio de aceptación |
|---|---|
| A · explícita | Distribución lab compilada + dos argumentos exactos en orden + marcador; extras rechazados. `--f81-verify-isolation` solo permite verificar el contrato y salir antes de construir el modelo. Flags faltantes/incorrectos: exit 1 sin ventana operativa. |
| B · datos | Root directo TEMP/NAP-F81-Lab-<GUID>, app dentro; prohibidas ubicaciones NAP persistentes, UNC, enlaces y ancestros Git. Sin settings, profiles, política, journals, recibos, SQLite ni diagnósticos de disco de NAP. Fixtures solo en memoria. Preparador/build y redirección de stdout/stderr/evidencias escriben dentro del root; restore usa caché estándar .NET externa, como herramienta de desarrollo, no como componente NAP en ejecución. |
| C · procesos | Solo exe dedicado, sin tray, timers, agentes, host, servicio ni tarea. Preparación invoca únicamente Git de desarrollo para verificar checkout y dotnet de compilación; nunca motor Git de NAP. |
| D · coordinación | Ningún owner, mutex, file lease ni named pipe NAP. Ausencia de coordinación de NAP es el doble sintético; no se reutiliza namespace productivo. No se cambia ACL/cuenta/servicio. |
| E · Git | Sin repositorio físico. Todos los comandos de escritura/autoridad/recovery/retry e inspección remota CanExecute=false permanente; el delegado también deniega. IGitManagementUiService sintético lanza ante propuesta/guardar/revocar/retry incluso llamada directa. Ver recibo y refrescar solo consultan memoria. |
| F · red | Ningún HttpClient, auditor, runner, GCM o cliente IPC en composición. Sin URLs de remoto en fixtures. No se afirma impedir tráfico del runtime/SO; la ausencia de red de componentes NAP requiere observación local adicional. |
| G · fallo seguro | Contrato/dependencias faltantes: rechazo, sin fallback. Escenario desconocido o error: limpiar estado anterior y STOP; nunca resolver con servicios productivos. Error de binding: contador y exit 8 al cerrar; no aceptación. |

## Evidencias reutilizadas y puertas de aceptación

Se conservan F7.5 y los TRX finales F72 (14+5), F73 (27), F73-A1 (5), F73-A2 (5), F74 (17), F74-A1 (2): **75 ejecuciones históricas, con solapamiento**, no nuevas pruebas ni 75 escenarios distintos. En F8.0 se comprobó presencia y contadores de los siete TRX; no se repiten Git/ZIP/volumen. Fase 5 conserva validación licenciada y reservas de #49; no se reconfiguran secretos ni se reabre una compra de licencia.

Aceptación Cloud: tests focalizados correctos, build WPF cruzado, IL de arranque exclusivo, vista/temas productivos sin diff, script parseado, publicación fuera de triggers. Aceptación física: runbook F8.1, licencia original validada en Release, preflights, ejecución real MSI y evidencias de aislamiento/UX. **F8.1 solo podrá cerrarse tras auditoría favorable independiente de ChatGPT sobre estas evidencias.** #47/#49 permanecen abiertas, también después de una aceptación visual F8.1.
