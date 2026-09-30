# NAP — Master Specification

**Proyecto:** NAP\
**Significado funcional:** Nimroel Asset Pipeline\
**Repositorio:** `robdor80/NAP`\
**Estado actual verificado:** la Fase 0 (capítulos 0.1–0.5) está implementada en el repositorio; la próxima fase es Fase 1, Capítulo 1.1 — Inbox.\
**Plataforma principal:** Windows 11, C# / .NET 8\
**Equipo objetivo principal:** MSI Raider GE78 HX 14V\
**Propósito de este documento:** trasladar a una nueva conversación
todas las decisiones, ideas y requisitos acordados para continuar el
diseño y desarrollo sin depender de conversaciones anteriores.


## Índice

- [Qué es NAP](#1-qué-es-nap)
- [Principios fundamentales](#2-principios-fundamentales)
- [Flujo de un asset](#3-flujo-de-un-asset)
- [Manifest y Visual Identity](#5-manifest)
- [Conversión y routing](#7-conversión-de-imágenes)
- [TeraBox e integridad](#9-terabox)
- [SQLite](#11-sqlite----requisito-fundamental)
- [Catálogo, estadísticas y objetivos](#15-catálogo-visual)
- [RoDo e IA](#22-rodo)
- [RobStyle UI y Companion](#32-ui----robstyle-ui)
- [Git y arquitectura](#46-git-del-propio-nap)
- [Estado actual y hoja de ruta](#55-hoja-de-ruta-acordada)
- [Decisiones críticas](#58-decisiones-que-no-deben-olvidarse)

------------------------------------------------------------------------

# 1. Qué es NAP

NAP será la aplicación de escritorio encargada de automatizar y
gestionar la producción masiva de assets visuales del videojuego
**Nimroel RPG**.

La necesidad nace de que Nimroel necesitará potencialmente **miles de
assets**, especialmente:

-   portraits de NPC;
-   scenes;
-   heráldica;
-   objetos;
-   entornos;
-   futuras categorías visuales.

El flujo actual es demasiado manual para escalar.

La idea fundamental es:

``` text
ChatGPT genera asset
        ↓
ZIP normalizado
        ↓
NAP
        ↓
validación
        ↓
plan de operaciones
        ↓
auditoría IA
        ↓
PNG maestro → TeraBox
        ↓
PNG → WebP de producción
        ↓
WebP + documentación → repo Nimroel
        ↓
actualización SQLite
        ↓
objetivos / estadísticas / RoDo
```

NAP debe convertirse en la **fuente operativa de verdad de la producción
visual**, aunque los assets y sus metadatos individuales sigan siendo
reconstruibles sin depender exclusivamente de su base de datos.

------------------------------------------------------------------------

# 2. Principios fundamentales

## 2.1 Seguridad antes que automatización

NAP nunca debe empezar siendo un programa que mueve archivos
alegremente.

Orden conceptual:

1.  recibir;
2.  leer;
3.  validar;
4.  calcular;
5.  mostrar qué piensa hacer;
6.  auditar;
7.  ejecutar;
8.  verificar;
9.  registrar.

Primero se desarrollará un **Dry Run / PLAN** completo que no modifique
nada.

------------------------------------------------------------------------

## 2.2 Nunca destruir silenciosamente

NAP no debe:

-   sobrescribir maestros diferentes;
-   recortar imágenes silenciosamente;
-   resolver colisiones por su cuenta;
-   borrar archivos importantes;
-   asumir rutas dudosas;
-   continuar después de una validación crítica fallida.

Ante duda importante:

``` text
STOP
```

y explicación clara al usuario.

------------------------------------------------------------------------

## 2.3 Los maestros son sagrados

El PNG original generado/aprobado será el **maestro**.

El maestro se conservará en TeraBox.

El repositorio de producción utilizará normalmente WebP optimizado.

------------------------------------------------------------------------

## 2.4 Todo debe poder reconstruirse

SQLite será fundamental, pero **no debe ser el único lugar donde exista
la información**.

Cada asset tendrá metadatos individuales suficientes para reconstruir el
catálogo.

Arquitectura conceptual:

``` text
asset individual
├── imagen producción
├── prompt
├── info
├── manifest
└── visual_identity
        ↓
fuentes persistentes

SQLite
        ↓
índice operativo reconstruible
```

------------------------------------------------------------------------

# 3. Flujo de un asset

El flujo deseado final es aproximadamente:

``` text
1. ChatGPT crea imagen.
2. Usuario aprueba imagen.
3. ChatGPT prepara ZIP.
4. Usuario descarga ZIP.
5. Usuario lo deposita en Inbox de NAP.
6. NAP detecta paquete.
7. NAP espera a que esté completamente copiado.
8. NAP copia a staging.
9. NAP extrae de forma segura.
10. NAP valida contenido.
11. NAP identifica asset.
12. NAP determina destino.
13. NAP genera ProcessingPlan.
14. Gemini audita el plan.
15. Si PASS:
       PNG maestro → TeraBox
       PNG → WebP producción
       documentación → repo
16. NAP verifica hashes y destinos.
17. NAP actualiza SQLite.
18. NAP actualiza objetivos/estadísticas.
19. NAP registra operación.
20. Asset → COMPLETED.
```

------------------------------------------------------------------------

# 4. Contenido previsto del ZIP

A partir de la normalización definitiva, los ZIP nuevos deberían
contener como mínimo algo equivalente a:

``` text
<asset_id>.png
<asset_id>_prompt.md
<asset_id>_info.md
<asset_id>_manifest.json
<asset_id>_visual_identity.json    # portraits y categorías donde aplique
```

Podrán añadirse otros archivos cuando una categoría lo necesite.

Todos los archivos deben compartir un `asset_id` coherente.

------------------------------------------------------------------------

# 5. Manifest

El `manifest.json` representa la identidad administrativa/estructural
del asset.

Ejemplo conceptual:

``` json
{
  "asset_id": "portrait_treskal_farmer_male_040",
  "asset_type": "portrait",
  "culture": "Norgard",
  "location": "Treskal",
  "role": "farmer",
  "sex": "male",
  "apparent_age": 38,
  "production_profile": "portrait_npc"
}
```

No debe confundirse con `visual_identity.json`.

------------------------------------------------------------------------

# 6. Visual Identity

Se ha decidido crear para cada portrait:

``` text
<asset_id>_visual_identity.json
```

Su función es describir **lo que realmente aparece visualmente en la
imagen final**, no simplemente lo solicitado en el prompt.

Debe permitir alimentar SQLite con rasgos físicos normalizados.

Campos previstos, entre otros:

-   etapa vital;
-   edad aparente;
-   banda de edad;
-   complexión;
-   forma facial;
-   anchura facial;
-   mandíbula;
-   pómulos;
-   nariz;
-   color de ojos;
-   color de pelo;
-   textura;
-   longitud;
-   pérdida de cabello;
-   canas;
-   presencia de barba/bigote;
-   tipo de vello facial;
-   color del vello facial;
-   tono de piel;
-   desgaste/exposición;
-   rasgos visibles distintivos.

Confianza:

``` text
high
medium
doubtful
not_visible
not_applicable
```

Especialmente para ojos y rasgos pequeños debe evitarse inventar.

Los campos `doubtful` deberán poder revisarse posteriormente con el PNG
maestro.

## Estado de los 39 portraits iniciales

Actualmente existen 39 portraits históricos de Treskal.

Otra conversación ha creado 39 `*_visual_identity.json`.

Se está realizando una auditoría independiente del lote antes de
considerarlo fuente fiable para SQLite.

**No asumir que esa auditoría está terminada al continuar el desarrollo.**

------------------------------------------------------------------------

# 7. Conversión de imágenes

## Portrait

Perfil previsto actualmente:

``` text
entrada:
PNG maestro

salida:
WebP
768 × 960
relación 4:5
Q90
```

No debe haber recorte silencioso.

## Scenes

Se definirá un perfil específico posteriormente.

NAP debe terminar utilizando un sistema genérico de perfiles:

``` text
portrait
scene
heraldry
object
environment
...
```

------------------------------------------------------------------------

# 8. Destinos y routing

Uno de los problemas importantes es determinar automáticamente dónde va
cada asset.

Ejemplos:

``` text
Norgard
└── Treskal
    └── farmer
        └── male
```

pero posteriormente existirán:

-   Hallheim;
-   Darovan;
-   otras localizaciones;
-   Belendril;
-   elfos;
-   otras culturas/reinos;
-   nuevas profesiones;
-   nuevas categorías.

NAP no debe tener toda la estructura codificada rígidamente para
siempre.

Debe:

1.  conocer la configuración de routing;
2.  leer la estructura real del repositorio local;
3.  detectar cambios;
4.  detectar nuevas carpetas;
5.  distinguir cambios normales de cambios estructurales;
6.  avisar cuando una modificación pueda afectar a las reglas.

Ejemplo conceptual:

``` text
portrait
→ portraits/{culture}/{location}/{role}/{sex}/{asset_id}
```

------------------------------------------------------------------------

# 9. TeraBox

TeraBox está instalado en Windows y se comporta como una carpeta/unidad
accesible localmente.

Se utilizará principalmente para **conservación**.

Actualmente existen dos conceptos distintos:

1.  copia del repositorio;
2.  archivo de maestros PNG y sus datos.

NAP deberá automatizar la incorporación incremental.

Si hoy se archivan 5 assets y mañana aparecen 2 nuevos:

``` text
NO volver a copiar 7
SÍ detectar y copiar sólo los 2 nuevos
```

Para ello se usarán:

-   Asset ID;
-   hashes;
-   índice;
-   SQLite;
-   verificación origen/destino.

Nunca sobrescribir silenciosamente un maestro con otro distinto.

------------------------------------------------------------------------

# 10. Índice de maestros

Debe existir un registro de los maestros archivados.

Datos posibles:

-   Asset ID;
-   hash SHA-256;
-   ruta;
-   fecha;
-   tamaño;
-   tipo;
-   estado de verificación.

La copia a TeraBox debe terminar con:

``` text
hash origen == hash destino
```

------------------------------------------------------------------------

# 11. SQLite --- requisito fundamental

**SQLite es una función obligatoria de NAP. No es opcional.**

Nombre provisional:

``` text
NimroelAssetCatalog.db
```

La DB activa debe residir en disco local normal, no directamente dentro
de una carpeta sincronizada por TeraBox/Dropbox.

Objetivos de SQLite:

-   gestionar miles de assets;
-   búsquedas instantáneas;
-   filtros;
-   estadísticas;
-   diversidad;
-   objetivos;
-   cobertura;
-   historial;
-   soporte para RoDo;
-   soporte futuro para Android.

------------------------------------------------------------------------

# 12. Información prevista en SQLite

El esquema definitivo se diseñará durante el desarrollo, pero
conceptualmente debe representar:

## Assets

-   Asset ID;
-   categoría;
-   cultura;
-   reino/región;
-   lugar;
-   profesión/rol;
-   sexo;
-   edad aparente;
-   etapa vital;
-   banda de edad;
-   complexión;
-   rasgos visuales;
-   estado;
-   fecha;
-   hash maestro;
-   hash producción;
-   ruta maestro;
-   ruta producción;
-   prompt;
-   manifest;
-   identidad visual;
-   estado auditoría.

## Rasgos visuales

Especialmente para portraits:

-   ojos;
-   pelo;
-   barba;
-   calvicie;
-   canas;
-   complexión;
-   edad;
-   etc.

Los vocabularios deben estar **normalizados** para que SQLite no termine
con sinónimos incompatibles.

------------------------------------------------------------------------

# 13. Reconstrucción de SQLite

Debe existir una función que permita reconstruir la DB desde los assets.

Si:

``` text
NimroelAssetCatalog.db
```

se pierde o corrompe, NAP debe poder recorrer:

``` text
manifest.json
visual_identity.json
otros metadatos
```

y regenerar el catálogo.

Esto debe probarse deliberadamente durante el desarrollo.

------------------------------------------------------------------------

# 14. Backups de SQLite

Además de poder reconstruirla, se quieren backups históricos.

Arquitectura:

``` text
NAP local
└── state/
    └── NimroelAssetCatalog.db
             ↓
        snapshot seguro
             ↓
        verificación
             ↓
          TeraBox
```

Destino conceptual:

``` text
TeraBox/
└── NAP_DATABASE_BACKUPS/
    ├── NimroelAssetCatalog_YYYY-MM-DD_HHMM.db
    └── ...
```

Opciones futuras:

-   diario;
-   semanal;
-   mensual;
-   personalizado;
-   política de retención;
-   backup extraordinario antes de operaciones grandes.

TeraBox es el destino principal elegido para los históricos de la DB.

------------------------------------------------------------------------

# 15. Catálogo visual

NAP no será sólo pipeline.

Debe tener un **explorador visual de assets**.

Ejemplo:

``` text
CATÁLOGO

Tipo
Cultura
Lugar
Profesión
Sexo
Edad
Pelo
Ojos
Barba
...
```

Resultado:

``` text
[miniatura] Farmer 001
[miniatura] Farmer 004
[miniatura] Farmer 008
...
```

Al pulsar un asset:

-   ficha completa;
-   miniatura;
-   metadatos;
-   rutas;
-   hashes;
-   prompt;
-   estado;
-   maestro;
-   producción;
-   auditorías.

------------------------------------------------------------------------

# 16. Miniaturas

No cargar miles de PNG maestros.

NAP debe generar/usar thumbnails ligeros y caché.

La UI sólo debe cargar los elementos visibles durante scroll.

Esto debe permitir que el catálogo siga siendo fluido incluso con miles
o decenas de miles de registros.

------------------------------------------------------------------------

# 17. Estadísticas y diversidad

SQLite permitirá responder preguntas como:

``` text
¿Cuántos farmer male de Hallheim hay?
¿Cuántas campesinas de Treskal de 20–30 años?
¿Cuántos NPC tienen barba?
¿Cuántos son calvos?
¿Cuántos tienen pelo claro?
¿Cómo se distribuyen las edades?
```

Pero también permitirá detectar **falsa diversidad**.

Ejemplo:

``` text
Farmer male Treskal = 10/10
```

puede parecer completo, pero:

``` text
7 tienen 30–40 años
8 tienen barba
7 son robustos
8 tienen pelo oscuro
```

NAP/RoDo debe poder señalar que el objetivo numérico está cumplido pero
la diversidad visual es pobre.

------------------------------------------------------------------------

# 18. Objetivos de producción

Función obligatoria del catálogo.

Ejemplo:

``` text
Hallheim
farmer
male
objetivo = 10
```

NAP calcula:

``` text
3 / 10
```

El contador **no se incrementa manualmente**.

SQLite consulta cuántos assets válidos cumplen los criterios.

Cuando llega a:

``` text
10 / 10
```

se marca:

``` text
OBJETIVO CUMPLIDO
```

------------------------------------------------------------------------

# 19. Objetivos complejos

Los objetivos podrán usar filtros:

``` text
Treskal
farmer
female
20–30 años
objetivo = 8
```

También podrán existir para:

-   scenes;
-   profesiones;
-   culturas;
-   edades;
-   sexo;
-   otros atributos.

Un mismo asset puede contribuir a varios objetivos simultáneamente.

------------------------------------------------------------------------

# 20. Campañas de objetivos

Agrupar objetivos en campañas.

Ejemplo:

``` text
CAMPAÑA:
Población base de Treskal

Farmer male        10
Farmer female      10
Fisher male         8
Fisher female       8
Blacksmith male     6
Blacksmith female   6
...
```

Progreso:

``` text
64 / 100
64 %
```

Al completarse:

``` text
CAMPAÑA COMPLETADA
```

------------------------------------------------------------------------

# 21. Concepto de cobertura

NAP debe distinguir:

``` text
Explorar
→ ¿qué tenemos?

Estadísticas
→ ¿cómo está distribuido?

Objetivos
→ ¿cuánto nos falta?

Diversidad/Cobertura
→ ¿lo que tenemos está equilibrado?
```

------------------------------------------------------------------------

# 22. RoDo

**RoDo debe formar parte oficial de NAP.**

RoDo es el asistente transversal de las aplicaciones del usuario.

En NAP no será sólo decoración.

Funciones previstas:

-   consultas naturales del catálogo;
-   explicación de estados;
-   resumen de cobertura;
-   ayuda con errores;
-   planificación de sesiones;
-   avisos;
-   análisis de evolución;
-   interpretación de estadísticas.

Ejemplos:

``` text
RoDo, ¿cuántas campesinas de Treskal entre 20 y 30 años tengo?
```

o:

``` text
RoDo, ¿qué objetivos están más retrasados?
```

------------------------------------------------------------------------

# 23. RoDo proactivo

Se desea que RoDo pueda avisar **sin que el usuario tenga que
preguntarle**, cuando exista algo relevante.

Ejemplos:

-   objetivo completado;
-   cobertura muy baja;
-   exceso de una profesión;
-   desequilibrio entre variantes;
-   varias sesiones creando casi lo mismo;
-   error repetitivo;
-   maestros sin verificar;
-   objetivos estancados;
-   diversidad insuficiente.

Ejemplo:

> Llevas 14 nuevos campesinos de Treskal en las últimas sesiones y sólo
> 2 herreros. Los objetivos de campesinos están prácticamente completos;
> herrería sigue muy por debajo.

La IA no debe contar manualmente.

**NAP/SQLite calcula los hechos. RoDo los interpreta.**

------------------------------------------------------------------------

# 24. IA ligera para supervisión cotidiana

Idea prevista:

Cada cierto número de assets o al terminar una sesión, NAP genera un
resumen pequeño:

``` text
Sesión:
8 assets nuevos

Treskal:
+ farmer male: 3
+ farmer female: 2
+ fisher female: 3

Objetivos:
farmer male: 8/10 → 10/10
fisher female: 2/10 → 5/10

Errores: 0
Duplicados: 0
```

Ese resumen se manda al modelo ligero configurado para RoDo.

Normalmente no debe molestar.

Sólo avisar si encuentra algo útil.

------------------------------------------------------------------------

# 25. Auditoría general periódica con IA potente

Además del seguimiento ligero se desea una **gran auditoría
periódica/manual**.

NAP preparará un informe estructurado con:

-   catálogo completo/resumen;
-   distribución;
-   culturas;
-   lugares;
-   profesiones;
-   sexos;
-   edades;
-   atributos;
-   objetivos;
-   cobertura;
-   últimas sesiones;
-   integridad;
-   maestros;
-   hashes;
-   errores;
-   evolución.

Ese paquete podrá enviarse a un modelo más potente disponible en ese
momento.

No fijar de forma rígida nombres/versiones de modelos futuros.

La arquitectura debe permitir cambiar proveedor/modelo mediante
configuración.

------------------------------------------------------------------------

# 26. Gemini auditor de operaciones

Separar responsabilidades:

``` text
Gemini Auditor
→ ¿es segura/coherente ESTA operación?

RoDo
→ ¿cómo evoluciona la producción?

Auditoría General
→ ¿cómo está el catálogo global?
```

Aunque técnicamente puedan usar el mismo proveedor, conceptualmente son
módulos diferentes.

------------------------------------------------------------------------

# 27. La IA no debe controlar directamente los archivos

No dar acceso irrestricto a TeraBox/filesystem sólo porque sea cómodo.

Preferencia:

``` text
TeraBox ─┐
Repo ────┤
SQLite ──┤
         ↓
        NAP
         ↓
 informe estructurado
         ↓
       IA
```

NAP mantiene autoridad sobre operaciones.

La IA:

-   analiza;
-   recomienda;
-   aprueba/rechaza según contrato;
-   no borra;
-   no mueve;
-   no hace Git directamente dentro del flujo crítico.

------------------------------------------------------------------------

# 28. ProcessingPlan / Dry Run

Antes de ejecutar, NAP construye un plan completo.

Ejemplo:

``` text
Asset:
portrait_treskal_farmer_male_040

MASTER:
Copiar PNG a:
TeraBox\...

PRODUCTION:
PNG → WebP
768x960
Q90

DESTINATION:
...\portraits\Norgard\Treskal\farmer\male\...

FILES:
WebP
prompt.md
info.md
manifest.json
visual_identity.json

No se ha modificado ningún archivo.
```

Este modo debe desarrollarse y probarse antes de habilitar escritura
real.

------------------------------------------------------------------------

# 29. Hashes y duplicados

Usar SHA-256.

Casos:

``` text
mismo Asset ID + mismo hash
→ ya procesado / idempotente

mismo Asset ID + distinto hash
→ COLISIÓN / STOP

distinto Asset ID + mismo PNG
→ posible duplicado / revisar
```

También verificar copias después de escribir.

------------------------------------------------------------------------

# 30. Jobs y estados persistentes

Cada proceso debe tener un Job ID.

Estados conceptuales:

``` text
DETECTED
STAGED
VALIDATED
PLANNED
AUDITED
EXECUTED
VERIFIED
COMPLETED
FAILED
```

NAP debe saber recuperarse tras:

-   apagado;
-   cierre;
-   excepción;
-   bloqueo;
-   pérdida temporal de TeraBox;
-   operación incompleta.

------------------------------------------------------------------------

# 31. Logs

Logs útiles pero no invasivos.

La UI debe mostrar información humana.

Los detalles técnicos podrán incluir:

-   rutas;
-   hashes;
-   tiempos;
-   excepciones;
-   resultado IA;
-   archivos;
-   operaciones.

Los logs no deben exponer secretos.

------------------------------------------------------------------------

# 32. UI --- RobStyle UI

La identidad visual oficial se llamará:

# **RobStyle UI**

NAP debe sentirse perteneciente a la misma familia visual que RobGit.

Características:

-   fondo azul petróleo/cerúleo;
-   blanco hielo;
-   paneles/tarjetas;
-   profundidad 3D sutil;
-   diseño limpio;
-   estados claros;
-   información técnica escondida salvo que se solicite;
-   sensación profesional;
-   interfaz agradable y bonita, no sólo funcional.

------------------------------------------------------------------------

# 33. NAP no será una ventanita pequeña

NAP se diseñará para aprovechar el MSI.

Debe:

-   arrancar maximizado o ser naturalmente usable a pantalla completa;
-   aprovechar grandes resoluciones;
-   mostrar catálogo y miniaturas cómodamente;
-   utilizar espacio disponible;
-   ser visualmente atractiva.

El hecho de que la lógica sea sencilla en algunos módulos **no justifica
una UI pequeña o pobre**.

------------------------------------------------------------------------

# 34. Tecnología UI

Candidata principal:

``` text
WPF + MVVM
```

Objetivo:

-   separar UI del motor;
-   componentes RobStyle reutilizables;
-   aplicación Windows profesional;
-   async para no congelar interfaz;
-   virtualización;
-   cachés;
-   rendimiento.

La decisión se confirmará técnicamente durante el desarrollo.

------------------------------------------------------------------------

# 35. Shell visual temprano

Aunque el núcleo se desarrollará primero, no esperar hasta el final para
ver NAP como aplicación.

Una vez estabilizada la base, crear un shell RobStyle con:

``` text
Inicio
Pipeline
Catálogo
Objetivos
Estadísticas
Backups
Historial
RoDo
Ajustes
```

Las secciones se irán llenando progresivamente.

------------------------------------------------------------------------

# 36. Dashboard

Ideas:

-   assets totales;
-   maestros verificados;
-   assets producción;
-   pendientes Inbox;
-   errores;
-   objetivos activos;
-   último backup;
-   estado repo;
-   estado TeraBox;
-   estado IA;
-   actividad reciente;
-   cobertura.

No llenar de métricas inútiles sólo porque hay espacio.

------------------------------------------------------------------------

# 37. Pantalla Pipeline

Mostrar claramente el proceso:

``` text
✓ Paquete recibido
✓ Validación
✓ Hash maestro
✓ Destino calculado
● Auditoría IA
○ Archivo TeraBox
○ Conversión WebP
○ Producción
○ Catálogo
```

Botones peligrosos bloqueados mientras corresponda.

------------------------------------------------------------------------

# 38. Pantalla Catálogo

Filtros amplios + cuadrícula visual.

Filtros potenciales:

-   tipo;
-   cultura;
-   lugar;
-   profesión;
-   sexo;
-   edad;
-   pelo;
-   ojos;
-   barba;
-   complexión;
-   estado;
-   objetivo/campaña.

------------------------------------------------------------------------

# 39. Pantalla Objetivos

Debe ser visual:

``` text
Farmer male       10/10 ✓
Farmer female      8/10
Fisher male        7/10
Fisher female      2/10
```

Al pulsar un contador se muestran los assets que lo componen.

------------------------------------------------------------------------

# 40. Pantalla Estadísticas

Distribuciones útiles:

-   por cultura;
-   lugar;
-   profesión;
-   sexo;
-   edad;
-   rasgos;
-   evolución temporal;
-   cobertura;
-   diversidad.

------------------------------------------------------------------------

# 41. RoDo dentro de la UI

RoDo debe tener presencia integrada en RobStyle UI.

No debe llenar la interfaz de mascotas repetidas.

Debe poder:

-   mostrar mensajes contextuales;
-   abrir panel conversacional;
-   explicar errores;
-   consultar catálogo;
-   señalar cobertura;
-   preparar sesiones.

Tono cercano, pero serio ante riesgos.

------------------------------------------------------------------------

# 42. NAP Companion --- Android

Idea futura muy importante.

Se desea poder consultar el catálogo desde Android cuando el usuario no
está en el MSI, por ejemplo en el gimnasio.

NO hacer que Android abra/escriba directamente la SQLite activa del PC.

Arquitectura preferida:

``` text
NAP Windows
   ↓
SQLite maestra
   ↓
exportación/snapshot móvil
   ↓
sincronización
   ↓
NAP Companion Android
```

------------------------------------------------------------------------

# 43. Funciones del Companion

Inicialmente principalmente lectura:

-   buscar assets;
-   filtros;
-   miniaturas;
-   estadísticas;
-   objetivos;
-   cobertura;
-   planificación de sesiones;
-   posiblemente RoDo.

Ejemplo:

``` text
Treskal
Fisher
Female
20–30

→ 6 assets
```

------------------------------------------------------------------------

# 44. Planificación de sesiones desde Android

Idea:

``` text
PRÓXIMA SESIÓN

Treskal · Fisher · Female ×4
Treskal · Blacksmith · Female ×3
Hallheim · Farmer · Elderly Male ×2
```

Más adelante esa planificación podría sincronizarse con NAP Windows.

------------------------------------------------------------------------

# 45. Caché móvil

No sincronizar PNG maestros.

Generar thumbnails pequeños, por ejemplo WebP optimizados, junto con
catálogo móvil.

Concepto:

``` text
mobile-cache/
├── thumbnails/
└── catalog.json
```

o una SQLite secundaria de sólo lectura.

La implementación exacta se decidirá más adelante.

------------------------------------------------------------------------

# 46. Git del propio NAP

Repositorio:

``` text
robdor80/NAP
```

Público.

Rama principal:

``` text
main
```

`main` debe mantenerse estable.

Estrategia:

-   commits pequeños;
-   push frecuente;
-   ramas para tareas grandes, experimentales o arriesgadas;
-   tags para versiones;
-   GitHub Releases para versiones publicadas.

El usuario trabaja normalmente con GitHub Desktop y agradece
instrucciones explícitas de cuándo hacer commit/push/rama y el Summary
del commit.

------------------------------------------------------------------------

# 47. Seguridad del repo público

Nunca subir:

-   API keys;
-   tokens;
-   secretos;
-   credenciales;
-   rutas privadas innecesarias;
-   configuraciones personales sensibles.

Usar:

-   variables de entorno;
-   User Secrets;
-   configuración local ignorada;
-   archivos `.example`.

------------------------------------------------------------------------

# 48. Estructura inicial prevista

``` text
NAP/
├── src/
│   ├── NAP.Core/
│   └── NAP.Cli/
├── tests/
│   └── NAP.Tests/
├── docs/
├── installer/
├── test-data/
├── README.md
├── CHANGELOG.md
└── .gitignore
```

Posteriormente se añadirá la aplicación WPF.

------------------------------------------------------------------------

# 49. Codex

Codex será el programador principal.

Forma de trabajo:

``` text
Usuario + ChatGPT
→ dirección / arquitectura / revisión

Codex
→ implementación / tests / refactor
```

No pedir:

> crea NAP entero.

Dar tareas pequeñas y verificables.

Cada capítulo debe:

1.  implementarse;
2.  compilar;
3.  probarse;
4.  revisarse;
5.  commit;
6.  push;
7.  pasar al siguiente.

------------------------------------------------------------------------

# 50. Prioridad respecto al videojuego

NAP consumirá cuota/tiempo de Codex que también se necesita para el RPG.

Por ello NO se pretende terminar todo NAP antes de continuar el juego.

Prioridad inicial:

## NAP mínimo realmente útil

``` text
ZIP
↓
validación
↓
plan
↓
auditoría
↓
PNG → TeraBox
↓
WebP → repo
↓
SQLite
```

más una UI Windows sencilla pero útil.

Una vez logrado:

-   bajar prioridad de NAP;
-   volver a concentrar Codex en el RPG;
-   continuar generando assets en paralelo;
-   evolucionar NAP progresivamente.

------------------------------------------------------------------------

# 51. Objetivo estratégico

Mientras Codex desarrolla el videojuego en el MSI:

``` text
ChatGPT
→ puede seguir creando assets

NAP
→ puede gestionarlos automáticamente
```

De esta forma, cuando el videojuego llegue a fases donde necesite
grandes cantidades de contenido visual, ya existirá una biblioteca
grande y correctamente organizada.

------------------------------------------------------------------------

# 52. Instalación profesional

NAP debe terminar siendo una aplicación instalable como producto real.

Objetivo:

``` text
NAP-Setup-1.0.0.exe
```

Candidato inicial para instalador:

``` text
Inno Setup
```

La aplicación se publicará Windows x64 autocontenida cuando corresponda.

Debe incluir:

-   icono;
-   versión;
-   menú Inicio;
-   acceso directo opcional;
-   desinstalador;
-   información de versión.

------------------------------------------------------------------------

# 53. GitHub Releases

No meter todos los instaladores dentro del historial normal Git.

Usar GitHub Releases:

``` text
NAP v1.0.0
├── NAP-Setup-1.0.0.exe
├── SHA256SUMS.txt
└── Release Notes
```

------------------------------------------------------------------------

# 54. Backup de releases

Además de GitHub Releases:

``` text
TeraBox/
└── NAP/
    └── RELEASES/
        └── 1.0.0/
            ├── NAP-Setup-1.0.0.exe
            └── SHA256SUMS.txt
```

Si se formatea el MSI:

1.  descargar última release;
2.  instalar NAP;
3.  restaurar/configurar rutas;
4.  reconstruir/recuperar estado si fuese necesario.

------------------------------------------------------------------------

## Estado actual del desarrollo

La Fase 0.1 (repositorio), 0.2 (solución .NET 8), 0.3 (primer ejecutable), 0.4 (tests) y 0.5 (estrategia Git + CI) están presentes en el repositorio actual. El siguiente trabajo previsto es **FASE 1 — Entrada y paquetes, Capítulo 1.1 — Inbox**. No se afirma que fases posteriores estén implementadas.

# 55. Hoja de ruta acordada

## FASE 0 --- Fundación

### 0.1

Crear repo `NAP`.\
**HECHO.**

### 0.2

Crear solución .NET 8.  
**HECHO.**

### 0.3

Primer ejecutable.  
**HECHO.**

### 0.4

Tests.  
**HECHO.**

### 0.5

Estrategia Git + CI.  
**HECHO.**

------------------------------------------------------------------------

## FASE 1 --- Entrada y paquetes

### 1.1

Inbox.

### 1.2

Detección.

### 1.3

Comprobar archivo completamente copiado.

### 1.4

Staging.

### 1.5

Extracción ZIP segura.

### 1.6

Primer paquete de prueba.

------------------------------------------------------------------------

## FASE 2 --- Manifest y validación

### 2.1

Diseñar manifest definitivo.

### 2.2

Modelo C#.

### 2.3

Naming.

### 2.4

Validación PNG.

### 2.5

Errores controlados.

### 2.6

ZIP deliberadamente incorrectos para tests.

------------------------------------------------------------------------

## FASE 3 --- Routing y repo

### 3.1

Configurar repo Nimroel.

### 3.2

Escanear estructura.

### 3.3

Routing.

### 3.4

Detectar carpetas nuevas.

### 3.5

Clasificar cambios.

### 3.6

Calcular destino.

------------------------------------------------------------------------

## FASE 4 --- PLAN / Dry Run

### 4.1

`ProcessingPlan`.

### 4.2

Dry Run.

### 4.3

Validar plan.

### 4.4

Logs.

------------------------------------------------------------------------

## FASE 5 --- Conversión

### 5.1

Portrait.

### 5.2

Validar salida.

### 5.3

Scene.

### 5.4

Perfiles genéricos.

### 5.5

No recorte silencioso.

------------------------------------------------------------------------

## FASE 6 --- Integridad

### 6.1

SHA-256.

### 6.2

Duplicados.

### 6.3

Job ID.

### 6.4

Estados.

### 6.5

Recuperación tras fallo.

------------------------------------------------------------------------

## FASE 7 --- Auditor IA

### 7.1

Cliente IA.

### 7.2

Informe estructurado.

### 7.3

PASS / WARNING / FAIL.

### 7.4

Aislamiento de permisos.

### 7.5

Pruebas en Dry Run.

------------------------------------------------------------------------

## FASE 8 --- TeraBox

### 8.1

Configurar ruta.

### 8.2

Índice maestros.

### 8.3

Copia incremental.

### 8.4

Verificación.

### 8.5

Colisiones.

### 8.6

Estructura definitiva.

------------------------------------------------------------------------

## FASE 9 --- Producción repo Nimroel

### 9.1

Copiar producción.

### 9.2

Crear carpetas permitidas.

### 9.3

Verificar.

### 9.4

Completed.

------------------------------------------------------------------------

## FASE 10 --- SQLite

### 10.1

Crear DB.

### 10.2

Esquema.

### 10.3

Importar assets existentes.

### 10.4

Actualización automática.

### 10.5

Filtros.

### 10.6

Estadísticas.

### 10.7

Reconstrucción.

**Añadido posteriormente como requisito fuerte dentro de esta fase:** -
visual identities; - cobertura; - diversidad; - objetivos; - campañas; -
soporte RoDo.

------------------------------------------------------------------------

## FASE 11 --- Explorador visual

### 11.1

Thumbnails.

### 11.2

Grid.

### 11.3

Ficha.

### 11.4

Búsqueda avanzada.

### 11.5

Estadísticas visuales.

------------------------------------------------------------------------

## FASE 12 --- Backups

### 12.1

Backup SQLite.

### 12.2

Histórico TeraBox.

### 12.3

Retención.

### 12.4

Restauración.

### 12.5

Snapshot repo.

### 12.6

Git bundle.

------------------------------------------------------------------------

## FASE 13 --- Git del repo Nimroel

### 13.1

Detectar cambios.

### 13.2

Mostrar archivos.

### 13.3

Preparar commit.

### 13.4

Commit asistido.

### 13.5

Push.

Dejar deliberadamente tarde por seguridad.

------------------------------------------------------------------------

## FASE 14 --- UI profesional

### 14.1

Ventana principal.

### 14.2

Dashboard.

### 14.3

Inbox/Pipeline.

### 14.4

Catálogo.

### 14.5

Objetivos.

### 14.6

Estadísticas.

### 14.7

Errores/Historial.

### 14.8

Ajustes.

### 14.9

Backups.

### 14.10

Bandeja sistema.

### 14.11

RoDo.

**Nota:** aunque esta fase represente el pulido completo, se quiere
introducir un shell RobStyle mucho antes.

------------------------------------------------------------------------

## FASE 15 --- Instalador y distribución

### 15.1

Publicación x64.

### 15.2

Icono.

### 15.3

Inno Setup.

### 15.4

Desinstalador.

### 15.5

GitHub Release.

### 15.6

Backup TeraBox.

------------------------------------------------------------------------

# 56. Posible revisión futura de la hoja de ruta

La hoja de ruta no es inmutable.

Especialmente:

-   SQLite se ha vuelto más importante desde que se creó la hoja
    original;
-   Objetivos y RoDo deben integrarse formalmente;
-   el shell RobStyle debería adelantarse;
-   NAP Companion se añadirá como fase posterior independiente;
-   visual identity debe formar parte del diseño de datos desde el
    principio.

Antes de programar módulos dependientes de datos conviene actualizar
formalmente la hoja de ruta.

------------------------------------------------------------------------

# 57. Próximo trabajo previsto

La especificación ya forma parte del repositorio como `docs/NAP_MASTER_SPEC.md`. La siguiente implementación es **Fase 1 · Capítulo 1.1 — Inbox**. El desarrollo debe continuar desde el estado real del repositorio, comprobándolo antes de afirmar su contenido.

# 58. Decisiones que NO deben olvidarse

1.  **SQLite sí o sí.**
2.  SQLite debe ser reconstruible.
3.  Backups históricos SQLite → TeraBox.
4.  Assets maestros PNG → TeraBox.
5.  Producción → WebP Q90 según perfil.
6.  Routing basado en metadata + estructura real del repo.
7.  Dry Run antes de escritura.
8.  IA audita; NAP ejecuta.
9.  RoDo forma parte de NAP.
10. RoDo debe poder ser proactivo.
11. Objetivos y campañas de cobertura.
12. Diversidad visual, no sólo cantidad.
13. `visual_identity.json` por portrait.
14. Vocabularios normalizados para SQLite.
15. Catálogo visual con thumbnails.
16. UI grande, bonita y profesional.
17. Identidad oficial: **RobStyle UI**.
18. Futuro **NAP Companion Android**.
19. GitHub desde el primer día.
20. Instalador profesional + Releases + copia TeraBox.
21. No paralizar durante demasiado tiempo el desarrollo del RPG.
22. Conseguir primero un NAP mínimo realmente útil y luego
    evolucionarlo.

------------------------------------------------------------------------

# 59. Visión final

NAP no debe acabar siendo simplemente:

> "un conversor de PNG a WebP".

Debe convertirse en:

> **el sistema de producción, conservación, catalogación, consulta,
> cobertura, auditoría y planificación de los assets visuales de
> Nimroel.**

La visión completa es:

``` text
                    ┌───────────────┐
                    │   ChatGPT     │
                    │ crea assets   │
                    └───────┬───────┘
                            │ ZIP
                            ▼
                    ┌───────────────┐
                    │      NAP      │
                    │   Pipeline    │
                    └───────┬───────┘
                            │
          ┌─────────────────┼──────────────────┐
          │                 │                  │
          ▼                 ▼                  ▼
       TeraBox           Repo Nimroel       SQLite
       maestros          producción         catálogo
          │                 │                  │
          │                 │          ┌───────┴────────┐
          │                 │          │                │
          │                 │          ▼                ▼
          │                 │      Objetivos         RoDo
          │                 │      Cobertura       Supervisor
          │                 │      Diversidad          │
          │                 │          │                │
          └─────────────────┴──────────┴────────────────┘
                                      │
                                      ▼
                               NAP Companion
                                  Android
```

Todo ello presentado mediante **RobStyle UI**, con seguridad,
trazabilidad, reconstrucción y backups.

------------------------------------------------------------------------

# 60. Regla permanente de continuidad

Esta especificación es la referencia estable de NAP. No se debe rediseñar desde cero sin razón técnica documentada, ni perder las prioridades de seguridad, SQLite, automatización, RobStyle UI y utilidad real. Cada capítulo debe comprobar el repositorio, implementar una tarea pequeña, compilar, probar y revisar antes de pasar al siguiente.
