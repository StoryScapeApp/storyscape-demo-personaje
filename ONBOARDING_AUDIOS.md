# Audios del onboarding – ElevenLabs (Text to Speech)

Genera cada línea en **ElevenCreative → Text to Speech** con **la misma voz del agente**, descárgala y guárdala en:

```
Assets/Onboarding/Audio/<id>.mp3
```

El nombre del archivo debe ser **exactamente el id**. Luego, con la escena `Onboarding` abierta, ejecuta **StoryScape → Onboarding → Asignar audios**.

> Si falta algún audio, el onboarding igual funciona: espera un tiempo según la longitud del texto.

| id | Estado | Momento | Texto |
|---|---|---|---|
| `01_intro` | **actualizado** | 1. Bienvenida | ¡Hola! ¡Bienvenido al libro El Principito! Antes de empezar nuestra aventura, te voy a comentar unas reglas muy fáciles. |
| `02_libro` | ya existe | 2. Tarjeta 1 · bordes del marcador | Primero: no tapes el cuadro negro con tus manos. Así sé en qué página estás. |
| `03_voz_alta` | ya existe | 3. Tarjeta 2 · meteoro y planeta | Segundo: lee en voz alta. Así podrás ver animaciones durante la historia. ¡Mira, ahí va un meteoro! |
| `04_duda` | ya existe | 4. Tarjeta 3 · Principito escucha | Tercero: si tienes una duda, llámame diciendo: Principito, tengo una duda. |
| `04b_duda_ejemplo` | ya existe | 4. Se resalta "Dedicatoria" y aparece su significado | Por ejemplo, si no entiendes la palabra dedicatoria, te la marco en el libro y te cuento lo que significa: son palabras al inicio de un libro para regalárselo a alguien especial como cuando escribes "Para mi mamá" en un dibujo que le regalas. |
| `04c_quiz` | ya existe | 5. Aparece el panel del quiz | Y al terminar cada capítulo, te haré dos preguntas sobre lo que leíste. |
| `05_juego_intro` | **actualizado** | 5. Pregunta y opciones | Mira, así se verán las preguntas. Por ejemplo: ¿cómo vas a leer el cuento? ¿En silencio, en voz alta o cantando? Se responde diciendo la letra de la opción.|
| `06_juego_casi` | **actualizado** | 5. Opción A se apaga · Principito desaprueba | Si eliges una respuesta que no es como por ejemplo la opción A, no pasa nada. ¡Puedes intentarlo otra vez! |
| `07_juego_bien` | ya existe | 5. Opción B crece, estrellas · Principito celebra (luego espera la respuesta) | Y cuando aciertes, ¡lo celebramos juntos! ¿Estás listo? ¡Empecemos! |
| `08_final` | ya existe | 6. Cierre · luego todo se desvanece | ¡Genial! Pasa la página y empieza a leer en voz alta. |

## Consejos

- Usa la **misma voz y configuración** en todas las líneas.
- Si cambias un texto, cámbialo también en el componente `OnboardingSequence` (campo *Text* de esa línea).
