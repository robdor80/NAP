# NAP08 — Certificacion fisica Windows 11 / MSI Raider GE78 HX

Fecha: 13/11/2026. Repositorio: robdor80/NAP. Base: main@3edf82cd5a8035389111108a022c164eafec68d1.
Rama independiente: feature/nap08-certificacion-fisica-msi-20261113.
Responsables: ChatGPT prepara/audita; propietario ejecuta y acepta en MSI Windows 11 x64.
Estado inicial: **PENDIENTE DE EJECUCION FISICA**. No certifica CUTOVER.

## 1. Alcance y seguridad

Se comprobaran instalacion limpia, versiones, conservacion de datos sinteticos, SmartScreen/firma, WPF/RobStyle, bandeja, interfaz 2560x1600, desinstalacion y reinstalacion, Host sintetico seguro y actualizaciones cuando exista procedimiento acreditado.

No tocar cuentas, ACL, servicios, identidades, tareas, worktrees antiguos, permisos de padres, produccion real Nimroel/Star Trek, ZIP, PNG maestros, catalogos, journals, claves Gemini, licencias, ni registros de Git productivos. No ejecutar NAP CI general, pruebas ZIP de volumen ni cambios sobre #47/#49. Ningun instalador se ejecuta automaticamente en el MSI mediante esta PR.

**Gate de seguridad:** si ya hay instalacion, %LOCALAPPDATA%\NAP, settings.json, Host\launch.json, un proceso NAP o credenciales Gemini en el perfil Windows, la etapa Antes bloquea las pruebas. No borrar ni renombrar nada para forzar un PASS. Resolver aislamiento o permisos solo tras decision posterior y expresa; esta fase no crea cuentas ni modifica ACL.

## 2. Procedencia de artefactos

El workflow focalizado .github/workflows/nap08-physical-windows-packages.yml (rama NAP08, no CI general) valida el compilador Inno 6.7.4 oficial, genera paquetes Windows x64 autocontenidos, utiliza la licencia Six Labors de Actions sin publicarla y hace un ensayo sintetico de instalacion/desinstalacion en Windows Server 2022.

El ZIP de laboratorio contiene:

- base/output/NAP-Setup-2.0.0.exe, base/inventory.json y base/test-results.json
- next/output/NAP-Setup-2.0.1.exe y next/inventory.json
- NAP08-MANIFEST.json con version, commit y SHA-256

**Las versiones 2.0.0 y 2.0.1 se compilan desde el mismo codigo.** Son una prueba de instalador Inno y preservacion, no una prueba de cambio funcional ni de NAP07 Stage/Apply. Ninguna Release de GitHub se publica automaticamente.

Los hashes incluidos en el mismo ZIP no bastan como evidencia independiente. ChatGPT verificara run, procedencia y SHA fuera del propio ZIP antes de entregarlos para ejecucion. El producto NAP no tiene certificado de firma: registrar SmartScreen y no declarar editor verificado ni desactivar globalmente sus defensas.

## 3. P01 — Verificacion inicial sin efectos sobre NAP

Extraer el artefacto en Descargas\NAP08. Abrir PowerShell x64 en la raiz del checkout NAP de la rama aprobada; sin elevacion. Reemplazar el hash de ejemplo por el SHA-256 independiente publicado en el acta de Actions:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\NAP08.PhysicalAudit.ps1" -Etapa Antes -SetupPath "$env:USERPROFILE\Downloads\NAP08\base\output\NAP-Setup-2.0.0.exe" -TrustedSha256 "<SHA256_VERIFICADO>"

El auditor solo lee la presencia de configuraciones, estado Windows, nombre de procesos, integridad/hash y firma. No lee secretos, no inicia Host ni modifica activos. Solo escribe un JSON en Descargas\NAP08_Evidencias. Si devuelve BLOQUEADO/exit 2, detenerse y compartir el informe. No forzar la instalacion.

## 4. P02 — Instalacion inicial

**Solo si P01 PASS y se confirma que el perfil estaba vacio:** crear la marca sintetica de supervivencia en ese perfil:

    New-Item -ItemType Directory "$env:LOCALAPPDATA\NAP" | Out-Null
    [IO.File]::WriteAllText("$env:LOCALAPPDATA\NAP\NAP08-PRESERVAR.txt", 'NAP08-LAB-SOLO')

Ejecutar manualmente el Setup 2.0.0 como usuario normal, destino fijo %LOCALAPPDATA%\Programs\NAP. No usar /DIR, no elevar ni iniciar Host. Registrar lo que muestre SmartScreen; si bloquea, detenerse hasta decidir expresamente si continuar. Verificar accesos directos/iconos y comprobar contra el inventario:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\NAP08.PhysicalAudit.ps1" -Etapa Instalado -ExpectedVersion '2.0.0' -InventoryPath "$env:USERPROFILE\Downloads\NAP08\base\inventory.json"

## 5. P03 — Experiencia visual y WPF

Abrir NAP solo con P01 limpio y Host/configuracion de universos ausentes. Probar Inicio, Catalogo, Produccion, Historial, Herramientas y Ajustes; minimizar a bandeja, restaurar, cerrar. Comprobar ventana nativa MSI 2560x1600 y tamano reducido 1200x700, DPI, texto, scroll, iconos, enlaces y errores de binding. No seleccionar carpetas reales ni iniciar procesamientos, Gemini, backups, Git o restauraciones.

Vista previa sintetica integrada (captura y cierre automatico, sin cargar settings/Host):

    & "$env:LOCALAPPDATA\Programs\NAP\NAP.App.exe" '--phase6-preview=active' "--phase6-output=$env:USERPROFILE\Downloads\NAP08_Evidencias\preview"

La vista previa no reemplaza el examen humano de la interfaz. Guardar capturas sin rutas, claves, seriales o datos privados.

## 6. P04 — Instalacion sucesora de laboratorio

Solo con App cerrada, Host ausente y SHA independiente verificado, ejecutar manualmente NAP-Setup-2.0.1.exe en la misma ubicacion Inno. Verificar version/hash y marcador:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\NAP08.PhysicalAudit.ps1" -Etapa Instalado -ExpectedVersion '2.0.1' -InventoryPath "$env:USERPROFILE\Downloads\NAP08\next\inventory.json"

No ejecutar NAP07 Stage/Apply/Recover/Rollback de forma improvisada. Sin Release fiable, SHA independiente y transaccion controlada se declara NAP07 FISICO NO PROBADO; tampoco se fuerza GITHUB_ACTIONS en el MSI.

## 7. P05 — Desinstalacion y datos intactos

Cerrar completamente WPF; no debe existir Host. Ejecutar el desinstalador Inno de forma manual. No borrar datos ni usar limpieza forzada. Comprobar ausencia de NAP.App.exe y permanencia del marcador:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\NAP08.PhysicalAudit.ps1" -Etapa Desinstalado

Reinstalacion: comprobar por separado y registrar si realmente se realiza. No declararla probada por simetria.

## 8. Puertas pendientes y acta

- Host fisico de dos universos y su IPC: requiere fixture seguro exclusivamente TEMP, con contrato NAP02/03, antes de ejecutarlo. No inventar launch.json conectado a datos reales.
- Actualizador NAP07 Stage/Apply/Recover: ausencia de Release autorizada; los fixtures de GitHub Actions anteriores no equivalen a esta prueba.
- Firma/SmartScreen: observar y registrar realmente en Windows 11; sin certificado de firma NAP.
- #49: timeout IPC tras cancelacion, StorageUnavailable y advisories ImageSharp siguen abiertos sin causa fisica aclarada.
- #47/NAP09: identidad unica, ACL y padres, escritores antiguos, 6.864 leases historicos (8 no vacios), copia externa restaurable y CUTOVER fuera de NAP08.

Acta minima: fecha, Windows build, version MSI y escala, workflow/run y HEAD, hashes independientemente verificados, JSON Antes/Instalado-2.0.0/Instalado-2.0.1/Desinstalado, capturas inocuas, nota SmartScreen, prueba de bandeja, prueba del Host cuando corresponda, regresiones observadas y confirmacion expresa de CERO operaciones productivas.

**Resultado actual: PENDIENTE.** No hay prueba fisica en el MSI ni aceptacion del propietario acreditadas. Mantener PR NAP08 Draft y no fusionar/cerrar #47/#49, ni afirmar certificacion operativa. Proxima barrera: NAP09 con autorizacion propia.

## 9. Incidencia de ejecución física — replanificación 14/11/2026

El propietario verificó en su MSI real y desde PowerShell de Windows 11 los SHA-256 de ambos
instaladores: 2.0.0 `acba41477de7b266fa8203977f9461986b88c5bb0aa5659f4852de9c52ac940a`
y 2.0.1 `0648c6d71737f80a546bad643a3e3c8fc4bb6d9e517eb39f111b19624afcecdc`.
La auditoría `Antes` de `andro` bloqueó por configuración NAP/Gemini preexistente; es
un **bloqueo preventivo correcto**, no un defecto demostrado del instalador.

Se creó temporalmente un usuario local `NAP08_Lab`, pero el propietario decidió eliminarlo.
**No existe ya autorización ni base operativa para lanzar pruebas con esa cuenta.**
Los dos lanzadores de doble clic que dependían de ella se retiraron del repositorio.
No instalar en `andro`, no eliminar ni renombrar `%LOCALAPPDATA%\\NAP`, ni tocar claves,
servicios, ACL o configuraciones preexistentes para falsear un PASS.

El resultado acreditado hasta la fecha es: **integridad física de los instaladores: PASS;
instalación/actualización/WPF/Host/desinstalación física: NO PROBADAS**. Las pruebas
sintéticas PASS en Windows Server 2022 (Actions) permanecen registradas aparte y no sustituyen
la certificación real en Windows 11 ni el CUTOVER.

**Siguiente vía técnica:** preparar, por ChatGPT y sin cambios locales del propietario,
un paquete portable para pruebas físicas de interfaz en modo sintético y lectura exclusiva,
o evaluar un entorno efímero aislado para ensayos destructivos. Toda alternativa debe validar
primero sus condiciones reales de uso; no crear cuentas, no exigir cambios de sesión, no
instalar componentes de Windows automáticamente y no garantizar certificación física antes
de obtener pruebas reales. Mantener NAP08 PR en Draft y #47/#49 abiertos.
