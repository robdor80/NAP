# Inventario sintético de referencia P6.7

Los totales por grupo proceden del corte histórico documentado del 03/10/2026. Las filas siguientes son escenarios ficticios, no el inventario nominal del registry. No se han abierto originales. Ninguna clasificación individual constituye un dato real de Treskal.

| Grupo histórico documentado | Cantidad |
| --- | ---: |
| farmer | 10 |
| carpenter | 5 |
| blacksmith | 5 |
| healer | 3 |
| children | 6 |
| elder | 6 |
| fisher | 2 |
| tavernkeeper | 2 |
| TOTAL | 39 |

Los cuatro archivos históricos eran `<asset_id>.webp`, `<asset_id>_prompt.md`, `<asset_id>_info.md` y `<asset_id>_visual_identity.json`. No se han creado archivos de retratos ni paquetes ZIP.

En children el fixture reproduce la distribución documental de roles y sex; sus seis identificadores e identidades son ficticios. En elder se incluye deliberadamente una clasificación incorrecta para demostrar la advertencia: no es una anomalía atribuida a los originales.

| Referencia ficticia | Grupo | Clasificaciones sintéticas | Escenario sintético |
| --- | --- | --- | --- |
| portrait_p67_reference_001 | farmer | culture=norgard, location=treskal, role=farmer, sex=male | known |
| portrait_p67_reference_002 | farmer | culture=norgard, location=treskal, role=farmer, sex=female | known |
| portrait_p67_reference_003 | farmer | culture=norgard, location=treskal, sex=male | unknown |
| portrait_p67_reference_004 | farmer | culture=norgard, location=treskal, sex=female | ambiguous |
| portrait_p67_reference_005 | farmer | culture=norgard, location=treskal, role=farmer, sex=male | incomplete |
| portrait_p67_reference_006 | farmer | No verificable | unverifiable |
| portrait_p67_reference_007 | farmer | culture=norgard, location=treskal, role=unlisted_role, sex=male | unknown_value |
| portrait_p67_reference_008 | farmer | culture=norgard, location=treskal, role=farmer, sex=female | known |
| portrait_p67_reference_009 | farmer | culture=norgard, location=treskal, role=farmer, sex=male | known |
| portrait_p67_reference_010 | farmer | culture=norgard, location=treskal, role=farmer, sex=female | known |
| portrait_p67_reference_011 | carpenter | culture=norgard, location=treskal, role=carpenter, sex=boy | incompatible_sex |
| portrait_p67_reference_012 | carpenter | culture=norgard, location=treskal, role=carpenter, sex=female | known |
| portrait_p67_reference_013 | carpenter | culture=norgard, location=treskal, role=carpenter, sex=male | known |
| portrait_p67_reference_014 | carpenter | culture=norgard, location=treskal, role=carpenter, sex=female | known |
| portrait_p67_reference_015 | carpenter | culture=norgard, location=treskal, role=carpenter, sex=male | known |
| portrait_p67_reference_016 | blacksmith | location=treskal, role=blacksmith, sex=male | missing_dimension |
| portrait_p67_reference_017 | blacksmith | culture=norgard, location=treskal, role=blacksmith, sex=female | known |
| portrait_p67_reference_018 | blacksmith | culture=norgard, location=treskal, role=blacksmith, sex=male | known |
| portrait_p67_reference_019 | blacksmith | culture=norgard, location=treskal, role=blacksmith, sex=female | known |
| portrait_p67_reference_020 | blacksmith | culture=norgard, location=treskal, role=blacksmith, sex=male | known |
| portrait_p67_reference_021 | healer | culture=Norgard, location=treskal, role=healer, sex=male | invalid_naming |
| portrait_p67_reference_022 | healer | culture=norgard, location=treskal, role=healer, sex=female | known |
| portrait_p67_reference_023 | healer | culture=norgard, location=treskal, role=healer, sex=male | known |
| portrait_p67_reference_024 | children | culture=norgard, location=treskal, role=farmer, sex=male | known |
| portrait_p67_reference_025 | children | culture=norgard, location=treskal, role=farmer, sex=male | known |
| portrait_p67_reference_026 | children | culture=norgard, location=treskal, role=village_child, sex=male | known |
| portrait_p67_reference_027 | children | culture=norgard, location=treskal, role=village_child, sex=female | known |
| portrait_p67_reference_028 | children | culture=norgard, location=treskal, role=village_child, sex=female | known |
| portrait_p67_reference_029 | children | culture=norgard, location=treskal, role=village_child, sex=female | known |
| portrait_p67_reference_030 | elder | culture=norgard, location=treskal, role=elder, sex=male | incompatible_role |
| portrait_p67_reference_031 | elder | culture=norgard, location=treskal, role=village_elder, sex=female | known |
| portrait_p67_reference_032 | elder | culture=norgard, location=treskal, role=village_elder, sex=male | known |
| portrait_p67_reference_033 | elder | culture=norgard, location=treskal, role=village_elder, sex=female | known |
| portrait_p67_reference_034 | elder | culture=norgard, location=treskal, role=village_elder, sex=male | known |
| portrait_p67_reference_035 | elder | culture=norgard, location=treskal, role=village_elder, sex=female | known |
| portrait_p67_reference_036 | fisher | culture=norgard, location=treskal, role=fisher, sex=male | known |
| portrait_p67_reference_037 | fisher | culture=norgard, location=treskal, role=fisher, sex=female | known |
| portrait_p67_reference_038 | tavernkeeper | culture=norgard, location=treskal, role=tavernkeeper, sex=male | known |
| portrait_p67_reference_039 | tavernkeeper | culture=norgard, location=treskal, role=tavernkeeper, sex=female | known |

La pareja con `identity_key=synthetic_duplicate_pair` demuestra una advertencia de identidad compartida; no implica duplicación de imágenes ni se ha calculado un hash de originales. Los demás campos individuales proceden exclusivamente del fixture versionado en `src/NAP.Core/Fixtures/TreskalSyntheticInventory.json`.
