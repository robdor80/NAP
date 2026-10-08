# RoDo · recurso de presentación WPF

Reutilización solicitada explícitamente por el usuario para Fase 14, tras aprobar
el RobStyle de NAP. No se usa generación de imágenes ni se altera RobGit.

- Repositorio de origen: `robdor80/RobGit_App`.
- Commit: `d6aabd9e82a4a65d0d46c03a7864cd3e1ca3c37a`.
- Asset: [`app/src/main/res/drawable-nodpi/rodo_v0_1.webp`](https://github.com/robdor80/RobGit_App/blob/d6aabd9e82a4a65d0d46c03a7864cd3e1ca3c37a/app/src/main/res/drawable-nodpi/rodo_v0_1.webp).
- Movimiento de referencia: `RodoUi.kt`, `AnimatedRodo`, estado idle.
- SHA-256 WebP original: `8b1e5360f63a9c241f8beaccd815e5d01a9f229f5074d3cfde6f318f046dc160`.
- Copia incluida: `rodo_v0_1.png`, PNG RGBA, **1024×1024**.
- SHA-256 PNG: `60aa9b8c90630e58756dcea2c0ab0fe3294afc331470f7ac41cef7a99df1e82c`.
- SHA-256 píxeles RGBA decodificados: `a5df35d85c5173dc3b7c555c4ef02a323b0034077bf58ef26a76461ea99a9954`.

Derivación: bytes de `git show <commit>:<asset>`, Pillow 12.2.0,
`Image.open(BytesIO(bytes)).convert("RGBA").save(..., format="PNG")`.
Se reabrió el PNG y se comparó **cada byte RGBA** con el WebP decodificado:
igualdad exacta, dimensiones idénticas, alfa conservado (mínimo 0, máximo 255).
Sin recorte, redimensionado, retoque, sustitución de colores ni cambio de composición.

WPF incorpora el PNG como Resource y lo decodifica con su lector PNG integrado;
no depende de un codec WebP de Windows ni añade un NuGet. El BitmapSource compartido
queda congelado. `RodoMascot` adapta la imagen al espacio disponible, con máximos de
300×300 DIP, centrada en la cabecera de la página, fuera del panel de conversación. Solo anima TranslateTransform.Y de −3 a +3 DIP,
2.1 segundos por sentido, AutoReverse, repetición y SineEase. Unloaded e IsVisibleChanged
eliminan el clock; no hay timers, procesos ni comportamiento de IA.
