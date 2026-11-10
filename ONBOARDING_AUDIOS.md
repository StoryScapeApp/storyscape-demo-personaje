# Audios del onboarding – ElevenLabs (Text to Speech)

Genera cada línea en **ElevenCreative → Text to Speech** con **la misma voz del agente**, descárgala y guárdala en:

```
Assets/Onboarding/Audio/<id>.mp3
```

El nombre del archivo debe ser **exactamente el id**. Luego, con la escena `Onboarding` abierta, ejecuta **StoryScape → Onboarding → Asignar audios**.

> Si falta algún audio, el onboarding igual funciona: espera un tiempo según la longitud del texto.

| id | Estado | Momento | Texto |
|---|---|---|---|
| `01_intro` | ya existe | 1. Bienvenida | ¡Hola! ¡Bienvenido al libro El Principito! Antes de empezar nuestra aventura, te voy a comentar unas reglas muy fáciles. |
| `02_libro` | **actualizado** | 2. Tarjeta 1 · bordes del marcador | Primero: no tapes el cuadrado de barras con tus manos. Así sé en qué página estás. |
| `03_voz_alta` | **actualizado**  | 3. Tarjeta 2 · lee en voz alta | Segundo: lee en voz alta. Así podrás ver animaciones durante la historia.|
| `04_quiz` | **actualizado**  | 4. Tarjeta 3 · panel del quiz | Tercero: al terminar cada capítulo, te haré dos preguntas sobre lo que leíste. Responde diciendo la letra de la opción. Si eliges una respuesta que no es, no pasa nada. ¡Puedes intentarlo otra vez! |
| `05_final` | **actualizado** | 5. Opción correcta y cierre onboarding · luego todo se desvanece | Y cuando aciertes, ¡lo celebramos juntos! ¿Estás listo?|
| `06_listo` | ya existe (antes `08_final`) | 6. Después de que el niño responde a "¿Estás listo?" · luego todo se desvanece y empieza la lectura en vivo | ¡Genial! Pasa la página y empieza a leer en voz alta. |

## Consejos

- Usa la **misma voz y configuración** en todas las líneas.
- Si cambias un texto, cámbialo también en el componente `OnboardingSequence` (campo *Text* de esa línea).
