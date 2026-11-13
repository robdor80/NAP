# NAP 2.0 — Plan maestro reconfigurado: máximo trabajo directo de ChatGPT y cuota Codex protegida

**Fecha:** 13 de noviembre de 2026  
**Repositorio:** `robdor80/NAP`  
**Plataforma:** Windows 11 x64, WPF, C#/.NET 10 LTS  
**Decisión del propietario:** NAP será una aplicación Windows instalable (`.exe`) y actualizable; **no hay presión de calendario**. La restricción relevante es **el consumo de cuota de Codex**, no el tiempo o esfuerzo de ChatGPT.

## 1. Decisión y prelación

Este documento **sustituye como hoja de ruta futura** la tabla NAP01–NAP06 de `docs/NAP01_CONSOLIDACION_REPOSITORIO_20261113.md` y la ampliación verbal NAP01–NAP07. No modifica retroactivamente los informes de ejecución, contratos históricos ni las decisiones de seguridad.

1. ChatGPT es **ejecutor principal**, no mero planificador. Realiza por sí mismo auditoría, diseño, cambios en GitHub, pruebas Windows mediante Actions focalizadas, informes, PR y fusiones autorizadas cuando las herramientas lo permiten.
2. **Codex no se consume por rutina ni por preferencia de rapidez**. Solo se recomendará ante un bloqueo técnico concreto, reproducible, imprescindible y no resoluble por ChatGPT con GitHub/Actions. Antes del encargo, entregar una justificación, alcance mínimo y prompt con modelo/nivel; será el propietario quien decida cuándo reservar cuota.
3. La comprobación física de Windows 11, WPF, instalador y conducta real del MSI corresponde al propietario, guiado por ChatGPT; **no se confunde con necesidad de Codex**.
4. Evitar duplicar el motor existente: se inspecciona cada función antes de crear una nueva. No reabrir bloques completos ya terminados; corregir únicamente déficits probados. Calidad significativa sí; burocracia, refactorizaciones gratuitas y pruebas masivas repetidas no.
5. No usar `NAP CI` general, suites masivas ni nuevos ensayos de volumen ZIP sin una autorización de alcance separada. Preferir workflows específicos Windows, datos sintéticos y conjuntos focalizados. Conservar la evidencia previa.
6. Las PR se crean sobre `main` en ramas propias. Una fusión técnica no equivale a autorizar instalación productiva, permisos, servicio privilegiado, migración de datos, Git productivo o CUTOVER.

## 2. Punto de partida verificado

- **NAP01 — Consolidación:** finalizada y fusionada mediante PR #63 (historia conservada).
- **NAP01-B — .NET 10 LTS:** finalizada y fusionada mediante [PR #64](https://github.com/robdor80/NAP/pull/64). `main@3401ad79c9ab64718cf543014d5420b79a608a81` al redactar. Once proyectos migrados, SDK mínimo 10.0.100, SQLite 10.0.15; compilación completa y tres herramientas auxiliares + **100/100 tests focalizados PASS** en GitHub Actions Windows Server 2022. **No es certificación física del MSI**. Informe: `docs/NAP01B_B04_AUDITORIA_CIERRE_NET10_20261113.md`.
- **PR #62 — Auditor de diversidad IA:** abierta **Draft**, sin fusionar. Sus pruebas y compilación históricas usan el contexto previo a la migración; requiere comprobar compatibilidad .NET 10 y aceptación visual de la nueva pantalla antes de decidir fusión. No rebasar o fusionar automáticamente.
- **Incidencia #49:** abierta. IPC tras cancelación, StorageUnavailable y avisos ImageSharp; no declarar corregidos defectos sin reproducción/validación.
- **Incidencia #47:** abierta. Puerta obligatoria para permisos, identidad escritora, instalación operativa y producción en rutas reales. La nueva autorización de desarrollar un instalador/actualizador **no levanta** por sí sola esa puerta.
- Existe `NAP.App.exe` como salida de compilación WPF; `installer/` contiene únicamente `.gitkeep`. Aún no hay instalador final ni sistema de actualización comprobado.
- En la migración quedaron scripts/workflows históricos asociados a `net8.0`; conservar su carácter histórico y **adaptar únicamente los reutilizados** antes de construir el release .NET 10.
- `ImageSharp 3.2.0` y la licencia privada Six Labors siguen su circuito existente; nunca publicar secretos, licencia ni claves Gemini.

## 3. Hoja de ruta aprobada

Las fases conservan los nombres funcionales NAP02–NAP07 previamente acordados. Se incorpora todo lo aprovechable de C01–C11 que puede ejecutar ChatGPT; se añaden NAP08–NAP09 como **puertas físicas y operativas**, no como excusa para consumir Codex.

### NAP01 — Consolidación (HECHA)
Repositorio único, historial reconciliado y ramas/PR clasificadas. No deshacer.

### NAP01-B — Migración a .NET 10 LTS (HECHA, validación física diferida)
Conversión de toda la solución y herramientas, workflow limitado y cierre técnico. No repetir. Comprobar SDK/runtime e inicio WPF en MSI durante NAP08.

### NAP02 — Host operativo, autoridad y controles WPF (CHATGPT)
- Auditar `NAP.AutomationHost`, IPC, cola durable, scheduler, límites de identidad escritora y flujos manuales antes de añadir código.
- Conectar la UI WPF normal con Host real aislado; iniciar, pausar, reanudar, cancelar/drenar y mostrar estado sin dobles procesos o ejecuciones.
- Mantener cierre seguro, exclusión mutua, separación de universos y permisos mínimos. No instalar un servicio privilegiado ni intervenir en raíces productivas.
- Pruebas de coordinación/cancelación/reinicio con datos sintéticos y Windows Actions focalizadas.
- **Salida:** app + host compuesto y trazable en entorno seguro, PR y evidencia.

### NAP03 — Flujo completo de assets y compatibilidad existente (CHATGPT)
- Confirmar y reparar el trayecto **Inbox mezclada de Nimroel y Star Trek → identificación `UniverseId+AssetId` → admisión → validación/plan → Gemini solo cuando esté configurado y autorizado → conversión WebP → archivo/publicación local → catálogo SQLite**; no crear implementaciones paralelas de etapas ya presentes.
- Un control de «procesar pendientes», progreso por lote, revisión de casos excepcionales y reanudación sin duplicar.
- Verificar importaciones antiguas, `LegacyPortraits.db`, evidencia separada de PNG maestros históricos y compatibilidad de NAP 1.0 **sin convertir históricos en publicaciones ficticias**. Corregir déficits reales de C02; si ya funciona, darlo por cubierto.
- Ensayos sintéticos por universo, con modo sin Gemini, sin Git real ni rutas de producción; no repetir suites de estrés.
- **Salida:** cadena extremo a extremo demostrada, con compatibilidad de catálogo/legado documentada.

### NAP04 — RobStyle, auditor de diversidad y laboratorio visual (CHATGPT + usuario para observación MSI)
- UI limpia y minimizable, trabajos pendientes/ejecutando/revisión/duplicado/completados, errores comprensibles, controles coherentes y sin ruido visual.
- Revisar `PR #62` como integración aislada: datos SQLite como fuente de verdad, agregación segura, mínimo de datos a Gemini, compatibilidad .NET 10; hacer pruebas WPF automatizadas y **validación física de la pantalla con el propietario** antes de fusionar.
- Recuperar el valor de C04 (diversidad) y C05 (laboratorio visual) sin mantener un laboratorio permanente que no aporte al producto.
- **Salida:** UI utilizable, decisión motivada sobre PR #62 y evidencia visual; no rediseño global gratuito.

### NAP05 — Estabilidad, recuperación, regresiones y seguridad (CHATGPT)
- Atender C03 y C06 de modo focalizado: incidentes reproducibles de #49, IPC/cancelación, StorageUnavailable, reinicios, bloqueo de trabajos, idempotencia, duplicados, recuperación sin sobrescritura.
- Comprobar integridad de backups/catálogos, sidecars, journals, hashes y separación por universo; simular fallos, restauraciones y colisiones **solo en directorios de laboratorio**.
- Evaluar advisories ImageSharp sin ocultarlos ni inventar una remediación; vigilar defectos que bloqueen seguridad.
- Priorizar regresiones específicas por cambio, evidencia y veredicto; sin grandes baterías de ZIP nuevas por defecto.
- **Salida:** recuperación y no regresión sustentadas, issue #49 actualizada según evidencia, sin cierre automático.

### NAP06 — Publicación Windows .exe e instalador profesional (CHATGPT + pruebas MSI)
- Definir distribución `win-x64` autocontenida o con prerrequisitos explícitos (elegir según evidencia), dependencias/licencia, configuración y persistencia.
- Construir un instalador tradicional `NAP-Setup-<version>.exe` mediante herramienta adecuada (estudiar Inno Setup/WiX y precedentes existentes antes de decidir).
- Inicio, icono, accesos directos, desinstalación no destructiva y separación estricta entre binarios reemplazables y datos de usuario, maestros, catálogos, journals y backups.
- Empaquetado determinista/reproducible cuando sea viable; checksums SHA-256, provenance, SBOM o inventario de componentes y workflow específico Windows x64. **Código de firma** si existe certificado válido; sin él no fingir editor verificado ni ausencia de avisos SmartScreen.
- Migrar/adaptar para .NET 10 únicamente scripts históricos necesarios; no activar `NAP CI` general ni modificar Windows del propietario desde Actions.
- **Salida:** artefacto instalable de laboratorio, documentado, con pruebas de empaquetado y autorización de instalación física aún separada.

### NAP07 — Actualizaciones verificadas y reversión (CHATGPT + pruebas MSI)
- Consultar versiones de un canal explícito de GitHub Releases, de forma configurable y con intervención del usuario antes de aplicar una actualización.
- Descargar a staging, validar procedencia/autenticidad e integridad; nunca ejecutar artefactos arbitrarios según metadatos no confiables.
- Coordinar parada/drenaje del Host y cierre de WPF, actualizar solo binarios, reabrir, verificar versión y permitir recuperación/rollback controlado sin perder datos ni rebajar schemas silenciosamente.
- Pruebas en sandbox de versión N→N+1, descarga alterada, fallo a mitad, disco lleno, rollback y reanudación; no publicar releases productivas automáticamente.
- **Salida:** mecanismo de actualización y guía probados en entorno aislado.

### NAP08 — Certificación física Windows 11 / MSI (CHATGPT prepara; propietario ejecuta)
- Verificar instalación limpia, actualización, desinstalación conservadora y reinstalación, firma/SmartScreen, arranque WPF, tray/ventana, Host, permisos y configuración sin tocar todavía el repositorio productivo.
- Ejecutar lista corta de casos sintéticos, contraste visual y recuperación; recopilar logs/reportes del MSI sin claves o datos sensibles.
- Detectar y corregir con ChatGPT lo que pueda corregirse remotamente; **Codex solo ante bloqueo imprescindible probado**.
- **Salida:** acta de aceptación física y fallos pendientes; NAP08 no certifica CUTOVER.

### NAP09 — CUTOVER y puesta en marcha real (CHATGPT prepara; propietario decide y ejecuta en MSI)
- Realizar inventario de escritores antiguos, tareas/procesos, leases (incluidos históricos), rutas/ACL de padres, protección de ejecutables, backups externos restaurables y plan de reversión.
- Determinar frontera de datos y permisos de manera explícita, sin alterar `Documents/GitHub` ni cambiar raíces por inferencia, enlaces/junctions o atajos.
- Procedimiento separado y validación en frío; **exige autorización operativa nueva del propietario** para instalación productiva, permisos, identidades y primera escritura real. No cerrar #47 hasta certificar evidencia.
- **Salida:** único NAP en producción con intervención controlada y acta de CUTOVER; si las barreras no pasan, mantener producción bloqueada.

## 4. Matriz de recuperación de los 11 bloques descartados

| Bloque antiguo | Reubicación | Decisión |
|---|---|---|
| C01 Auditoría | NAP01 y auditoría continua al abrir fase | CUBIERTO |
| C02 Importaciones históricas | NAP03 | CONSERVAR, verificar antes de añadir |
| C03 Regresiones / #49 | NAP05 | CONSERVAR focalizado |
| C04 Diversidad IA | NAP04, PR #62 | CONSERVAR con aceptación independiente |
| C05 Laboratorio visual | NAP04 y NAP08 | CONSERVAR valor, sin infraestructura superflua |
| C06 Recuperación integral | NAP05, NAP07 | CONSERVAR y comprobar casos de fallo |
| C07 .NET 10 LTS | NAP01-B | **HECHO** |
| C08 Host definitivo | NAP02/NAP03 | CONSERVAR, imprescindible |
| C09 Instalación/actualizaciones | NAP06/NAP07 | **OBLIGATORIO, autorización nueva** |
| C10 Certificación MSI | NAP08 | CONSERVAR, intervención física propietario |
| C11 CUTOVER | NAP09 | CONSERVAR con barrera #47 y aprobación posterior |

No se eliminan buenas mejoras solo por tardar. Se eliminan únicamente duplicaciones, refactorizaciones sin utilidad demostrada, laboratorios que no produzcan valor, integraciones externas no necesarias (p. ej. TeraBox directo), pruebas masivas rutinarias y cualquier paso de Codex que pueda hacer ChatGPT.

## 5. Regla concreta para usar Codex

Antes de proponer un encargo se exige un **informe de bloqueo** con:
1. Función concreta imposible de cerrar por ChatGPT y por qué (error, limitación de herramientas, acceso al sistema, dependencia).
2. Alternativas intentadas: API GitHub, workflow Windows focalizado, prueba sintética y cambio mínimo.
3. Por qué el requisito es indispensable para el objetivo aprobado; si es prescindible y solo Codex puede resolverlo, **aplazarlo/descartarlo**.
4. Definición del trabajo mínimo que haría Codex; archivos/zonas autorizadas, pruebas y evidencia que debe dejar versionada en GitHub para auditoría posterior de ChatGPT.
5. Prompt con recomendación expresa de **modelo y nivel de razonamiento**, sin iniciar Codex ni usar su cuota sin el propietario.

**No está demostrado hoy ningún bloque que necesariamente precise Codex.** El hecho de que una tarea sea compleja o de C# no la convierte automáticamente en tarea de Codex.

## 6. Seguridad y criterios de cierre transversales

- `main` protegida, PR revisables, sin cambios silenciosos sobre ramas ajenas. No merge de #62 hasta aceptación independiente.
- Git/backsups/restauración y cualquier escritura real quedan bajo permisos y aprobación explícitos; Gemini por acción voluntaria/contrato y sin claves en el repo.
- La licencia Six Labors continúa como secreto de Actions; ningún release empaqueta el archivo de licencia ni la expone.
- Diferenciar: **compilación de CI**, **prueba funcional sintética**, **prueba física MSI**, **instalador de laboratorio** y **permiso de CUTOVER**. Una no sustituye a la siguiente.
- El propietario autoriza desarrollar un instalador actualizable; **no se ejecuta instalación/desinstalación, cambio ACL, migración, activación de servicio ni publicación sobre datos reales en esta fase documental**.
- No añadir funciones nuevas por oportunidad sin relación con NAP. Documento vivo: podrá ajustarse por hallazgos sin malgastar la cuota de Codex.

## 7. Próxima acción

Iniciar **NAP02 — Host operativo conectado a WPF** desde `main` (ya migrada a .NET 10) con un inventario técnico de lo existente y una rama independiente. ChatGPT implementa y prueba directamente. No pedir Codex por anticipado. La aceptación física de la migración .NET 10 se mantiene pendiente de NAP08 o de un control sintético anticipado, sin bloquear el desarrollo.
