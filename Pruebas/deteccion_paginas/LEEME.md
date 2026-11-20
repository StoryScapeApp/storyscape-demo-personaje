# Prueba · Detección de páginas

> **Versión anterior** (sin seguimiento del libro, ArUco enviados cada 0,5 s). La prueba actual está en [`../deteccion_paginas_seguimiento/LEEME.md`](../deteccion_paginas_seguimiento/LEEME.md). Los resultados de esta carpeta (sesión del 16/11) se conservan como referencia.

Mide si el sistema reconoce la doble página correcta en condiciones de aula. Diseño: 3 luces × 3 posiciones × 2 repeticiones = **18 videos**, pasando las 7 dobles páginas en orden (unos 5 s cada una).

## Antes de empezar
- Montaje definitivo: cámara en su posición, proyector a la altura final, libro con sus 7 marcadores.
- Cierra `aruco_sender.py` (usa la cámara). El grabador lo reemplaza: envía los ArUco a Unity igual que el sistema.

## Grabar
1. Abre el grabador:
   ```
   cd python
   python grabar_deteccion.py grabar --cam 0
   ```
   Usa la misma resolución que el sistema. Si el sistema la cambia, pásala con `--ancho --alto --fps`.
2. En Unity, escena *Onboarding*: **activa el objeto `PruebaDeteccion`** y da **Play**. Haz clic en la vista Game para que reciba las teclas.
   - El proyector repite el onboarding y el vuelo del Principito: es el contenido real que ilumina el libro.
   - La lectura en vivo (Scribe), el quiz y las dudas quedan apagados: no gastan créditos ni usan el micrófono.
3. Para cada video, siguiendo el nombre que muestra el panel:

   | Tecla | Acción |
   |---|---|
   | **N** | Empieza el video. Antes, abre el libro en la **página 1** |
   | **P** | Empieza a pasar la hoja |
   | **Espacio** | La nueva doble página quedó a la vista |
   | **F** | Fin del video (después de la página 7) |
   | **R** | Descartar y repetir el video |
   | **← / →** | Elegir otro video |
   | **H** | Ocultar o mostrar el panel |

4. Al terminar, sal de Play y **desactiva `PruebaDeteccion`**.

## Resultados (en esta carpeta)
| Archivo | Contenido |
|---|---|
| `etiquetas_<sesion>.csv` | `video,inicio_s,fin_s,pagina_esperada`, con tramos de página y de `transicion` |
| `resumen_<sesion>.csv` | Lo que hizo el sistema en vivo, por tramo: detectada, latencia, % del tiempo con la página correcta y confirmaciones incorrectas |
| `eventos_<sesion>.csv` | Cada mensaje ArUco, cada confirmación (2 en 1,5 s) y cada cambio de página, con su tiempo |
| `videos/` | Los videos, más `<video>.cuadros.csv` con el tiempo real de cada cuadro |

Los tiempos se cuentan en segundos desde el primer cuadro del video.

## Analizar cuadro por cuadro
```
python grabar_deteccion.py analizar
```
Usa el `etiquetas_*.csv` más reciente y genera `analisis_<sesion>.csv`. Por tramo calcula el % de cuadros con el marcador correcto y la detección con el mismo criterio del sistema. Al final muestra la tasa por condición y las confirmaciones incorrectas durante las transiciones.
