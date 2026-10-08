# Fase 14 · UI profesional

La ampliación posterior [Normalización técnica segura](IMAGE_NORMALIZATION.md)
añade a Producción preparación de preview, comparación, límites, aprobación/rechazo
y selección de candidatos verificados. Respeta RobStyle y las confirmaciones
existentes; una normalización aprobada nunca habilita publicación por sí sola.
El historial de Fase 14 que sigue describe su entrega original.

Implementación funcional de **14.1–14.11**. La primera revisión física en el MSI aprobó
el RobStyle; la pasada final de UX, perfiles importables y mascota queda pendiente de **segunda revisión física**.
La fase **no está cerrada** y no autoriza commit, push, merge o PR. Rama: `feature/professional-ui-v1`.
Base de trabajo: merge de Fase 13 `8b32c76ea8a459cd842605c95a528a0acf6e3f91`.

## Auditoría y referencia visual

Antes de implementar se comprobó el shell de Fase 11, sus adapters, paginación LRU,
panel de recycling, miniaturas y pruebas existentes. MainWindow concentraba catálogo,
estadísticas y ajustes; seis áreas seguían deshabilitadas. La base Windows pasó
**3221 tests**, con build Release sin warnings ni errores.

La referencia se leyó del repositorio local RobGit_App usando `git show` del commit
**d6aabd9e82a4a65d0d46c03a7864cd3e1ca3c37a**. Fuentes canónicas:

- [RobGitTheme.kt](https://github.com/robdor80/RobGit_App/blob/d6aabd9e82a4a65d0d46c03a7864cd3e1ca3c37a/app/src/main/java/es/robertodorado/robgit/RobGitTheme.kt).
- [MainActivity.kt](https://github.com/robdor80/RobGit_App/blob/d6aabd9e82a4a65d0d46c03a7864cd3e1ca3c37a/app/src/main/java/es/robertodorado/robgit/MainActivity.kt).
- [RodoUi.kt](https://github.com/robdor80/RobGit_App/blob/d6aabd9e82a4a65d0d46c03a7864cd3e1ca3c37a/app/src/main/java/es/robertodorado/robgit/RodoUi.kt).

La mascota oficial de RoDo se incorpora por petición explícita de la revisión física.
Su copia PNG y comparación exacta de píxeles están documentadas en
[PROVENANCE.md](../src/NAP.App/Assets/RoDo/PROVENANCE.md). RobGit no se modifica.

| Token compartido | Valor |
| --- | --- |
| Petroleum / Dark / Deep / Light | #05131A / #040F15 / #020B10 / #0B202B |
| Ice / IceMuted | #EAF6F8 / #B8D6DB |
| Success / Warning / Error / AI | #9ED9C2 / #F1CF8A / #FFB4AB / #C8C4FF |
| Radio / stroke / gap / padding | 18 / 1 / 16 / 24 |
| Elevación reposo / pulsado | 5 / 1 |
| Opacidad deshabilitada contenido / borde | 0.42 / 0.6 |

RobGit usa Compose/Android y NAP usa WPF/Windows. NAP adapta la densidad, Segoe UI,
iconos MDL2, sidebar y tamaños de escritorio, conservando la paleta, radios, trazos,
profundidad, pesos tipográficos y semántica de estados. No queda el cerúleo histórico.

## Arquitectura

`NAP.Presentation` sigue siendo net8.0 portable y sin WPF. `ShellViewModel` coordina diez
ViewModels de página, navegación, confirmaciones y actividad observada. Reutiliza
`ExplorerViewModel`, sus adapters y cache. `ProfessionalUiService` adapta contratos
Core tipados y ejecuta I/O pesado en workers mediante Task.Run; el auditor existente
es async. `UiAsyncCommand` impide duplicar una acción en curso y actualiza CanExecute.

`UniverseProfileStore` en Presentation añade una frontera local portable de perfiles;
reutiliza UniverseProfileLoader y UniverseRegistry de Core, sin cambiar sus contratos.
`NAP.App` contiene composición, diez UserControls, recursos, geometría del frame,
conversor semántico, diagnósticos de bindings, diálogo de planes y bandeja nativa.
MainWindow compone header, sidebar, content y status bar; ya no contiene las páginas.
Windows Forms se usa únicamente para NotifyIcon desde el framework Windows Desktop,
sin añadir paquetes NuGet. App referencia el proyecto NAP.AI existente.

**NAP.Core, NAP.AI, NAP.Cli y los contratos/runtime de Fases 0–13 no se modifican.**
No cambia SQLite ni ningún schema.

## RobStyle reutilizable

- `RobStyle.xaml`: entrada pública del sistema y dictionaries de colores, tipografía, botones, controles y templates.
- Colores, espacios, radio, stroke, tipografía, iconos, tamaños de tiles y elevación centralizados.
- `RobTitledFrame` + `RobFrameOutline`: header medido y geometría abierta. El trazo superior
  termina antes del título y continúa después; no existe un borde pintado debajo y tapado con un parche.
- Botones Primary, Secondary, Danger, Navigation y ActionTile: superficie oscura,
  stroke Ice, radio 18, profundidad 5→1 con transición breve, foco visible y opacidades separadas.
- Inputs, selector, listas, selección de tiles, barras horizontales y scrollbars oscuros.
- Templates reutilizables para objetivos, checkpoints, incidencias y receipts.

## Pantallas y alcance real

| Apartado | Implementación funcional |
| --- | --- |
| 14.1 | Ventana maximizada, nueve destinos de sidebar activos, selector por perfil, Git accesible en header/Inicio/Backups, estados explícitos. |
| 14.2 | Assets, Inbox, jobs activos y backups de lecturas reales; objetivos y diagnósticos. Un dato no disponible es —, nunca un cero supuesto. |
| 14.3 | Producción: descubrimiento de ZIP, copia a staging, extracción segura, validación y planes reales; auditor de Fase 7; publicación con confirmación y Coordinator. |
| 14.4 | Catálogo paginado, LRU, viewport/recycling, miniaturas diferidas, filtros AND, clasificación, traits tipados, ficha y documentos originales. |
| 14.5 | Cobertura real, barras Ice/Success, campañas existentes y guardado explícito de objetivo con los filtros activos del catálogo. |
| 14.6 | Estadísticas actuales por tipo/perfil/clasificación/traits, sin tendencias inventadas ni librerías de charting. |
| 14.7 | Checkpoints durables, receipts y hasta 100 observaciones de la sesión con fecha de observación real; STOP y diagnóstico plegado. |
| 14.8 | Frames de universos, almacenamiento, catálogo, Git, Archivo y aplicación; perfiles importables, rutas explícitas e indicadores de validación real. |
| 14.9 | Histórico verificado de los tres tipos de backup; creación explícita; restore y retención con planes Core congelados y confirmación. |
| 14.10 | Ocultar/restaurar, abrir secciones y salir desde la bandeja Windows. Minimizar a bandeja es opt-in por sesión y se limpian recursos al cerrar. |
| 14.11 | RoDo visual lavender, mascota PNG canónica con flotación idle, historial vacío y composer deshabilitado: «RoDo estará disponible en la Fase 15.» |

Git utiliza inspector/plan/commit/receipt/recovery/retry/push de Fase 13.
La navegación Git lee receipts locales y **no consulta el remoto automáticamente**.
El usuario solicita inspección local/remota. Solo resultados ProductionAssetResult
físicamente verificados de una publicación de esta sesión habilitan preparar commit.
El catálogo no reconstruye esa prueba. Plan antes de confirmar, commit y push separados.
Un Prepared durable permite revisión/retry explícito del Core; no hay ejecución al abrir la página.

Los journals conservan un único checkpoint. El stepper resalta exclusivamente ese
estado: no añade horas ni afirma que los pasos anteriores hayan sido observados.
Sin una asociación durable a los planes/paquete, la UI no reconstruye un pipeline
para reanudar jobs encontrados tras reiniciar. Los muestra fielmente para revisión.

## Scope, cancelación y confirmaciones

Cada llamada recibe UniverseContext explícito. Una generación y el token de lectura
impiden publicar resultados tardíos después de navegación, cancelación, cambio de
universo o dispose. Los resultados con universo incorrecto producen STOP. Al cambiar
scope se vacían datos, selección, actividad y evidencias de sesión.

Lecturas se pueden cancelar; catálogo/estadísticas propagan cancelación a Explorer.
Las mutaciones explícitas conservan el contexto capturado: navegación, selector y
contenidos editables se bloquean durante la operación. Cerrar la ventana se bloquea
mientras la operación está en curso; no se cancela una publicación física a mitad mediante el botón de lectura.

Publicar, restore, retención, commit, retry y push solicitan confirmación de un plan
concreto y su universo. Declinar no invoca el executor. Core revalida posteriormente
planes stale, seguridad de paths, ownership y bytes. No hay auto-repair, auto-overwrite,
auto-delete, auto-restore, auto-commit o auto-push. Hooks neutralizados, preflight de
attributes/filters, comprobación staged byte-exact y rollback del index siguen en Core.

Restore reabre el scope de lectura de Explorer después de la sustitución validada.
Caché regenerable de miniaturas es la excepción ya documentada de Fase 11; navegación
y smoke no modifican assets, Archive ni el catálogo.

## Configuración y ejecución

Desde la raíz del repositorio, en Windows:

```powershell
dotnet run --project .\src\NAP.App\NAP.App.csproj --configuration Release --no-build
```

Sin configuración aparece Ajustes, sin inventar rutas ni crear catálogo.
Los perfiles empaquetados se descubren a un solo nivel en `config/universes/<universe_id>/profile.json`;
Nimroel es el perfil incluido actual. Los importados se descubren en la raíz local explícita.
Settings se guardan fuera de Git mediante el store existente.
Puede indicarse un archivo local explícito con `--settings=<ruta absoluta>`.

La auditoría de pipeline usa el adapter de Fase 7 existente, únicamente si se
proporcionan **NAP_GEMINI_API_KEY** y **NAP_GEMINI_MODEL** en el entorno del proceso.
La clave no se persiste ni se muestra. Sin ambas variables se deshabilita Auditar y
Publicar nunca obtiene PASS. El presupuesto explícito de entrada es 64.000.000 píxeles;
Core sigue aplicando validación, conversión, integridad y sus límites físicos.

## Pruebas y revisión

`ProfessionalUiTests` cubre navegación/active state, todas las páginas, datos reales,
objetivos, ZIP→PLANNED→auditor→COMPLETED, ausencia de auditor, WARNING/FAIL, universos
cruzados, cancelación, resultados/fallos tardíos, dispose, settings, STOP sin repair,
confirmación/rechazo, restore con safety backup, retención, y commit/push separados
sobre repositorios Git temporales reales. No usa sleeps para coordinar acciones.

El smoke Windows abre WPF, recorre las diez páginas a **1200×700 y 2560×1600**
(20 visitas), exige cero errores de binding, mide la apertura de los frames, renderiza
miniaturas/ficha del catálogo real, comprueba ocultar/restaurar con NotifyIcon y
ejercita 30.000 elementos lógicos con reciclado y realizaciones acotadas.
La ventana nativa respeta el monitor/DPI; la medición adicional del contenido WPF
verifica los dos viewports lógicos exactos sin perder los bindings relativos a Window.

Para guardar PNG de fixtures reales, establecer NAP_UI_SMOKE_OUTPUT antes de los
tests Windows, o usar `--smoke-test --smoke-output=<directorio>` al arrancar con
settings de un fixture. Son diagnósticos explícitos, no datos de demostración del producto.

Validación reproducible:

```powershell
dotnet restore NAP.sln
dotnet build NAP.sln --configuration Release --no-restore
dotnet test NAP.sln --configuration Release --no-build
.\scripts\Test-MultiUniverseSchemas.ps1
git diff --check
git status --short
git rev-parse HEAD
```

**Pendiente:** segunda revisión física humana pantalla por pantalla antes de autorizar el commit.
Fase 15 reserva IA/RoDo conversacional, integración de contexto y comportamiento
proactivo; Fase 16 reserva instalador, icono propio y distribución; Fase 17 reserva
documentación técnica y handover. No se implementan aquí.

## Pasada final de UX sobre el RobStyle aprobado

La UI utiliza **Producción** en sidebar, pantalla, accesos rápidos y bandeja.
Los nombres internos Pipeline/Core se conservan. Base de datos (SQLite), Archivo
de maestros y recibo sustituyen las etiquetas humanas Database/Archive/receipt.
No se presentan enums Empty/Ready como mensajes humanos.

Ficha sin selección: «Sin selección» y su instrucción, sin scroll de una ficha vacía.
Sin raíces: «Catálogo no disponible» y Ajustes; un error real conserva su diagnóstico.
Objetivos, estadísticas, backups, checkpoints, recibos Git y actividad tienen mensajes
de vacío según evidencia. Solo una lectura válida sin registros afirma que no hay registros;
sin configuración, tras STOP o cancelación se muestra la causa. Métricas desconocidas: —.

UiAsyncCommand expone DisabledReason desde los requisitos reales de cada operación:
configuración ilegible, raíces, lectura, selección, campos, plan, auditor PASS, evidencia
física o estado del recibo. Bindings explícitos y ToolTipService.ShowOnDisabled hacen
que la explicación se vea también en botones deshabilitados. RoDo explica Fase 15 en
input y botón. No cambia el criterio de autorización del Core.

## Universe Profiles importables

Ruta productiva exacta:

```text
%LOCALAPPDATA%\NAP\universes\<universe_id>\profile.json
```

Es independiente del settings.json local y de las tres raíces de cada universo.
Los perfiles incluidos son read-only; el selector muestra una cápsula estática con uno
y un ComboBox real con dos o más. Ajustes → UNIVERSOS muestra nombre, ID, origen,
universo activo, incidencias y «Añadir perfil de universo…».

El diálogo autoriza leer un JSON concreto. Un worker valida un archivo normal local,
sin reparse/symlink en ningún componente, de **1 byte a 1 MiB**. El loader real acepta
las versiones v1–v4 existentes y rechaza campos desconocidos/duplicados, IDs no
canónicos y contratos inválidos. El JSON nunca proporciona rutas para la instalación.
También se rechazan IDs que sean nombres reservados de dispositivos Windows.

Descubrimiento no recursivo: solo las dos raíces explícitas y sus hijos canónicos,
máximo **128 entradas por raíz** y un límite de 128 perfiles para nuevas instalaciones. Orden: Nimroel primero,
resto por UniverseId Ordinal. Cada carpeta debe coincidir con el ID de su profile.json.
Los incluidos tienen precedencia; una colisión local se muestra como STOP y nunca los
sustituye. Un perfil local corrupto queda excluido con una incidencia visible, conservando
los demás. Si el inventario incluido no se puede validar completo, no se autoriza importar.

Import valida antes de crear almacenamiento. Rechaza cualquier ID instalado o carpeta
de destino existente y nunca actualiza/edita perfiles. Un mutex de proceso/host sobre
la raíz local coordina instalaciones simultáneas. Crea `.nap-import-<UUID>` dentro de
esa raíz, escribe con CreateNew/FileShare.None, Flush(true), reabre y compara bytes,
revalida límites y publica con Directory.Move al ID canónico, sin overwrite.
Se limpia exclusivamente el temporal propio; no se borran perfiles ni entradas ajenas.
Un temporal abandonado tras crash no se carga ni se elimina automáticamente: es visible.

La raíz local no puede estar en un checkout Git, seguir links ni solaparse en ninguna
dirección con WorkspaceRoot, ProductionRoot o ArchiveRoot de ningún universo.
El guard se usa tanto al importar como antes de guardar/activar raíces en Explorer.
Core sigue validando disponibilidad, independencia entre universos y settings corruptos;
estos permanecen visibles y no se sobrescriben. Verde significa validación física real
de las tres raíces actuales; editar cualquier ruta invalida ese estado hasta validar de nuevo.

Tras instalar se actualiza la colección y se selecciona el nuevo perfil en Ajustes.
No se inventan ni crean raíces/settings/catálogos: el usuario configura sus tres raíces.
El perfil persiste tras reinicio. Cambiar de universo conserva el aislamiento y vacía
datos/evidencias de sesión del universo anterior. No existe CurrentUniverse global.

Para smoke exclusivamente, `--profiles-root=<raíz absoluta de fixture>` y
`--smoke-import-profile=<JSON de fixture>` permiten comprobar esta misma frontera
sin escribir en el almacenamiento real del usuario. Fuera de --smoke-test no se aplican.

## RoDo de presentación

La imagen original del commit fijado se convirtió a PNG RGBA 1024×1024 y se comprobaron
dimensiones, transparencia y cada byte de los píxeles decodificados. El PNG se incluye
como Resource WPF, sin codecs WebP externos ni nuevos NuGet.
RodoMascot adapta su tamaño al espacio reservado de la cabecera, hasta 300×300 DIP, centrado en la cabecera de la página, fuera del panel de conversación.
La flotación usa TranslateTransform.Y, −3↔+3, 2.1 s por sentido, AutoReverse, Forever,
SineEase EaseInOut; Loaded/Unloaded/IsVisibleChanged activan o eliminan el clock.
No utiliza timers, llamadas IA, respuestas ni procesos. Solo queda un mensaje principal
de disponibilidad y el subtexto breve; input y envío permanecen deshabilitados.

## Inventario de archivos

13 archivos existentes modificados:

- Documentación: `README.md`, `CHANGELOG.md`, `docs/NAP_MASTER_SPEC.md`.
- App: `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `NAP.App.csproj`,
  `Themes/RobStyle.xaml`, `Controls/ThumbnailView.xaml.cs`, `VisualSmokeProbe.cs`.
- Presentation: `ExplorerViewModel.cs`, `ExplorerServices.cs`.
- Tests: `VisualExplorerWindowsSmokeTests.cs`.

42 archivos nuevos:

- `docs/PROFESSIONAL_UI.md`.
- App: `BindingDiagnostics.cs`, `ConfirmationDialog.cs`, `TrayHost.cs`,
  `Controls/RobTitledFrame.cs`, `Controls/ToneBrushConverter.cs`.
- App: `Controls/RodoMascot.cs`, `Assets/RoDo/rodo_v0_1.png`, `Assets/RoDo/PROVENANCE.md`.
- Themes: `RobStyle.Colors.xaml`, `RobStyle.Typography.xaml`, `RobStyle.Buttons.xaml`,
  `RobStyle.Controls.xaml`, `RobStyle.Templates.xaml`.
- Views, cada una con `.xaml` y `.xaml.cs`: `DashboardView`, `PipelineView`,
  `CatalogView`, `ObjectivesView`, `StatisticsView`, `BackupsView`, `HistoryView`,
  `GitView`, `SettingsView`, `RodoView` (20 archivos).
- Presentation: `ShellViewModel.cs`, `ProfessionalPages.cs`, `ProfessionalUiModels.cs`,
  `ProfessionalUiServices.cs`, `UiAsyncCommand.cs`.
- Presentation: `UniverseProfileStore.cs`.
- Tests: `ProfessionalUiTests.cs`, `UniverseProfileImportTests.cs`.

Los nombres App, Themes y Views están bajo `src/NAP.App`; Presentation bajo
`src/NAP.Presentation`; Tests bajo `tests/NAP.Tests`.

## Validación de la primera implementación (antes de la pasada final)

Validado en Windows, 8 de octubre de 2026, sobre el mismo HEAD de partida:

| Comprobación | Resultado |
| --- | --- |
| Baseline previo | 3221 passed, 0 failed, 0 skipped |
| Suite final Release | 3260 passed, 0 failed, 0 skipped; 4 min 47 s |
| Casos añadidos de Presentation | 39 |
| Smoke WPF existente reforzado | 2 tests, 20 visitas por recorrido, ambos viewports, 0 errores de binding |
| Tests focalizados tras finalizar controles RobStyle | 41 passed, 0 failed, 0 skipped |
| Restore | Correcto |
| Build Release | 0 warnings, 0 errors |
| JSON schemas | 436 checks passed |
| git diff --check | Sin errores |
| Whitespace de los 37 archivos nuevos | Sin errores |
| Runtime Core / AI / CLI, config, schemas y CI | Sin cambios |

Resultados locales ignorados por Git: `tests/NAP.Tests/TestResults/phase14-baseline.trx`,
`phase14-controls-final.trx` y `phase14-final-validation.trx`.
La comprobación de 30.000 elementos corresponde al panel virtualizado del smoke;
las miniaturas, documentos y métricas del recorrido proceden del catálogo real del fixture.

Estado de trabajo: 12 archivos modificados y 37 nuevos, ninguno staged.
Rama `feature/professional-ui-v1`; HEAD `8b32c76ea8a459cd842605c95a528a0acf6e3f91`.
Sin checkout, commit, push, merge ni PR en NAP. Los tests de Git trabajan únicamente
con repositorios temporales propios del fixture.

La primera revisión visual humana aprobó RobStyle. La segunda revisión física de la
pasada final sigue pendiente; estos resultados no cierran Fase 14.

## Validación final de UX, perfiles importables y RoDo

Validado en Windows el 8 de octubre de 2026, manteniendo la misma rama y HEAD:

| Comprobación | Resultado |
| --- | --- |
| Suite Release completa | **3299 passed, 0 failed, 0 skipped**, 4 min 53 s |
| Incremento de esta pasada | 39 casos: 38 de importación/UX/PNG y 1 smoke de segundo universo |
| Smoke WPF después de la suite | **3 passed, 0 failed, 0 skipped** |
| Recorridos WPF | Diez páginas × dos viewports por fixture; 1200×700 y 2560×1600 |
| Bindings, RoDo y controles | 0 errores; PNG visible y animación liberada al salir; motivos reales en botones deshabilitados |
| Selector e importación | Cápsula con uno; selector con dos; importación real sin crear raíces/settings |
| Catálogo y bandeja | 30.000 elementos, realizaciones entre 1 y 200; ocultar/restaurar correcto; procesos de smoke finalizados |
| Restore y build Release | Correctos; **0 warnings, 0 errors** |
| JSON schemas | **436 checks passed** |
| git diff --check y whitespace de archivos nuevos | Sin errores |
| Runtime Core / AI / CLI, config, schemas y CI | Sin cambios |
| Estado Git | 13 archivos modificados, 42 nuevos, ninguno staged |

Evidencia local ignorada por Git: `tests/NAP.Tests/TestResults/phase14-ux-full.trx`
y `phase14-ux-smoke-final.trx`. Se exportaron las veinte pantallas del fixture de
catálogo real para comprobar el render final; no se agregan al producto.

Se revisaron el diff y los archivos nuevos: UniverseContext explícito, sin rutas
privadas hardcoded, nuevos modelos/llamadas IA ni ejecución automática de Git,
restore o retención. La copia PNG y sus píxeles canónicos tienen verificación adicional.
Los cambios quedan únicamente en working tree. HEAD permanece en
`8b32c76ea8a459cd842605c95a528a0acf6e3f91`; no hubo checkout, commit, push, merge ni PR.
**Fase 14 sigue abierta hasta la segunda revisión física en el MSI.**
