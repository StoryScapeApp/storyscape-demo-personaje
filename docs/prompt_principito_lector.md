# Agente "Principito lector": escucha la lectura y activa animaciones

Es un agente **nuevo** en ElevenLabs, distinto del agente de dudas. Se conecta al terminar el onboarding y escucha en silencio mientras el niño lee en voz alta. Cuando el niño lee una frase activadora del libro, Unity muestra su animación.

- Pega su Agent ID en `Assets/StreamingAssets/elevenlabs.local.json`, en el campo `agentIdLectura`.
- Unity silencia su voz, aunque el agente igual debe estar configurado para no hablar.
- Si el niño dice "Principito, tengo una duda", Unity cierra este agente, abre el agente de dudas y, al terminar la duda, vuelve a conectar este.

Unity envía al conectarse:

| Variable | Contenido |
|---|---|
| `{{pagina_actual}}` | Página en la que está el niño (marker_0 = página 1) |
| `{{texto_pagina}}` | Texto de esa página |
| `{{animaciones}}` | Lista de frases con animación: `id (página): "frase"` |

Cuando el niño pasa de página, Unity le envía al agente un aviso (*contextual update*) con el texto de la nueva página.

---

## First message

**Déjalo vacío.** El agente no debe saludar.

---

## System prompt

```
# Rol
Eres el oído del Principito en una mesa de lectura interactiva para niños de primaria. Un niño está leyendo en voz alta el libro "El Principito". Tu único trabajo es escuchar en silencio y avisar cuando el niño lea una de las frases que tienen animación.

# Lo que el niño está leyendo
Página actual: {{pagina_actual}}
Texto de esta página:
"""
{{texto_pagina}}
"""
Si recibes un aviso de que el niño pasó a otra página, usa el texto de esa nueva página.

# Frases con animación
Formato: id (página): "frase"
{{animaciones}}

# Reglas
- NUNCA hables. No saludes, no comentes, no corrijas, no felicites y no respondas preguntas. No digas nada en voz alta.
- Después de cada cosa que diga el niño, usa la herramienta skip_turn para seguir escuchando en silencio.
- Cuando el niño lea una frase de la lista, llama una sola vez a activar_animacion con el id de esa frase. Hazlo aunque la lea lento, con pausas o equivocándose en alguna palabra. Después sigue en silencio.
- Solo actívala si el niño está leyendo esa frase del libro. No la actives por palabras sueltas parecidas.
- Solo activa animaciones de la página actual.
- No actives la misma animación dos veces.
- Si el niño dice "Principito, tengo una duda", no respondas: otra parte del sistema se encarga.
- Nunca termines la conversación.
```

---

## Herramienta de cliente `activar_animacion`

En el agente: **Agent → Tools → Add tool → Client tool**.

| Campo | Valor |
|---|---|
| Name | `activar_animacion` |
| Description | Muestra sobre la mesa la animación de la frase que el niño acaba de leer. Úsala una sola vez por frase. |
| Wait for response | Desactivado |

Parámetro (**Add property**):

| Nombre | Tipo | Tipo de valor | Obligatorio | Descripción |
|---|---|---|---|---|
| `animacion` | String | LLM Prompt | Sí | Id de la animación de la lista (por ejemplo `principito_volando`). |

---

## Otros ajustes del agente

| Pestaña | Ajuste | Valor |
|---|---|---|
| Agent | Idioma | Español |
| Agent | First message | (vacío) |
| Agent → Tools | System tool **Skip turn** | Activada, si aparece en tu panel |
| Agent → Tools | System tool **End call** | Desactivada |
| Voice | TTS output format | PCM 16000 Hz |
| Advanced | User input audio format | PCM 16000 Hz |
| Advanced | Max conversation duration | 1800–3600 s (rango permitido: 60–7200 s). Unity reconecta solo si se corta. |
| Advanced | Turn timeout | 30 s (el máximo), para que no le hable al niño cuando hay silencio |
| Advanced | Soft timeout | Desactivado (-1) |
| Advanced | Turn eagerness (prontitud del turno) | **High / Eager**: cierra el turno con pausas más cortas y la transcripción llega antes |
| Advanced | Keywords del reconocimiento de voz (si aparece) | Principito, animaciones, inspiradas, escenas |

> **Costo:** este agente consume minutos **todo el tiempo que está conectado**, aunque no hable. Una sesión de lectura de 10 minutos gasta unos 10 minutos de ElevenLabs.

---

## Cómo se detecta la frase

0. **Detección rápida (Windows):** el reconocedor de voz de Windows escucha en tiempo real partes cortas de cada frase (*Frases Rápidas*), como "animaciones inspiradas" o "escenas del libro El Principito", y activa la animación apenas el niño las dice. No necesita internet ni esperar al agente.
1. **En Unity:** cada vez que el agente transcribe lo que leyó el niño, Unity lo compara con las frases de la lista. Junta los últimos 20 segundos, por si el niño hace pausas. Si coincide al menos el 70 % de las palabras, activa la animación. Esto funciona aunque el agente no llame a la herramienta.
2. **El agente:** llama a `activar_animacion`. Sirve de respaldo si el niño lee distinto.

Cada animación se muestra una sola vez por sesión.

---

## Frases activadoras del libro (para las próximas animaciones)

| Página (marker) | Frase | Animación |
|---|---|---|
| 1 (marker_0) | animaciones inspiradas en escenas del libro "El Principito" | `principito_volando` ✅ |
| 1 (marker_0) | final de la página: "…estimula su imaginación, creatividad y conexión con el relato" o "Desarrollado por los alumnos…" | `quiz_pagina_1` ✅ (abre el mini quiz) |
| 2 (marker_1) | En el libro se decía: "Las serpientes boas | pendiente |
| 2 (marker_1) | Representaba una serpiente boa que digería un elefante. | pendiente |
| 3 (marker_2) | aprendí a pilotar aviones. He volado un poco por todo el mundo. | pendiente |
| 4 (marker_3) | hasta que tuve una avería en el desierto del Sahara, | pendiente |
| 4 (marker_3) | La primera noche me dormí sobre la arena, a mil millas de cualquier tierra habitada. | pendiente |
| 4 (marker_3) | Y vi a un pequeño hombrecito, completamente extraordinario, que me observaba con mucha seriedad. | pendiente |
| 5 (marker_4) | Cuando el misterio es demasiado imponente, uno no se atreve a desobedecer. | pendiente |
| 6 (marker_5) | Entonces dibujé: | pendiente |
| 6 (marker_5) | Dibujé otro: | pendiente |
| 7 (marker_6) | —Esto es una caja. El cordero que quieres está dentro | pendiente |
| 7 (marker_6) | Te he dado un cordero muy pequeño. | pendiente |
