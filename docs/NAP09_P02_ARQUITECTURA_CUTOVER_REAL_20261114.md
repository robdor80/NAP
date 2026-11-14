# NAP09 — Arquitectura corregida por decisión del propietario

**Fecha: 14 de noviembre de 2026. Estado: diseño sustituido; no ejecutar CUTOVER.**

## Decisión definitiva para esta fase

**NAP utilizará la sesión de Windows habitual del propietario. NO se crearán cuentas Windows adicionales, identidades escritoras separadas ni servicios de Windows.** Tampoco se hará depender NAP09 de una instalación administrativa paralela en Program Files. La propuesta de «cuenta escritora exclusiva», y todo el código NAP09 creado para imponer SID diferentes, **queda expresamente descartada por el propietario**.

La fase NAP09 debe continuar sobre el modelo normal de NAP 2.0:
- Interfaz WPF y `NAP.AutomationHost` bajo el **mismo usuario actual**, usando los controles de arranque y reconexión ya existentes de NAP02.
- IPC `PipeOptions.CurrentUserOnly` con autenticación HMAC; sin permiso para otros usuarios.
- Un único propietario efectivo por universo mediante coordinadores, leases/mutex y ledger duradero, con idempotencia y comprobación de política aprobada. No inferir seguridad por un simple número de archivos de coordinación.
- Mantener las rutas actuales de Nimroel y los datos históricos; no cambiar `Documents` ni `Documents/GitHub` por sorpresa.
- Preparar una habilitación de **producción en el Host existente**, independiente del modo laboratorio, sin perder el control de autorizaciones por universo, sin crear cuentas nuevas y sin activarla implícitamente.
- Respaldos externos y restauración verificable, comprobación en frío de escritores históricos y protocolo reversible para entrar en funcionamiento real.

## Corrección efectuada en la PR #73

Se han revertido los cambios de NAP09 en `App.xaml.cs`, `ManagedAutomationHost.cs`, `AutomationHost/Program.cs`, `AutomationHost.cs`, `AutomationHostLaunchConfiguration.cs` y `AutomationProtocol.cs`, dejando las versiones que ya funcionaban en `main`. Se han retirado de la rama los módulos nuevos que requerían SID múltiples, el paquete administrativo dedicado, las pruebas asociadas y sus workflows. Los experimentos permanecen rastreables en el historial Git, **pero no forman parte del código activo propuesto para fusión**.

Se conservan la importación histórica de los 39 retratos y sus PNG maestros, los informes, inventarios de solo lectura y las pruebas sintéticas pertinentes. Los 39 retratos NO requieren ninguna repetición de importaciones.

## Situación real

El Host normal sigue limitado a raíces TEMP de laboratorio. NAP09 no está terminada: faltan diseñar, implementar y probar la habilitación bajo el mismo usuario; preservar control de escritor único y compatibilidad con Nimroel y Star Trek; acreditar restaurabilidad y efectuar el CUTOVER físico tras autorización explícita. Las incidencias #47/#49 siguen abiertas. **No hay activación productiva ni cambios en el MSI.**

El objetivo es una aplicación personal de escritorio operativa, no un despliegue empresarial. No volver a proponer cuentas adicionales o servicios Windows sin que el propietario los solicite.
