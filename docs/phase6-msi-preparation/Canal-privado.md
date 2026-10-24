# Canal de entrega — comprobación y bloqueo

## Evidencia efectiva

- `robdor80/NAP` tiene visibilidad **public** según la API, comprobada desde Codex Cloud. No se cambia.
- GitHub permite descargar artefactos a personas autenticadas con acceso de lectura al repositorio. En este repositorio público, no es un grupo privado de probadores. La ausencia de una Release no aporta privacidad.
- La carpeta `NAP 2.0/Fase 6/Aceptación MSI - Preparación de prueba privada` y sus padres tienen `shared=false` y un único permiso de propietario. Se han creado cuatro subcarpetas, sin llamadas de compartición ni modificación de permisos.
- La conexión autorizada de Drive permite subir archivos desde Codex Cloud y recuperarlos completos. Se ha transferido un TXT inocuo, vuelto a descargar por el conector y comparado SHA-256 y tamaño. Esto comprueba la ruta documental real, no una transferencia de binarios desde Actions.
- La identidad de Drive vive en el conector. No se ha obtenido, exportado ni instalado su token en Actions. El runner no recibe automáticamente esta identidad.
- El entorno Cloud no declara identidades salientes de Google ni secretos de Drive preparados. La API de listado de nombres de secretos de Actions devolvió **403 Resource not accessible by integration**; no es posible afirmar que no existen secretos en GitHub. Los workflows inspeccionados solo usan el secreto de compilación Six Labors, no un destino privado de distribución.
- No se ha modificado la configuración de red para acceder a Google desde el executor. El conector de Drive funciona por su conexión autorizada independiente.

## Conclusión

**Destino Drive privado validado para documentación; transporte privado de binarios runner→Drive no establecido.** No hay ejecutable publicado ni subido a Drive. No se ha utilizado un artefacto público como escala intermedia. El candidato de publicación se retiene en RUNNER_TEMP y se destruye al finalizar; no se crea ZIP de distribución.

La comparación después de la descarga del TXT no acredita que un paquete inexistente haya sido transferido ni que el propietario ya haya realizado su descarga en el MSI. Los enlaces de Drive requieren autenticación del propietario y pueden recuperarse posteriormente mediante la conexión actual de ChatGPT mientras conserve acceso.

## Alternativa concreta con una intervención inicial

La opción operativa propuesta es autorizar una identidad de Google dedicada a la transferencia con acceso **solo a la carpeta de entrega**, preferentemente por federación OIDC de GitHub y credenciales de corta duración. Si la cuenta personal/Drive y la infraestructura no permiten esa federación y destino, usar una autorización OAuth dedicada y revocable, almacenada exclusivamente como secretos de Actions mediante la interfaz de GitHub; nunca pegar tokens en ChatGPT, código ni logs. La elección y provisión de esa identidad requieren intervención del titular fuera de las credenciales disponibles. No solicitar ni reutilizar la licencia del MSI para este fin.

No se ha creado esa identidad, no se han concedido permisos ni registrado secretos. El permiso adicional de una identidad nueva a Drive necesita autorización expresa porque el usuario prohíbe modificar compartición. No basta crear un secreto llamado DRIVE_TOKEN sin comprobar su identidad y alcance.

Antes de habilitar publicación, el workflow deberá: validar ID de carpeta/propietario/permisos sin grants `anyone` o `domain`; publicar tras superar licencia y aislamiento; limpiar Six Labors; escanear árbol y formar manifiesto; transferir por HTTPS autenticado sin upload-artifact de binarios; volver a descargar íntegramente y comparar SHA-256; comprobar permisos de carpeta y archivo; registrar evidencias sin credenciales. Probar primero con un archivo inocuo. No copiar la identidad al paquete.

Una transferencia cifrada mediante una herramienta estándar y clave privada retenida fuera de GitHub podría evitar credenciales Drive en el runner, pero **no se ha implementado ni validado**. No se entrega como solución probada ni se confunde cifrado de contenido con privacidad de metadatos/artefactos. En esta sesión, los binarios no salen del runner.

Fuentes oficiales consultadas el 24/10/2026:

- https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts
- https://developers.google.com/drive/api/guides/manage-uploads
- https://developers.google.com/drive/api/guides/manage-sharing
- https://developers.google.com/identity/protocols/oauth2
