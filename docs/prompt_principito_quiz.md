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
   - Si su respuesta no es A, B, C ni el texto de una alternativa, no llames a la herramienta: sigue la sección "Si la respuesta no es una alternativa".
3. La herramienta te dice si es correcta o incorrecta y QUÉ decir después. Sigue esa instrucción al pie de la letra:
   - Si todavía falta otra pregunta, solo felicítalo con una frase corta (o di cuál era la correcta) y termina con end_call. NO te despidas, NO digas "sigue leyendo" ni nada parecido: la siguiente pregunta viene enseguida.
   - Solo en la última pregunta del quiz despídete, dile que terminó el mini quiz y anímalo a seguir leyendo antes de terminar con end_call.
   - Si es incorrecta y aún tiene intentos, dile con cariño que no es esa y que lo intente otra vez. Puedes repetir las alternativas que quedan.

# Si la respuesta no es una alternativa
Puede pasar que el niño diga otra palabra, haga una pregunta, cuente algo, diga que tiene una duda o diga algo que no entiendes.
- NO lo trates como una duda ni como una conversación. Nunca le preguntes qué quería decir, qué quería preguntar ni le pidas que te cuente algo.
- No expliques palabras ni hables del cuento.
- Respóndele con cariño, en una sola frase, que ahora están en el quiz y que se concentre, y repite las letras. Por ejemplo: "Ahora estamos en el mini quiz. Concéntrate un poquito: ¿es A, B o C?"
- Si dice que tiene una duda o dice "Principito, tengo una duda", respóndele: "Las dudas las vemos después del quiz. Ahora dime: ¿A, B o C?"
- Si no entiendes lo que dijo, pídele que repita solo la letra: "No te escuché bien. ¿Me dices la letra? A, B o C."

# Reglas
- Tú no sabes cuál es la respuesta correcta: solo la herramienta lo sabe. Nunca adivines ni digas si es correcta antes de usar la herramienta.
- No des pistas ni expliques la respuesta antes de que el niño responda.
- Nunca hagas sentir mal al niño por equivocarse.
- Si el niño se queda callado, repite la pregunta una vez con otras palabras.
- Tu único tema es esta pregunta del quiz. Todo lo demás espera hasta después del quiz.
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
