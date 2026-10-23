# Resultados exactos — P6.8

**Código efectivamente verificado: `ed556bbbdf8a6971cd22d38f96ff88885a366225`.** [Actions 39921263656](https://github.com/robdor80/NAP/actions/runs/39921263656). Commit de evidencia/documentación posterior no equivale a nueva compilación.

**Final: dos builds Release Windows correctos; 157/157 casos Passed, 0 fallos, 0 no ejecutados. 113 PNG distintos, 38 escenarios, 76 procesos.** Cero errores de binding, 76 stderr vacíos. 1440×900 y 1200×700 a 96 DPI. Tray hide/restore comprobado en los procesos. Inventario principal: 400,88 / 200,88 px útiles; también se comprueba límite exterior y visibilidad de rutas/propuesta.

| Ejecución | SHA de código | Build/tests | PNG conservados | Resultado real |
|---|---|---|---|---|
| [39918769852](https://github.com/robdor80/NAP/actions/runs/39918769852) | 15a52baa1967100a3ad748e593cf22c60b557d33 | Ambos builds OK; 157/157 | 13 | Recurso Visible faltante en el cambio inicial; render se detiene en Ajustes. |
| [39919314372](https://github.com/robdor80/NAP/actions/runs/39919314372) | 00270c3d8a583acf1ad6b7c05a0c605336927fe3 | Ambos builds OK; 157/157 | 97 | Viewport del histórico vacío a 2 px; el estilo local perdía la herencia RobStyle. |
| [39920176930](https://github.com/robdor80/NAP/actions/runs/39920176930) | 3f8dd62f667f923ccd29b58688189c26948b7cfd | Ambos builds OK; 157/157 | 80 | Viewport compacto de 106,26 px. Además, push de evidencia rechazado por avance concurrente de la misma rama; recuperado en la ejecución siguiente, sin forzar push. |
| [39920275175](https://github.com/robdor80/NAP/actions/runs/39920275175) | e588886f316598a02baa606d16b99e7aae87acec | Ambos builds OK; 157/157 | 80 | Mismo viewport compacto insuficiente; conserva además comprobaciones de rutas. Ejecución preliminar, no aceptación. |
| [39920731413](https://github.com/robdor80/NAP/actions/runs/39920731413) | 67693a04c3d2a79016dcd5cbfc808e1f3b62ea23 | Ambos builds OK; 157/157 | 111 | Layout de exportación aún no creado al buscar mensaje; texto sintético ya exportado/cotejado. Corregido orden de layout. |
| [39921263656](https://github.com/robdor80/NAP/actions/runs/39921263656) | ed556bbbdf8a6971cd22d38f96ff88885a366225 | Ambos builds OK; 157/157 | 113 | Final: builds, pruebas, 76 procesos, visibilidad y unicidad aprobados. |

Los cinco intentos anteriores son evidencia preliminar fallida de render/entrega. **No se suman** como 942 tests independientes. Los PNG anteriores pueden repetirse entre ejecuciones del mismo estado: son repeticiones justificadas y se separan de la galería final.

| Grupo final | Casos ejecutados |
|---|---|
| Phase6OverviewTests | 13 |
| Phase6WorkflowTests (sin caso ZIP) | 7 |
| Phase6PolishTests | 11 |
| Phase6SettingsTests | 18 |
| Phase6HistoricalTests | 14 |
| ProductionDestinationResolverTests | 93 |
| ProfessionalUiTests.UnconfiguredNavigationNeverInventsCountsOrPerformsIo | 1 |

Filtro literal conservado en workflow/log. El caso `OneSyntheticAssetPersistsVerifiedDecisionAndContinuesWithoutAuditOverrideOrGlobalChange` se excluye porque crea un ZIP sintético. No se ejecuta NAP.sln test, suite general, carga, benchmarks ni pruebas ZIP de volumen. Los fixtures de cola son metadata/rutas ficticias; no se procesan assets reales.

El aviso **xUnit2030 no aparece en la compilación final**. Se conserva la aserción Assert.Contains. Cinco advisories NU1902/NU1903 ImageSharp permanecen visibles: GHSA-gwg2-r3hj-4w44, GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w, GHSA-wmxv-xphr-5c9g. No se interpreta como análisis de dependencias limpio.

Compilación y render en Windows Server 2022 del runner, no en el MSI Windows 11. Sin aceptación humana automática. El timeout IPC y StorageUnavailable de #49 no quedan diagnosticados ni resueltos por estos resultados.

Verificación offline: desde la raíz del repositorio, `python docs/phase6-acceptance/Verify-evidence.py` (Python 3 y Pillow). Coteja TRX, PNG originales y píxeles, estados, stderr, hashes de fuentes frente al SHA, y SHA-256 de la entrega. Los logs se conservan con LF normalizado; PNG/TRX/stdout/stderr mantienen bytes originales.

El workflow recibió después una opción de recuperación de originales (sin build/tests/render), para conservar ZIP/TRX con digest y bytes originales ante normalización de Git. No se atribuye esa operación al código probado ni se cuenta como una nueva ejecución funcional. Los hashes de fuentes cubren 522 archivos de producto/tests/configuración/helpers; verified-build-workflow.yml conserva el workflow exacto de ed556bb para reproducibilidad.

Recuperación original: [Actions 39922236605](https://github.com/robdor80/NAP/actions/runs/39922236605), solo job preserve-original-evidence; focused-review omitido expresamente. **Seis ZIP originales: sus SHA-256 coinciden con Actions y cada entrada coincide byte por byte con runs/<id>.** No se ejecutan pruebas del producto en esa operación.
