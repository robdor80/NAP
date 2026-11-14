# NAP09-P02 — Decisión de arquitectura para la puesta en marcha real

**Fecha:** 2026-11-14. **Estado:** DISEÑO PROPUESTO / no ejecutado.
**Repositorio:** `robdor80/NAP`; PR NAP09 #73 Draft.
**Entrada:** informe verbal de `NAP09.ReadOnlyPreflight.ps1` (02:27) y contratos ya integrados.
**Objetivo:** pasar del catálogo histórico visible (39 WebP + 39 evidencias PNG) a un Host productivo seguro, sin confundir esta planificación con una autorización de CUTOVER.

## 1. Hechos que gobiernan la decisión

- Windows 11 MSI / NAP 2.0.2 instalado. Catálogos 3/3, binarios 2/2 (existencia).
- Un proceso NAP sin identificar en el resumen; cero tareas/servicios con nombre coincidente, inventario no exhaustivo.
- Coordinación del SID interactivo: **44.983 ficheros *.lease**, **16 no vacíos**, aviso 1. La cifra **no demuestra** corrupción, actividad ni propiedad de esos leases. No borrarlos/editar su contenido.
- Originales de retratos y catálogo anterior se han conservado. Importación WebP histórica y vinculación de 39 PNG acreditadas por interfaz. No hay necesidad de importarlos de nuevo.
- `src/NAP.AutomationHost/Program.cs` exige literalmente `--laboratory-config`, declara que no existe modo producción/servicio y configura `LaboratoryRoot` y un `desktop-requests` bajo ese laboratorio. `ManagedAutomationHost.cs` y NAP02 conservan el límite TEMP; **no es seguro activar el Host real simplemente apuntando launch.json a Documents/GitHub**.
- Contrato `docs/NAP_2_0_FASE_4_CUTOVER.md`: identidad escritora única protegida, exclusión efectiva entre usuarios/versiones, ACL también en padres y archivos, binarios inmutables para escritores antiguos, backup externo restaurable y parada en frío. Incidencia #47 sigue abierta. NAP05 no cerró #49.

## 2. Decisión recomendada (aún sin autorización de ejecución)

**Separar la zona de trabajo normal de desarrollo de la zona de escritura controlada**. No modificar a ciegas las ACL de `C:\Users\andro\Documents` ni de `Documents\GitHub` — ambos usados por Godot, Git y herramientas de desarrollo.

La distribución NAP y sus configuración/secretos de arranque deberán residir bajo una frontera protegida que la cuenta de desarrollo no pueda modificar. El Host real debe estar bajo **una sola identidad escritora de mínimo privilegio** y su proceso/arranque debe tener revisión explícita. Las raíces de procesamiento deben ser inaccesibles para escritores antiguos. No basta un mutex ni las bases SQLite.

**Punto por resolver antes de crear rutas:** NAP publica activos sobre el checkout Nimroel actual; separar su ProductionRoot exige un contrato de entrega al repositorio (sin volver a poner dos escritores sobre la misma raíz) y continuidad de Git/Godot. Hay dos alternativas a comparar:

A. Conservar raíces existentes, con ACL/ownership de toda la cadena (incluyendo padres y enlaces), restricciones a escritores anteriores y pruebas de compatibilidad con Git/Godot. Riesgo elevado por compartir `Documents/GitHub` con desarrollo normal.

B. **Preferida para estudiar:** nueva raíz de producción aislada, con contrato explícito de publicación/entrega a checkout y migración controlada de configuración, referencias, catálogos, journals y recibos; el checkout no pasa a ser una segunda raíz escritora de Host. Requiere estudio de integración real, no un `robocopy` improvisado.

Ninguna opción está aprobada todavía. El plan debe devolver al propietario una propuesta concreta de rutas, identidad, permisos efectivos y reversión **antes** de afectar el MSI.

## 3. Orden técnico mínimo y responsable

1. **Arquitectura / ChatGPT:** inspeccionar Host, configuración WPF, `UniverseContext`, edición de jobs, Git publication y contratos; especificar modo productivo explícito, independiente del modo sintético, con `SyntheticAudit=false` y rechazo de configuraciones ambiguas. No habilitar por accidente una `--laboratory-config` para rutas reales.
2. **Coordinación / ChatGPT:** análisis por ámbito de `*.lease` usando únicamente lectura. El recuento total incluye históricos persistentes; no inferir 44.983 ejecuciones activas. Priorizar correlación de nombres de scope/raíz/identidad y estado de archivo, sin contenido sensible ni limpieza.
3. **Protección / ChatGPT + aceptación propietario:** diseñar distribución y configuración protegidas, identidad mínima y bloqueo externo verificable contra versiones antiguas; pruebas exclusivas en Windows sintético.
4. **Datos / ChatGPT + propietario:** respaldo externo con prueba real de restauración, delimitando SQLite, WAL/SHM si existen, journals, maestro, PNG/WebP, recibos, claves privadas sin exponerlas y manifest SHA. Sin reabrir ni alterar COMPLETED.
5. **Corte / propietario guiado:** inventario final de procesos y mecanismos de arranque, parada/drenaje en frío, aplicación de la arquitectura autorizada, verificación de que identidades antiguas no pueden escribir, y primera activación controlada. Autorización independiente y explícita obligatoria.

## 4. Condiciones de salida y límites

- P02 se considera cerrado cuando hay **contrato concreto, verificable** para Host productivo y frontera de escritor, con compatibilidad de Nimroel/Star Trek, más un procedimiento de migración/reversión revisable.
- El corte operativo se mantiene **BLOQUEADO**. No generar `launch.json` productivo ni convertir la ruta TEMP en ruta real sin contrato.
- No limpiar leases, no elevar PowerShell, no hacer desinstalaciones, no cambiar ACL ni usuarios, no activar NAP CI general/ZIP masivos, y no repetir importaciones ni capturas de los 39 retratos.
- Esta PR documenta; **no pretende certificar** que el equipo haya satisfecho las garantías de #47.


## 5. Hallazgo de código adicional: IPC entre identidades

La lectura directa de `src/NAP.Core/AutomationProtocol.cs` confirma que **cliente y servidor** construyen las tuberías con `PipeOptions.CurrentUserOnly`. Esto impide que la UI interactiva se conecte sin más a un escritor bajo una identidad Windows separada, aunque se conozca la clave HMAC. No basta cambiar `--laboratory-config` por `--production-config` ni levantar el límite TEMP.

Además, `src/NAP.Presentation/AutomationDesktopBridge.cs` recibe el principal SID de la tubería y lo incluye en deduplicación/datos de revisión; al crear un canal entre identidades debe seguir verificando autorización de **cada** principal y operación, sin exponer secretos, conceder permisos a Everyone ni aceptar clientes arbitrarios.

**Decisión de implementación P02/P03 (sin ejecutar CUTOVER):**

- Diseñar un canal Windows de producción **distinto** del canal de laboratorio `CurrentUserOnly`: ACL explícita para SID escritor y cliente autorizado, autenticación mutua HMAC existente, vinculación de sesión/instalación y controles de permisos por operación/principal. Nunca abrir una tubería pública solo porque se conoce un secreto.
- Mantener invariado el canal de laboratorio, para conservar CI y NAP08.
- Probar en cuentas/raíces **sintéticas** que un SID no autorizado es rechazado, los tokens falsos no permiten acciones y no hay doble escritor. La prueba de autenticación no reemplaza prueba de ACL/ownership del MSI.
- Solo tras esa prueba conectar `ManagedAutomationHost` con un modo productivo protegido, sin arranque como usuario de desarrollo, sin fallback local y sin admitir modo laboratorio hacia raíces reales.

## 6. Trabajo de código P03 completado en la rama

- `scripts/NAP09.CoordinationInventory.ps1`: lectura no destructiva de leases persistentes, agrupación por scope, conteo de vacíos, clasificación de 16 no vacíos y anomalías (sin intentar tomar/borrar locks).
- `scripts/NAP09.WriterInventory.ps1`: inventario no destructivo de procesos, tareas programadas por acción/nombre, servicios y cadena de padres/ACL; no exporta argumentos de línea de órdenes ni secretos.
- `tests/nap09/Test-CoordinationInventory.ps1`: fixture TEMP con cuatro archivos, tres ámbitos, dos no vacíos y una denominación inválida, verificando que ninguno cambia.
- CI independiente `.github/workflows/nap09-coordination-inventory.yml` en Windows Server 2022 con PowerShell 5.1, sin NAP CI general, producción ni licencia externa.
- PR #73 continúa en Draft; scripts **no instalados ni ejecutados** en MSI desde la nueva rama. En particular, el resumen de 44.983/16 procede del diagnóstico anterior del propietario y no de esta nueva herramienta.

Nota: el inventario no demuestra permisos efectivos, exclusión completa de escritores, ni que un lock no vacío esté ocupado. Mantener #47 abierta. Los 39 retratos y sus PNG no requieren más pruebas de aceptación.

**Referencia API Windows:** Microsoft documenta que `NamedPipeServerStreamAcl.Create` admite `PipeSecurity` explícito, pero si se conserva `CurrentUserOnly` se ignora ese objeto y la tubería se limita a la identidad actual: https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstreamacl.create?view=net-10.0. Una adaptación futura tendrá que retirar el flag **solo en el modo con ACL/SID explícitos**, no en el laboratorio.


## 7. Implementación P02 (transporte opt-in sin activar producción)

Código incorporado en la PR #73, todavía sin despliegue MSI:

- `src/NAP.Core/AutomationWindowsEndpointAccess.cs`: política Windows explícita, SID de escritor y SID de interfaz distintos, rechazo de grupos genéricos; solo el escritor correcto puede construir la ACL. ACL protegida de tubería para dos usuarios identificados, no para Everyone.
- `src/NAP.Core/AutomationProtocol.cs`: cliente/servidor aceptan opcionalmente esta política. Sin política mantienen **exactamente** el canal de laboratorio `CurrentUserOnly`; con política, servidor usa `NamedPipeServerStreamAcl.Create` y exige SID exacto de cada cliente antes de enviar la solicitud al puente WPF. HMAC de autenticación existente permanece activo.
- `tests/NAP.Tests/Nap09ProtectedIpcTests.cs`: rechaza SID de grupo o SID idéntico, rechazo de escritor distinto y ensayo de ida/vuelta de canal protegido con la identidad real del runner como escritor y SID sintético adicional.
- `.github/workflows/nap09-protected-ipc-windows.yml`: compilación completa Windows 2022/.NET 10 y batería focalizada de IPC/Host, con licencia privada Six Labors y sin NAP CI general.

**Límites:** el transporte opt-in aún **no se conecta** a `NAP.AutomationHost` ni a `ManagedAutomationHost` de producción. Un test con un único usuario y un SID inventado no acredita conexión real entre dos cuentas, Windows ACL efectiva del MSI ni instalación de servicio. No existe `--production-config` utilizable y no se autorizan escrituras reales. La siguiente integración debe acreditar dos identidades distintas en un laboratorio desechable autorizado y protección de configuración/secretos antes de habilitar un arranque productivo.

## 8. Implementación posterior: Host productivo condicionado (rama #73)

**Código nuevo sin ejecución en el MSI:**

- `AutomationProductionLaunchConfiguration` define de manera independiente el arranque `--production-config`. No acepta inyección de auditor sintético ni las raíces temporales ordinarias como producción.
- `AutomationProductionBoundary.Validate` (antes de construir el Host) exige identidad de Windows escritora exacta y diferente del usuario de la interfaz, instalación protegida bajo `Program Files`, raíz de datos exacta bajo `ProgramData/NAP-Production-v1`, config protegida, raíces separadas y sin enlaces, DACL y propietarios auditables en datos, instalación y padres, backup con recibo SHA-256 en otro volumen e **inventario completo de hashes de binarios** compatible con `Build-Nap06Release.ps1`. Toda divergencia: STOP, sin arranque del proceso de trabajo.
- `AutomationHostOptions.ProductionBoundary` es un testigo interno del Core: una instancia del Host con raíces no TEMP carece de autorización sin el testigo; el modo laboratorio existente conserva la restricción TEMP.
- `NAP.AutomationHost/Program.cs` distingue `--laboratory-config` y `--production-config`. Ambos terminan usando los servicios durables existentes, no una segunda fábrica de procesamiento. El modo productivo nunca recurre a `SyntheticAuditor` y mantiene auditoría Gemini solo si está configurada/autorizada.
- `App.xaml.cs` y `ManagedAutomationHost.cs` hacen que un binding WPF protegido sea **attach-only**, también en arranque normal. Ningún cierre/arranque de WPF crea un escritor productivo bajo el usuario interactivo.
- `Nap09ProductionBoundaryTests` verifica rechazo de identidad inadecuada, raíz TEMP y configuración inválida sin crear registros Host. `Nap09ProtectedIpcTests` cubre rechazo de SID falso, canal protegido e intercambio del contrato de enlace. CI: workflow específico Windows .NET 10 con pruebas focalizadas, sin probar cuentas/ACL de la máquina del propietario.

**Limitaciones que no se dan por certificadas:**

1. Una coincidencia SHA del *recibo* de backup no prueba que el respaldo haya sido restaurado fuera del equipo. Hace falta verificación real independiente.
2. La prueba de denegación de dos cuentas Windows distintas (writer + interfaz) todavía no se ha realizado. Una prueba sintética con SID inventado no certifica IPC multiusuario real.
3. El instalador actual NAP 2.0.2 es de perfil; no existe aún la provisión revisada de distribución en `Program Files`, cuenta escritora, permisos, configuración administrativa o arranque administrado. **No ejecutar el comando productivo en el MSI hasta nueva aprobación.**
4. El protocolo de publicación Git/Godot desde raíces aisladas, límites de identidades antiguas y denegación de sustitución en padres sigue pendiente de certificación operativa; el gate de ACL es una defensa adicional, no sustituye #47.
5. No hay primera escritura de producción, ni reconfiguración de NAP en el MSI, ni migración de los 39 retratos. La PR permanece Draft y no está fusionada.

**Orden siguiente:** cerrar pruebas focalizadas y revisión de seguridad, preparar paquete/provisión administrativa sin afectar Documents/GitHub, generar configuración emparejada Host/WPF + inventarios en un escenario sintético protegido, certificar respaldos y cuenta escritora en frío; solo después solicitar autorización explícita de CUTOVER al propietario.
