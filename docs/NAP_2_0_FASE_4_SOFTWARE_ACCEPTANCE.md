# NAP 2.0 — aceptación de software de Fase 4 y despliegue separado

Fecha: 2026-10-16. Decisión de diseño aprobada por el responsable; preparada para revisión final en PR #46. No autoriza merge, instalación, migración ni producción real.

## 1. Dos puertas de aceptación

**Es viable aceptar formalmente la Fase 4 como implementación de software completada y validada, manteniendo pendiente la certificación operativa del MSI.** La aceptación final del código se somete a la revisión de PR #46; no se declara una aprobación de revisión todavía inexistente.

| Puerta | Evidencia / requisito | Estado |
| --- | --- | --- |
| Software de Fase 4 | P4.1–P4.4 y coordinación v2: autorización, ejecución por item, recuperación por evidencia y regresión sintética | Implementados y validados; listos para revisión final |
| Dependencia de Fase 5 | Baseline de Fases 1–4 revisada y disponible para integrar host/lifecycle | Sin bloqueo técnico identificado para continuar desarrollo y pruebas aisladas; pendiente revisión final de PR #46 |
| Despliegue operativo del MSI | Distribución e identidad protegidas, exclusión externa de escritores, raíces/padres seguros, preservación y pruebas de barrera | Pendiente; bloquea instalación productiva y activación real |
| Aceptación integral de Fase 7 | Fases 5–7 validadas y certificación operativa satisfecha antes de sustituir el escritor habitual | Pendiente; llegar a Fase 7 no concede autorización por sí solo |

El criterio original de Fase 4 en [Fase 0, sección 9](NAP_2_0_FASE_0_AUDITORIA.md#fase-4--ejecución-y-reconciliación-idempotente) exige un ZIP **sintético** hasta COMPLETED, recovery demostrado y ausencia de overwrite/repair. El [inicio de Fase 4](NAP_2_0_FASE_4_START.md) excluye habilitar universos reales. La dependencia «Fases 1–4 completas» de Fase 5 corresponde a los contratos y software aceptados; no exige intervenir en el MSI antes de desarrollar el host.

La separación no debilita [CUTOVER](NAP_2_0_FASE_4_CUTOVER.md): sus precondiciones siguen siendo obligatorias **antes de cualquier escritura de NAP 2.0 sobre raíces reales**, incluso si se propone un piloto anterior a Fase 7. No se promete exclusión frente a clientes antiguos sin la barrera externa. Las pruebas usan raíces sintéticas disjuntas de las reales y una única identidad por conjunto de raíces; no se usa el desarrollo como excepción de acceso productivo.

## 2. Base verificable de aceptación

- Código validado: `7fc39bd5ab4d6d60023716f27da90169d88acccd`, [Actions 38774561085](https://github.com/robdor80/NAP/actions/runs/38774561085), cinco jobs aprobados.
- Windows completo: 3.716 aprobadas, cero fallos/omisiones. Linux: 3.712 aprobadas, cero fallos y cuatro WPF omitidas; WPF Windows independiente: 4/4.
- P4.1–P4.4: 80/80 por plataforma; exclusión histórica: 4/4; procesos/runtimes: 19/19; RecoveryPush: 29/29. Las tandas se solapan y no se suman.
- Cabeza previa a esta decisión: `ed32a301721777a4b88a466327cb392abbb82082`, [Actions 38777914482](https://github.com/robdor80/NAP/actions/runs/38777914482), cinco jobs aprobados. Desde `7fc39bd` hasta esa cabeza solo cambiaron documentos y evidencia.
- La [evidencia permanente](NAP_2_0_FASE_4_COORDINATION_V2_EVIDENCE.json) conserva hashes, versiones, contadores y `RealHostCutoverApplied=false` / `RealStartupPolicyAttested=false`. Esta decisión no cambia esos valores ni atribuye resultados de CI a una revisión que no los haya ejecutado.

El [informe de ejecución](NAP_2_0_FASE_4_EXECUTION_RECOVERY.md) conserva fallos anteriores y límites: symlinks locales sin privilegio, pruebas de muerte de owner que no certifican pérdida eléctrica, ausencia de exactly-once remoto y rendimiento sintético distinto del productivo. CI Windows ejecutó la regresión completa sin omitir los casos de symlink. Los avisos existentes de ImageSharp requieren valoración separada antes de desplegar; no se ocultan ni se resuelven mediante este cierre documental.

No se identifica una dependencia técnica ausente que obligue a completar el corte del MSI para empezar Fase 5 en aislamiento. Un defecto de seguridad o corrección descubierto en revisión sí bloquearía la aceptación del software y tendría que resolverse antes de integrar esa base. El cierre documental no anticipa el resultado de la revisión ni autoriza por sí solo implementar la siguiente fase.

## 3. Producto definitivo y conservación

NAP 2.0 evoluciona NAP en el **mismo repositorio**, reutilizando App, Presentation, Core y AI. El objetivo final es un único producto y una única instalación operativa; los componentes internos, CLI y herramientas de prueba no se convierten en productos independientes.

Se conservarán preparación/validación de ZIP, normalización y conversión, auditoría IA, operación manual y publicación, archivo, catálogo/explorador, perfiles multiuniverso, objetivos/estadísticas, journals/recuperación, backups/restauración/retención y Git explícito. Fases 5–7 deben comprobar equivalencia funcional y evitar pipelines manual/automático divergentes. No se atribuye al software actual la integración del host futuro.

Dirección aprobada: distribución publicada independiente y protegida, fuera del repositorio y worktrees. `C:\Program Files\NAP` es una ubicación propuesta, no una instalación creada o aprobada en detalle. Deben protegerse ejecutable, todas sus DLL, perfiles publicados, configuración de arranque y actualización; no basta proteger el lanzador o permitir cualquier DLL mediante dotnet. Elegir runtime y dependencias verificados formará parte del paquete final.

Las aplicaciones de los worktrees `nimroel-cartography` / `safe-image-normalization` y las distribuciones de desarrollo dejarán de ser puntos de arranque operativos solo tras aceptar la sustituta. No se borran fuentes, worktrees, Debug/Release necesarios para desarrollo, fixtures, peers, CoordinationProbe ni evidencias baseline/aisladas. No se instala el utillaje de pruebas como parte del runtime productivo. Una eventual limpieza de salidas regenerables necesita evaluación y autorización separadas. Conservar o reconstruir binarios antiguos obliga a imponer la barrera de acceso; borrarlos no la sustituye.

## 4. Frontera de datos: preferencia, sin migración aprobada

La auditoría preventiva del MSI realizada en la conversación de 2026-10-16 confirmó estas raíces de Nimroel, que permanecen intactas:

| Raíz | Ruta actual |
| --- | --- |
| WorkspaceRoot | `C:\Users\andro\Documents\NAP\Nimroel` |
| ArchiveRoot | `C:\Users\andro\Documents\Nimroel_Maestros_PNG` |
| ProductionRoot | `C:\Users\andro\Documents\GitHub\Videojuego_Nimroel\Worldbuilding\Direccion artistica\Assets` |
| StateRoot, derivada | `C:\Users\andro\Documents\NAP\Nimroel\state` |

La cuenta habitual conserva control total sobre datos y padres; los binarios de desarrollo permiten modificación a CodexSandboxUsers. La auditoría no acreditó exclusión de handles, denegación desde otras cuentas ni una política de arranque exclusiva. Identificó además leases no vacíos y permisos del padre ProgramData que necesitan revisión. Son pendientes operativos, no evidencia de una barrera aplicada.

Se estudiarán preferentemente raíces independientes bajo un padre protegido, conservando configuración, assets, PNG maestros, catálogo, journals, recibos y proyectos. **No hay aprobación para mover, copiar, sustituir o reconfigurar datos reales.** Tampoco se aprueban cambios generales de permisos sobre Documents, GitHub, carpetas de Codex o proyectos Godot.

Mantener las rutas actuales no acredita el contrato mientras la identidad anterior controle padres capaces de sustituirlas. No se promete resolverlo con ACL solo en la hoja, un marker, un mutex, la retirada de accesos directos o junctions. Una política amplia de bloqueo de ejecutables no se da por aprobada y podría afectar a las herramientas de desarrollo.

La alternativa de nuevas raíces requerirá un contrato de migración aprobado que complemente o sustituya expresamente el corte actual **sin movimiento de datos**. Debe tratar bindings por ruta, autorizaciones, evidencia, trabajos existentes y relación con Godot/Git/sincronizadores; no basta copiar una DB o reescribir rutas en journals. No adoptar automáticamente finales ambiguos ni reabrir COMPLETED. Una eventual exportación para Godot no será un segundo estado productivo ni una vía de escritura sin control.

## 5. Decisiones que debe resolver Fase 5

| Decisión / requisito | Resolución exigida antes de cerrar el diseño del host |
| --- | --- |
| Composición | AutomationHost, scheduler y lifecycle; un worker productivo global inicial, ámbitos inmutables y fairness entre universos; sin invocar commands de Shell como pipeline |
| Identidad | Una identidad de escritor por conjunto de raíces; decidir identidad no interactiva y arranque administrado de mínimo privilegio, sin credenciales accesibles a usuarios antiguos |
| UI y host | Evaluar host en App frente a componente interno separado. La propuesta histórica en App no prueba que una sesión interactiva ordinaria cumpla CUTOVER; cualquier separación requiere revisión de arquitectura |
| Canal interno, si hay separación | Autenticación/autorización local y operaciones tipadas; impedir solicitudes de DLL, comandos o rutas arbitrarias; revisar implicaciones de WPF, cierre y reinicio |
| Todos los participantes | Escrituras manuales, automáticas, objetivos, backups, restauración, retención y Git deben respetar la misma frontera; lectores que toman leases bajo la misma identidad o a través de su servicio. Inventariar también escrituras de caché |
| Coordinación | Ownership thread-affine, orden de locks y CAS de Fase 4; no mantener transacciones DB durante coordinación/HTTP; no adquirir otro Job lease alrededor del coordinador |
| Lifecycle | Inicio inerte sin configuración/autorización; pausa durable, drenaje, cierre, retry acotado y reapertura sin duplicar efectos; navegar/cerrar una vista no abandona al ejecutor |
| Almacenamiento | Resolver perfil/configuración por identidad, ubicación y protección de coordinación/padres y compatibilidad con futuras raíces. No cambiar ProgramData global ni mover leases en caliente como atajo |
| Pruebas | Universos sintéticos disjuntos, dos instancias, lotes multiuniverso, pausa/restart, aislamiento de incidencias, memoria/espacio y preservación; sin proveedor ni credenciales reales |

El host no debe introducir un modo productivo de desarrollo que eluda estos requisitos. Su implementación y ensayos aislados pueden avanzar antes de crear la identidad o instalar el host real. La selección de arquitectura no autoriza crear cuentas, servicios o tareas en el MSI.

## 6. Puertas restantes y reversibilidad

1. **Fase 4:** revisión final del software y de esta separación. PR #46 sin merge hasta aprobación expresa; no activar auto-merge.
2. **Fase 5:** host/lifecycle e identidad diseñados y probados en aislamiento; resolver las decisiones anteriores sin instalar componentes reales.
3. **Fase 6:** UX de automatización e incidencias y equivalencia con la aplicación manual; ninguna vista imprescindible para sostener el worker.
4. **Fase 7:** Git configurable, aceptación integral y paquete definitivo. La instalación productiva exige también aprobar/verificar el procedimiento operativo y cualquier migración; el número de fase no levanta la barrera.
5. **Corte posterior autorizado:** backup externo e inventarios verificados, exclusión de escritores/handles antiguos, identidad/arranque y rutas/padres protegidos, pruebas de denegación y preservación, y autorización productiva separada.

Conservar distribuciones y configuración para recuperación no concede rollback en caliente a un escritor antiguo. Ante fallo, detener de forma controlada el nuevo arranque y mantener los datos protegidos; cualquier rollback repite exclusión y validación, sin restaurar indiscriminadamente assets, catálogo o journals. La preparación y la futura operación requieren autorizaciones separadas de esta decisión.

**Estado operativo vigente: certificación del MSI pendiente; ninguna intervención Windows ni cambio en activos reales autorizado por este documento.**
