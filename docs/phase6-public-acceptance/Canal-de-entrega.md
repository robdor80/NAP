# Canal de entrega autorizado

El 25/10/2026 el responsable autorizó expresamente la publicación del paquete compilado de prueba en GitHub Actions. Esta decisión sustituye el requisito anterior de transporte privado runner → Google Drive. No hacen falta credenciales de Google en Actions y no se han añadido.

El ejecutable se entrega solo mediante el artefacto de la ejecución identificada en README.md y en la guía PDF. El archivo descargado tiene un único nivel: al descomprimirlo aparecen NAP.App.exe, Iniciar-prueba-NAP.cmd, las dependencias, instrucciones, avisos, SHA256SUMS.txt y PROCEDENCIA.json. No hace falta descomprimir otro ZIP interno.

Los artefactos del repositorio público requieren iniciar sesión normal en GitHub para descargarlos desde el navegador. No es una solicitud de permisos privados ni requiere un token personal. Referencia oficial: https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts . Retención solicitada y comprobada: 90 días. La fecha exacta de caducidad figura en download-verification.json.

Los informes permanecen en la carpeta existente de Drive, con sus permisos actuales. No se duplica allí el ejecutable. Se mantienen las evidencias históricas de la preparación de 24/10/2026; sus manifiestos de candidato retenido no corresponden al programa descargable actual.

Esta autorización no crea una GitHub Release, un instalador definitivo, una licencia comercial de NAP ni un updater. Main permanece intacta. El responsable aún debe realizar la aceptación visual humana en Windows 11; no se ha accedido al MSI ni se ha cerrado la Fase 6.
