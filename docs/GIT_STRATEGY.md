# Estrategia Git de NAP

## Rama principal

`main` es la rama principal de NAP y debe mantenerse estable.

## Commits y push

- Se prefieren commits pequeños, coherentes y fáciles de revisar.
- Se hará push frecuente para mantener el repositorio actualizado.
- No se crearán ramas para cada cambio pequeño.

## Ramas de trabajo

Se crearán ramas para tareas grandes, experimentales, arriesgadas o con cambios transversales. Los nombres recomendados pueden utilizar estos prefijos:

- `feature/`
- `fix/`
- `refactor/`
- `experiment/`

Antes de integrar cambios importantes, la solución debe compilar correctamente y superar todos sus tests.

Más adelante se utilizarán tags y GitHub Releases para las versiones publicadas.

Nunca deben versionarse API keys, tokens, credenciales ni otros secretos.
