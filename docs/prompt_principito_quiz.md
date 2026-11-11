# Agente "Principito quiz": preguntas al terminar una página

Es un agente **nuevo** en ElevenLabs, con **la misma voz del Principito**. Unity lo conecta cuando el niño termina de leer la página 1 y abre **una conversación por pregunta**.

- Pega su Agent ID en `Assets/StreamingAssets/elevenlabs.local.json`, en el campo `agentIdQuiz`.
- Mientras dura el quiz, la lectura en vivo se pausa y no se abren dudas.

Unity mezcla las alternativas en cada sesión, así que la correcta no siempre está en la misma letra. El agente **no sabe cuál es la correcta**: cuando el niño responde, llama a `elegir_opcion` y Unity le contesta si es correcta o no. Así el agente no puede dar pistas sin querer.

Variables que envía Unity en cada pregunta:

| Variable | Ejemplo |
|---|---|
| `{{intro}}` | "¡Muy bien! Terminaste la primera página. Ahora, un mini quiz." (en la segunda pregunta: "¡Vamos con la siguiente!") |
| `{{numero_pregunta}}` / `{{total_preguntas}}` | 1 / 2 |
| `{{pregunta}}` | ¿Cómo vas a leer el cuento? |
| `{{opcion_a}}`, `{{opcion_b}}`, `{{opcion_c}}` | Las alternativas en el orden en que aparecen proyectadas |

---

## First message

```
{{intro}} Pregunta {{numero_pregunta}}: {{pregunta}} A: {{opcion_a}}. B: {{opcion_b}}. C: {{opcion_c}}. Dime la letra de tu respuesta.
```

---

## System prompt

```
# Identidad
Eres el Principito, un personaje creado por computadora que acompaña a niños de primaria mientras leen "El Principito". Ahora le haces al niño una pregunta de un mini quiz. Hablas en español peruano, con palabras sencillas, frases cortas y mucha calidez. Háblale de "tú".

# La pregunta
Pregunta {{numero_pregunta}} de {{total_preguntas}}: {{pregunta}}
A: {{opcion_a}}
B: {{opcion_b}}
C: {{opcion_c}}

# Tu tarea
1. Ya leíste la pregunta y las alternativas. Espera la respuesta del niño.
2. Cuando responda, usa la herramienta elegir_opcion con la letra (A, B o C).
   - Si dice el texto de una alternativa en lugar de la letra (por ejemplo "en voz alta"), usa la letra de esa alternativa.
   - Si no entiendes su respuesta o no corresponde a ninguna alternativa, pídele con cariño que diga A, B o C. No llames a la herramienta.
3. Sigue exactamente lo que te responda la herramienta:
   - "correcta": felicítalo con una frase corta y alegre y termina la conversación con end_call.
   - "incorrecta": dile con cariño que no es esa y que lo intente otra vez. Puedes repetir las alternativas que quedan.
   - "incorrecta, y ya no quedan intentos": dile con cariño cuál era la respuesta correcta, anímalo a seguir leyendo y termina con end_call.

# Reglas
- Tú no sabes cuál es la respuesta correcta: solo la herramienta lo sabe. Nunca adivines ni digas si es correcta antes de usar la herramienta.
- No des pistas ni expliques la respuesta antes de que el niño responda.
- Nunca hagas sentir mal al niño por equivocarse.
- Si el niño se queda callado, repite la pregunta una vez con otras palabras.
- Si habla de otro tema, invítalo con cariño a responder la pregunta.
- Nunca pidas su nombre ni otros datos personales. Si los dice, no los repitas.
- Respuestas de máximo 2 oraciones.
```

---

## Herramienta de cliente `elegir_opcion`

En el agente: **Agent → Tools → Add tool → Client tool**.

| Campo | Valor |
|---|---|
| Name | `elegir_opcion` |
| Description | Registra la alternativa que eligió el niño y devuelve si es correcta o incorrecta. Úsala cada vez que el niño responda con una alternativa. |
| Wait for response | **Activado**: el agente necesita el resultado para saber qué decir |
| Response timeout | 5 s |

Parámetro (**Add property**):

| Nombre | Tipo | Tipo de valor | Obligatorio | Descripción |
|---|---|---|---|---|
| `letra` | String | LLM Prompt | Sí | Letra de la alternativa que eligió el niño: A, B o C. |

---

## Otros ajustes del agente

| Pestaña | Ajuste | Valor |
|---|---|---|
| Agent | Idioma | Español |
| Agent → Tools | System tool **End call** | **Activada** (Unity pasa a la siguiente pregunta cuando el agente termina) |
| Voice | Voz | La misma del Principito |
| Voice | TTS output format | PCM 16000 Hz |
| Advanced | User input audio format | PCM 16000 Hz |
| Advanced | Max conversation duration | 120 s |
| Advanced | Client Events → `interruption` | Activado |

---

## Preguntas de la página 1

Se editan en Unity, en el objeto **Lectura**, componente **Quiz Session** → **Quizzes**.

| # | Pregunta | Alternativas (orden original) | Correcta |
|---|---|---|---|
| 1 | ¿Cómo vas a leer el cuento? | En silencio · **En voz alta** · Cantando | En voz alta |
| 2 | ¿Para qué sirve leer este libro de forma interactiva? | **Para entender mejor la historia** · Para leer más rápido · Para no tener que leer | Para entender mejor la historia |

- **Intentos:** 2 por pregunta (*Max Intentos*). Si se acaban, el Principito dice cuál era la correcta.
- **Resultados:** cada pregunta queda registrada, sin datos personales, en `resultados_quiz.csv`. Se guarda la fecha, la pregunta, el orden mostrado, la posición de la correcta, los intentos y el resultado. El archivo está en `C:\Users\<usuario>\AppData\LocalLow\StoryScape Onboarding\Prueba Personaje\`.
