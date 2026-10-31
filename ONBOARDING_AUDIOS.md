# Audios del onboarding – ElevenLabs (Text to Speech)

El onboarding usa **audios pregenerados** (no gasta minutos del agente). Genera cada línea en **ElevenLabs → Text to Speech** con **la misma voz del agente**, descárgala y guárdala en:

```
Assets/Onboarding/Audio/<id>.mp3
```

El nombre del archivo debe ser **exactamente el id** (por ejemplo `01_intro.mp3`). Luego, con la escena `Onboarding` abierta, ejecuta **StoryScape → Onboarding → Asignar audios**.

> Si falta algún audio, el onboarding igual funciona: espera un tiempo según la longitud del texto (útil para probar).

| id | Momento | Texto a generar |
|---|---|---|
| `01_intro` | Saludo (luego espera la respuesta del niño) | ¡Hola! ¡Bienvenido al libro El Principito! ¿Cómo te llamas? |
| `02_nombre` | Después de que el niño dice su nombre | ¡Qué lindo nombre! Antes de empezar nuestra aventura, te voy a contar tres reglas muy fáciles. |
| `03_regla1` | Aparece la regla 1 · Principito **escucha** | Uno: lee en voz alta. Así yo puedo escucharte y acompañarte en la lectura. |
| `04_regla2` | Aparece la regla 2 · Principito **pregunta** | Dos: si tienes una duda, pídeme ayuda. Solo tienes que decir: Principito, tengo una duda, y yo te ayudaré. |
| `05_regla3` | Aparece la regla 3 | Tres: al terminar cada capítulo, jugaremos a las preguntas. Te haré dos preguntas sobre lo que leíste. ¡No te preocupes, no es un examen, es un juego! |
| `06_juego_intro` | Aparecen la pregunta y las 3 opciones | Mira, así se verá el juego. Por ejemplo: ¿cómo vas a leer el cuento? ¿En silencio, en voz alta o cantando? |
| `07_juego_casi` | La opción azul se apaga · "¡Casi! Otra vez" | Si eliges una respuesta que no es, no pasa nada. ¡Puedes intentarlo otra vez! |
| `08_juego_bien` | La opción verde crece, estrellas · "¡Muy bien!" | Y cuando aciertes, ¡lo celebramos juntos! |
| `09_cierre` | La pregunta desaparece (luego espera la respuesta) | ¿Estás listo? ¡Empecemos! |
| `10_final` | Despedida | ¡Genial! Pasa la página y empieza a leer en voz alta. |

## Consejos

- Usa la **misma voz y configuración** en todas las líneas para que suene como un solo personaje.
- Si quieres cambiar un texto, cámbialo también en el componente `OnboardingSequence` (campo *Text* de esa línea) para que la documentación y los tiempos de prueba coincidan.
- Para no usar `10_final`, deja vacío su texto en `OnboardingSequence` y no agregues el audio.
