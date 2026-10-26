# NAP 2.0 — F7.1 · Acta de cierre documental
**Fecha:** 26 de octubre de 2026. **Estado:** CERRADA como subfase documental/arquitectónica; F7.2 pendiente.
**Repositorio:** robdor80/NAP. **Rama exclusiva:** `chatgpt/nap-2-f7-git-contracts`.
**Base `main` comprobada:** `a6dc6fe81815db93f744d00f6ecd17e315c54e67`.

## Trabajo concluido
- Se confirmó PR #53 fusionada y `main` protegido; #47 y #49 continúan abiertos.
- Se inspeccionaron cuatro archivos de workflow. `NAP CI` usa `workflow_dispatch` solamente; permanece explícitamente prohibida su ejecución. Fase 6 conserva un trigger push **solo** para su rama histórica; no abarca esta rama.
- Auditoría estática del Git nativo (inspector, seguridad, proceso, receipts, service), Job/Queue/producción y UI manual.
- Se identificó que **GitOutbox no existe** aún en `main` y debe implementarse como subsistema durable independiente, sin tratar receipts existentes como Outbox.
- Se corrigió la confusión sobre `RemoteTip == HEAD`: se exige inicialmente; durante push del commit propio se comprueba remoto BaseHead o CommitSha.
- Se fijó la restricción `RequireNoPending`, autorización por universo/repo, modos Disabled/Manual/AutoCommitBatch/AutoCommitAndPushBatch, recuperabilidad sin repetir producción, y relación Job/Queue COMPLETED independiente de Git.
- Se preparó un encargo específico F7.2 para Codex y una matriz de pruebas focalizadas sintéticas.

## Entregables
1. `NAP_2_0_F7_1_GIT_ARCHITECTURE.md`: contrato de Fase 7.
2. `NAP_2_0_F7_1_CODIGO_Y_ACTIONS_AUDITORIA.md`: fuentes y hallazgos contrastados.
3. `NAP_2_0_F7_1_SECURITY_ACCEPTANCE_MATRIX.md`: casos de aceptación acotados, aún no ejecutados.
4. `NAP_2_0_F7_2_CODEX_BRIEF.md`: instrucciones técnicas de implementación posteriores.

## Seguridad / alcance exacto
- **No se ha implementado código C# ni cambiado WPF.**
- **No se ha ejecutado ningún test**, CI ni workflow; no se han utilizado ZIP ni repos ficticios/productivos.
- **No se ha modificado `main` directamente**, ni CUTOVER, secrets, licencias, ACL, raíces, assets, Releases ni instalaciones.
- Estado administrativo de habilitación/deshabilitación de workflow en el servidor GitHub no acreditado por API; sí se leyó el disparador YAML. Mantener prohibición de ejecución total.
- #47 y #49 permanecen sin cerrar, y la fase completa NAP 2.0 todavía no está certificada para producción.

## Hito siguiente
Encargar F7.2 a Codex **solo cuando se active esa subfase** con instrucciones del documento correspondiente. Auditar implementación y evidencias focalizadas antes de F7.3. La PR final y eventual merge se reservan a F7.5 bajo permiso expreso del responsable.
