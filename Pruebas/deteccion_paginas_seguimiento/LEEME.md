# Prueba · Detección de páginas (con seguimiento del libro)

Mide si el sistema reconoce la doble página correcta en condiciones de aula **y si la proyección se queda quieta sobre el libro** ahora que lo sigue (`SeguimientoLibro`).

Diseño: 3 luces × 3 posiciones × 2 repeticiones = **18 videos**. Cada video pasa las **7 dobles páginas con marcador**, en orden (ArUco 0 → 6, páginas 2 a 8 del PDF; la portada y la contraportada no tienen marcador), dejando cada una a la vista unos 5 s.

> La versión anterior (sin seguimiento, envío cada 0,5 s) quedó en `Pruebas/deteccion_paginas`. No se mezclan resultados.

## Qué cambió respecto de la versión anterior
- El sistema envía los ArUco cada **0,1 s** (antes 0,5 s) con su posición y giro. El grabador hace lo mismo, así que la prueba mide el sistema actual.
- La proyección **sigue al libro** y tapa el ArUco activo con un cuadro negro. Por eso la prueba exige que la proyección esté **calibrada** antes de grabar.
- Además de la detección de página, se registra la **estabilidad de la proyección**: cuántas veces se movió, cuánto recorrió y cuánto se alejó de su posición con cada página a la vista.

## 1. Preparación
- **Montaje definitivo:** cámara del sistema en su posición, resolución y FPS de siempre; proyector a la altura final.
- **Proyector encendido con contenido real:** la prueba proyecta lo que se vería al leer la página que ve la cámara: su **fondo** (biblioteca en la página del ArUco 1, cuarto en la del ArUco 2; las demás, el fondo del prólogo) y sus **animaciones** (vuelo del Principito, piloto → niño, serpiente boa y elefante, avión y vuelta al mundo), cada vez que se llega a esa página. Los quizzes no se hacen. Con el proyector apagado los resultados salen más optimistas que en el aula.
  - Para volver a la versión anterior (repetir el onboarding), en `PruebaDeteccion` cambia *Modo Contenido* a `Onboarding`.
- **Libro:** la versión final impresa, con los 7 marcadores del mismo tamaño que se usará.
- **Luz** (mídela con un luxómetro en el celular, sobre el libro):
  - `luzBaja`: unos 100–200 lux (persianas cerradas o luces apagadas)
  - `luzNormal`: unos 300–500 lux (luz de aula habitual)
  - `luzAlta`: más de 700 lux, o una lámpara de costado que genere reflejos

## 2. Posiciones del libro
| Posición | Qué hacer | Qué se espera de la proyección |
|---|---|---|
| `centrado` | Libro quieto y bien ubicado | Quieta: 0–1 movimientos por página |
| `rotado` | Girado unos 15° o levantado un poco, como lo haría un niño | Se acomoda al giro y luego se queda quieta |
| `oclusion` | Una mano o un dedo sobre parte de la página o del marcador | No debe saltar; si se pierde el ArUco, se queda donde estaba |

## 3. Grabar
1. Cierra `aruco_sender.py` (usa la cámara) y abre el grabador:
   ```
   cd python
   python grabar_deteccion.py grabar --cam 0
   ```
2. En Unity, escena *Onboarding*: **activa el objeto `PruebaDeteccion`** y da **Play**. Haz clic en la vista Game para que reciba las teclas.
3. **Calibra la proyección** si el panel dice *SIN calibrar*: presiona **K** y coloca el centro del ArUco sobre cada una de las 4 cruces (1 s quieto en cada una). Si *Calibrar Antes Del Onboarding* está marcado en `SeguimientoLibro`, la calibración aparece sola al dar Play. Repite la calibración solo si mueves la cámara o el proyector.
4. Para cada video, siguiendo el nombre que muestra el panel (p. ej. `luzBaja_centrado_rep1.mp4`):

   | Tecla | Acción |
   |---|---|
   | **N** | Empieza el video. Antes, abre el libro en la **página 1** (ArUco 0) |
   | **P** | Empieza a pasar la hoja |
   | **Espacio** | La nueva doble página quedó a la vista |
   | **F** | Fin del video (después de la página 7) |
   | **R** | Descartar y repetir el video |
   | **← / →** | Elegir otro video |
   | **H** | Ocultar o mostrar el panel |
   | **K / L** | Calibrar la proyección / apagar o encender el seguimiento |

   De preferencia, una repetición por autor.
5. Al terminar, sal de Play y **desactiva `PruebaDeteccion`**.

## Resultados (en esta carpeta)
| Archivo | Contenido |
|---|---|
| `etiquetas_<sesion>.csv` | `video,inicio_s,fin_s,pagina_esperada`, con tramos de página y de `transicion` |
| `resumen_<sesion>.csv` | Por tramo: detectada, latencia, % del tiempo con la página correcta, confirmaciones incorrectas y **estabilidad de la proyección** (`movimientos_proyeccion`, `recorrido_proyeccion`, `desvio_max_proyeccion`, en unidades de escena; el libro mide 5,5 × 3,7) |
| `eventos_<sesion>.csv` | Cada mensaje ArUco, cada confirmación (2 en 1,5 s), cada cambio de página y cada movimiento de la proyección (`mov_proyeccion`), con su tiempo |
| `proyeccion_<sesion>.csv` | Posición y giro de la proyección cada 0,1 s, si se está moviendo y qué ArUco usa |
| `videos/` | Los videos, más `<video>.cuadros.csv` con el tiempo real de cada cuadro |

Los tiempos se cuentan en segundos desde el primer cuadro del video.

**Cómo leer la estabilidad:** con una página a la vista, 0–1 movimientos es normal (la proyección se acomoda al pasar la hoja). Más movimientos, o un desvío máximo mayor a ~0,1, indican que la proyección rebota.

## 4. Analizar cuadro por cuadro
```
python grabar_deteccion.py analizar
```
Usa el `etiquetas_*.csv` más reciente de esta carpeta y genera `analisis_<sesion>.csv`. Por tramo calcula el % de cuadros con el marcador correcto y la detección con el mismo criterio del sistema (mensajes cada 0,1 s, 2 en 1,5 s). Al final muestra la tasa de detección por condición, las confirmaciones incorrectas durante las transiciones y la estabilidad de la proyección por condición.
