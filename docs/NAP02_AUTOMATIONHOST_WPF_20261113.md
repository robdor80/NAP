# NAP02 — Integración operativa de AutomationHost con WPF (13/11/2026)

## Alcance de producto
NAP02 incorpora **al binario WPF ordinario** la composición y localización del ejecutable AutomationHost, un gestor de arranque/reconexión y controles por universo. No crea una versión DEV, una segunda aplicación ni un motor paralelo.

- Al iniciar NAP, si existe una configuración de Host previamente instalada en `%LOCALAPPDATA%\\NAP\\Host\\launch.json`, se intenta **conectar primero** al proceso existente. Si no existe, se lanza una única instancia con mutex interproceso recuperable tras cierre abrupto entre sesiones WPF.
- Es posible seleccionar explícitamente el mismo archivo mediante `--automation-managed-config=<ruta>`; el modo de conexión `--automation-config=<ruta>` se conserva para procesos Host ya gestionados externamente. No se permiten ambos a la vez.
- El gestor verifica challenge IPC, sesión, instalación, universo y raíces antes de entregar los servicios remotos a la interfaz. Ante fallo de autenticación, desajuste de identidad o error de configuración **no vuelve** a un escritor local ni inventa un estado operativo.
- Cerrar WPF no mata el Host que mantiene trabajo durable. Un nuevo WPF puede reconectarse al mismo Host. La detención por universo usa `StopUniverse` y conserva disponibles los demás; el apagado global anterior continúa existiendo como operación separada y explícita.
- La pantalla de detalle de producción/historial permite **cancelar o reactivar un asset individual** mediante el ledger durable del Host, con confirmación por universo, sin crear una cola local. El control indica que no puede deshacer una etapa indivisible ya iniciada. Un control no verificable no se muestra como disponible.
- La compilación normal de WPF incluye los artefactos del ejecutable Host. No hay edición de producto recortada.

## Invariantes de seguridad
NAP02 **no** levanta #47, no configura ACL, no instala un servicio, no habilita elevación, no mueve maestros y no altera las raíces reales. El constructor de AutomationHost sigue limitando la ejecución a laboratorios TEMP autorizados, y las políticas siguen exigiendo concesión previa. El mecanismo que instalará una configuración productiva bajo la raíz privada del usuario se certificará durante NAP08/NAP09, tras autorización operativa específica. En ausencia de esa configuración, la aplicación conserva el comportamiento normal preexistente en lugar de simular un Host.

No se modifica el tratamiento secreto de Six Labors/Gemini, el esquema SQLite, los contratos IPC existentes ni la PR #62. NAP CI general permanece desactivada.

## Comprobación exigida antes de cierre
1. Build Release .NET 10 Windows x64 de la solución completa, verificando que WPF incluye Host.
2. Prueba WPF de arranque gestionado y segunda conexión sobre la misma sesión y sin crear un segundo motor.
3. Prueba de detener un universo sin apagar otros ni alterar el proceso.
4. Regresión de cierre de WPF, persistencia/reinicio de Host y controles por universo. El caso histórico de fallo de persistencia de apagado global presentó un timeout en Windows durante la auditoría NAP02; queda registrado para NAP05/#49 y no se utiliza para acreditar ni invalidar un cambio que no modifica ese contrato.
5. Inspección de errores o regresiones. **No se declara validación de producción/MSI por pasar Actions.**

## Puertas posteriores
NAP03 integra la cadena de assets sobre el Host existente. NAP04 cierra UI y revisión visual. NAP05 trata #49. NAP06/NAP07 distribuyen y actualizan el instalador. NAP08 valida físicamente el MSI. NAP09 decide y ejecuta CUTOVER, con #47 abierto hasta entonces.
