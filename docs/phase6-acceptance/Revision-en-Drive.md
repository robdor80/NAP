# NAP 2.0 — P6.8 · Revisión en Drive

Código realmente verificado: `ed556bbbdf8a6971cd22d38f96ff88885a366225`. [Actions final](https://github.com/robdor80/NAP/actions/runs/39921263656): dos builds Release, 157/157 casos focalizados y 113 PNG WPF distintos. Aceptación humana del Raider y paquete privado pendientes.

- [Informe-ejecutivo.pdf](https://drive.google.com/file/d/1oroZTAWtK27YwXUHznoYTkD5cVI3869y/view?usp=drivesdk)
- [Galeria-WPF.pdf](https://drive.google.com/file/d/1wQq1B2bNDY8ZKNVKHUShwii5vfj6X8at/view?usp=drivesdk)
- [Guia-MSI.pdf](https://drive.google.com/file/d/13P8IhhzsVr7Iuv2IYZaYrXSjIwlaFoQH/view?usp=drivesdk)

- [A. Informe](https://drive.google.com/drive/folders/10l_02NmEvtharDHPbJCf9_pU6mJodvIH)
- [B. Capturas WPF](https://drive.google.com/drive/folders/10I_B5JQ5qrx5f2l3zVVAwU3TYcCOtRdV)
- [C. Pruebas y evidencias](https://drive.google.com/drive/folders/1IGhrfhl6H5nxLS9o7Gri1uSWVoGBCzAj)
- [D. Aceptación MSI](https://drive.google.com/drive/folders/1IzDw0nYL2XmhrYuetJS7EY-54UhdxG7x)
- [E. Cierre y distribución futura](https://drive.google.com/drive/folders/1SPNc4JuS-npTH1TsPHqMF5lLcq1Y5qhS)

Los PNG originales están individualmente en B. Los seis ZIP originales de Actions, TRX, logs y manifiestos están en C. Se conservan cinco ejecuciones preliminares fallidas, sin sumar sus resultados al final. SHA-256 local/artefactos originales verificado; lectura de Drive por nombre/tamaño/existencia, sin checksum remoto expuesto por el conector.

Main, #47/#49, CUTOVER y assets reales intactos. Sin PR, fusión, instalación, Release pública, web, updater ni Fase 7.

Paquete privado no preparado: workflow de build/render en repositorio público sin canal binario privado validado. Requisitos de distribución/dependencias/licencia y aislamiento se explican en la guía. Políticas sin caducidad por fecha e incidencias IPC/StorageUnavailable/ImageSharp siguen documentadas.
