# P6.7 — revisión humana pendiente

[Informe Markdown](Informe-revision.md) · [Informe PDF](Informe-revision.pdf) · [Galería PDF](Galeria-WPF.pdf) · [Galería Markdown](Galeria-WPF.md)

- [Inventario sintético documentado](Inventario-sintetico.md); [fixture JSON](../../src/NAP.Core/Fixtures/TreskalSyntheticInventory.json).
- [Simulación completa y anomalías](Informe-simulacion-anomalias.txt).
- [Auditoría previa](AUDIT.md); [homologación real futura, no ejecutada](Homologacion-real-futura.md).
- [TRX original final](final/phase6-historical.trx); [logs de build y revisión](final/build-and-review.log).
- [Manifiesto de procedencia](evidence.json); [hashes](SHA256SUMS.txt); [hashes de fuentes verificadas](verified-source-files.json).
- [Evidencias preliminares](preliminary/); [incidencias de transferencia](Transferencia-evidencia.md).

Código verificado `f3130ce7144644fb4179920c0332371ed0c5991c`. [Actions 39870877507](https://github.com/robdor80/NAP/actions/runs/39870877507): dos builds Release, 108/108 pruebas focalizadas, 34 renderizados WPF a 1440×900 y 1200×700. No se ejecutan tests de volumen ZIP ni suites generales. Los commits posteriores de documentación no se atribuyen checks nuevos.

Verificación offline: `python Verify-evidence.py` (Python, Pillow). Renderizado reproducible en Windows con el ejecutable de esa fuente: `NAP.App.exe --phase6-preview=historical --phase6-output=<directorio-nuevo>`; añadir `--compact` para 1200×700. Los nueve modos y las comprobaciones de captura están en el workflow focalizado versionado. Se necesita el SDK/runtime Windows Desktop y la validación de licencia oficial vigente; no desactivar sus guardas.

Los 39 registros individuales son sintéticos. Solo los totales y las reglas citadas proceden del corte documental del 03/10/2026. Ningún asset se presenta como homologado para producción. Sin originales, adopción, CUTOVER, cambios en main, PR, fusión ni P6.8.
