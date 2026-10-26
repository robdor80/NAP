# Especificación preliminar de distribución futura (sin implementar)

Objetivo posterior: instalador **.exe Windows x64**, una sola aplicación NAP que preserve funcionalidades anteriores. Este documento no autoriza construir instalador/updater, Release pública, instalación ni cambios en robertodorado.es.

| Aspecto | Requisito antes de implementar |
|---|---|
| Empaquetado | Evaluar publicación .NET 8 Windows Desktop x64 autónoma frente a runtime externo. Incluir DLL nativas SQLite/WebP y todo contenido/perfiles necesarios, avisos/licencias y manifiesto de versiones. Fijar SDK/runtime/dependencias; evitar rutas del desarrollador. |
| Instalación | Preferentemente por usuario y sin elevación. Separar binarios de datos/configuración. La instalación por usuario no es una frontera suficiente para un host privilegiado: resolver #47 antes de producción. |
| Actualización | Estudiar **Velopack** como candidato, sin adoptarlo todavía. Revisar firma del feed/paquetes, origen HTTPS, hashes, control de versiones/rollback, canales privado/estable, parada/drenaje de host y coordinación de lectores/escritores. Nunca reemplazar binarios durante una operación aceptada. |
| Datos | Preservar configuración, registros administrativos, perfiles importados, catálogos, journals, receipts y originales. Migraciones versionadas, backup verificado, validación previa y recuperación sin reactivar escritores antiguos. No restablecer rutas silenciosamente. |
| Desinstalación | Retirar binarios/accesos/registro del instalador; conservar datos/configuración por defecto. Eliminación de datos solo opción explícita separada y con backup. No borrar licencias privadas ni raíces ajenas. |
| GitHub Releases | Solo tras autorización: tag/SHA exactos, canal, notas, manifiesto SHA-256 de EXE/DLL/runtime/config, artefactos firmados y procedencia del workflow. Distinguir prerelease privada de Release pública; un repositorio público no da privacidad a binarios ordinarios de Actions. |
| Web | Futuro enlace de descarga desde **robertodorado.es** al canal aprobado y versión verificable. No modificar web ahora ni insertar enlaces a ejecutables todavía inexistentes. |
| Integridad y procedencia | Verificar hashes tras build, transporte e instalación; firma Authenticode con timestamp, clave protegida en servicio/secretos de firma y revocación planificada. Hash sin procedencia autenticada no acredita autoría. Evaluar attestations/SBOM de dependencias. |
| ImageSharp | Validación oficial de build con secreto temporal. Nunca incluir sixlabors.lic ni secretos; confirmar nombres de ensamblados, tipo/vigencia y derecho de uso/redistribución bajo condiciones reales. Fecha administrativa no acredita licencia del proveedor. Avisos NU1902/NU1903 visibles y decisión de seguridad documentada. |
| Privado frente a público | Uso privado no concede automáticamente derecho de redistribución pública. Revisar licencias de biblioteca, validación y terceros y condiciones comerciales antes de cada canal. Si falta autorización, bloquear entrega y documentar, sin parchear validadores. |
| Firma y Windows | Elegir certificado/editor y proceso seguro; advertencias SmartScreen/Defender pueden persistir por reputación aunque haya firma. No pedir desactivar protecciones ni eludir advertencias. Detener ante firma/hash inesperados. |
| Validación | Pruebas autorizadas de instalación por usuario, desinstalación conservando datos, actualización/rollback, errores de red y recuperación, compatibilidad Windows 11/MSI y denegación de antiguos escritores. Requieren alcance posterior. |

No hay instalador definitivo, updater, tag/Release pública ni modificación web en P6.8.
