# StoryScape – Demo de personaje caminando (proyección)

Demo en Unity del proyecto **StoryScape** (realidad aumentada espacial con proyector + cámara). Un personaje 2D en pixel art entra al área proyectada, camina sobre la mesa haciendo algunas paradas y se detiene en un punto final, mirando al lector. No requiere teclado ni controles.

## Requisitos

- **Unity 6000.3.23f1** (Unity 6.3 LTS) con plantilla 2D (URP)
- Windows 10/11
- (Opcional) Proyector conectado por HDMI como segunda pantalla

## Cómo abrirlo

1. Clona el repositorio:
   ```bash
   git clone https://github.com/StoryScapeApp/storyscape-demo-personaje.git
   ```
2. En **Unity Hub → Add → Add project from disk**, selecciona la carpeta clonada.
3. La primera apertura tarda un poco: Unity regenera la carpeta `Library/`.
4. Abre la escena `Assets/Scenes/StoryScapeDemo.unity` y presiona **Play**.

## Cómo funciona

| Elemento | Descripción |
|---|---|
| Fondo negro | En el proyector equivale a *sin luz*: sobre la mesa solo se ve el personaje. |
| `Recorrido/PuntoInicio` | Lugar desde donde entra el personaje (fuera del área proyectada). |
| `Recorrido/PuntoFinal` | Lugar donde se detiene. Al llegar aparece un anillo de luz bajo sus pies. |
| `Personaje` → `CharacterPathWalker2D` | Controla el recorrido: paradas intermedias al azar, velocidad, pausas, zona de movimiento y animación en 4 direcciones. |
| Evento `On Arrived ()` | Se dispara al llegar al punto final (punto de enganche para iniciar el diálogo con la IA). |

Parámetros útiles en `CharacterPathWalker2D`: `Random Stops`, `Walk Speed`, `Pause Time`, `Area Min / Area Max`, `Loop`.

## Proyectar sobre la mesa

1. Conecta el proyector y en Windows presiona **Win + P → Extender**.
2. Compila con **File → Build Profiles → Windows → Build** (Fullscreen Window).
3. Ejecuta el `.exe` con el argumento `-monitor 2` para abrirlo en el proyector.
4. Corrige orientación y trapecio desde el menú del proyector y ajusta `PuntoFinal` para que coincida con la zona del libro.

## Estructura

```
Assets/
├── Demo2D/
│   ├── Editor/Demo2DGenerator.cs        # Menú StoryScape > Generar demo personaje caminando
│   ├── Scripts/CharacterPathWalker2D.cs  # Lógica del recorrido y animación
│   └── Sprites/                          # Sprites del personaje (placeholders en pixel art)
└── Scenes/
    └── StoryScapeDemo.unity              # Escena principal de la demo
```

> ⚠️ Volver a ejecutar el menú **StoryScape > Generar demo personaje caminando** regenera la escena y descarta los ajustes manuales (escala del personaje ×1.75 y del anillo ×0.85).

## Próximos pasos

- Anclar `PuntoFinal` a la posición de un marcador **ArUco** detectado con la cámara (OpenCV).
- Reemplazar los sprites placeholder por los del personaje de StoryScape.
- Conectar `On Arrived ()` con la IA conversacional.

## Equipo

Proyecto académico – Taller de Proyecto, Universidad Peruana de Ciencias Aplicadas (UPC).
