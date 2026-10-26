# NAP 2.0 — Encargo Codex F7.2 (pendiente de ejecución)
Implementar commits automáticos seguros siguiendo docs/NAP_2_0_F7_1_GIT_ARCHITECTURE.md. Primero leer código real de GitProductionService, GitOutbox, receipts, JobStateStore, coordinadores, permisos y recuperación; reutilizar contratos sin sustituirlos ciegamente.

Entregables:
1. Configuración persistente por universo/repositorio, Disabled por defecto, Manual y AutoCommitBatch; preparar modo AutoCommitAndPushBatch sin habilitar push todavía.
2. Autorización versionada y revocable, validación de rutas/identidad/HEAD y ownership por receipt + SHA-256.
3. Intents de Outbox durables e idempotentes después de COMPLETED; serialización interproceso por repo, staging exacto aislado, commits seguros y recuperación tras caída.
4. Estados Pending/Committed/Blocked y errores accionables. Prohibido modificar índices ajenos, hacer commits parciales inseguros, incluir cambios de terceros o afectar COMPLETED.
5. Pruebas pequeñas y focalizadas con repositorios temporales ficticios y como máximo 20 ZIP por escenario. No CI general ni suites masivas.

No tocar main, ProductionRoot/ArchiveRoot reales, repositorios productivos, licencias, secretos ni CUTOVER. No habilitar push automático en F7.2. Documentar rutas cambiadas, comandos de prueba concretos, evidencias, riesgos y bloqueos. Entregar commits en la rama de Fase 7 para auditoría de ChatGPT; no fusionar.
