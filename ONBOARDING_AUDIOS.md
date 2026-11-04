# Audios del onboarding – ElevenLabs (Text to Speech)

Genera cada línea en **ElevenCreative → Text to Speech** con **la misma voz del agente**, descárgala y guárdala en:

```
Assets/Onboarding/Audio/<id>.mp3
```

El nombre del archivo debe ser **exactamente el id**. Luego, con la escena `Onboarding` abierta, ejecuta **StoryScape → Onboarding → Asignar audios**.

> Si falta algún audio, el onboarding igual funciona: espera un tiempo según la longitud del texto.

| id | Estado | Momento | Texto |
|---|---|---|---|
| `01_intro` | ya existe | 1. Bienvenida (luego espera el nombre) | ¡Hola! ¡Bienvenido al libro El Principito! ¿Cómo te llamas? |
| `02_nombre` | ya existe | 1. Después del nombre | ¡Qué lindo nombre! Antes de empezar nuestra aventura, te voy a contar tres reglas muy fáciles. |
| `03_libro` | **nuevo** | 2. Tarjeta 1 · bordes del marcador | Primero: no tapes el cuadro negro con tus manos. Así sé en qué página estás. |
| `04_voz_alta` | **nuevo** | 3. Tarjeta 2 · meteoro y planeta | Segundo: lee en voz alta. Así podrás ver animaciones durante la historia. ¡Mira, ahí va un meteoro! |
| `05_duda` | **nuevo** | 4. Tarjeta 3 · Principito escucha | Tercero: si tienes una duda, llámame diciendo: Principito, tengo una duda. |
| `05b_duda_ejemplo` | **nuevo** | 4. Se resalta "Dedicatoria" y aparece su significado | Por ejemplo, si no entiendes la palabra dedicatoria, te la marco en el libro y te cuento lo que significa: son palabras al inicio de un libro para regalárselo a alguien especial. |
| `05c_quiz` | **nuevo** | 5. Aparece el panel del quiz | Y al terminar cada capítulo, te haré dos preguntas sobre lo que leíste. |
| `06_juego_intro` | ya existe | 5. Pregunta y opciones | Mira, así se verá el juego. Por ejemplo: ¿cómo vas a leer el cuento? ¿En silencio, en voz alta o cantando? |
| `07_juego_casi` | ya existe | 5. Opción A se apaga · Principito desaprueba | Si eliges una respuesta que no es, no pasa nada. ¡Puedes intentarlo otra vez! |
| `08_juego_bien` | ya existe | 5. Opción B crece, estrellas · Principito celebra (luego espera la respuesta) | Y cuando aciertes, ¡lo celebramos juntos! ¿Estás listo? ¡Empecemos! |
| `09_final` | ya existe | 6. Cierre · luego todo se desvanece | ¡Genial! Pasa la página y empieza a leer en voz alta. |

Los audios anteriores `03_regla1`, `04_regla2` y `05_regla3` ya no se usan y se pueden borrar.

## Consejos

- Usa la **misma voz y configuración** en todas las líneas.
- Si cambias un texto, cámbialo también en el componente `OnboardingSequence` (campo *Text* de esa línea).
