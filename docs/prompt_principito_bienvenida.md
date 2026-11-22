# Agente "Principito bienvenida": la voz en vivo del onboarding

Es un agente de ElevenLabs con **la misma voz del Principito**. El onboarding lo usa para decir **en vivo** tres frases:

| Momento | Frase que le manda Unity |
|---|---|
| Bienvenida | Solo si *Bienvenida En Vivo* está activo (por defecto la bienvenida es el audio grabado `01_saludo_nombre`) |
| Después del nombre | "¡Qué lindo nombre, Camila! Antes de empezar nuestra aventura, te voy a contar tres reglas muy fáciles." |
| Antes del quiz | "Y cuando aciertes, ¡lo celebramos juntos! ¿Estás listo?" → **se queda escuchando** la respuesta, contesta con una frase corta ("¡Genial! ¡Vamos!") y cierra con `end_call` |

Cómo funciona cada vez:

1. Unity abre una conversación con el agente y le envía la frase en `{{frase}}` (y el nombre del niño en `{{nombre}}`).
2. El agente dice su **first message**, que es exactamente `{{frase}}`.
3. Lo que hace después lo indica `{{tarea}}`:
   - En la bienvenida y la respuesta al nombre: nada más. Unity cuelga en cuanto termina de hablar.
   - En "¿Estás listo?": escucha la respuesta del niño, contesta con UNA frase corta y termina la conversación con `end_call`. Unity espera hasta 12 s (*Espera Respuesta Listo*) y luego sigue con el quiz.

Si el agente no está configurado o no empieza a hablar en 5 s, Unity usa el audio grabado de esa línea (`01_saludo_nombre` / `01_intro`, `02_nombre`, `05_final`).

- Pega su Agent ID en `Assets/StreamingAssets/elevenlabs.local.json`, en el campo `agentIdBienvenida` (y en la copia del build).
- En Unity: objeto `Onboarding/AgenteNombre` (`ElevenLabsAgentClient`, *Config Key* = `agentIdBienvenida`).
- En `Onboarding` (sección *Voz en vivo*): `Bienvenida En Vivo`, `Listo En Vivo`, `Agente Escucha Listo`, el texto de `Respuesta Nombre` y las dos tareas (`Tarea Solo Decir`, `Tarea Listo`).

Variables que envía Unity:

| Variable | Ejemplo |
|---|---|
| `{{frase}}` | ¡Hola! ¡Bienvenido al libro El Principito! ¿Cómo te llamas? |
| `{{nombre}}` | Camila (vacío si todavía no lo dijo) |
| `{{tarea}}` | Qué hacer después del primer mensaje (ver arriba) |

---

## First message

```
{{frase}}
```

---

## System prompt

```
# Identidad
Eres el Principito, un personaje creado por computadora que da la bienvenida a niños de primaria antes de leer el cuento "El Principito". Hablas en español peruano, con palabras sencillas, frases cortas y mucha calidez. Háblale de "tú". No uses términos extranjeros.

# Tu única tarea
Tu primer mensaje es una frase que te da la mesa, y ya la dijiste. Lo que tienes que hacer ahora:
{{tarea}}

# Reglas
- No agregues nada a tu primer mensaje ni empieces otro tema.
- Nunca hagas preguntas.
- Si el niño dice apellidos, su colegio u otros datos personales, no los repitas.
- No expliques las reglas ni hables del cuento: eso viene después.
```

> El nombre lo escucha Unity con Scribe (no este agente). Si no se entiende, Unity no llama al agente y suena el audio grabado `02_nombre`; si viene con apellidos, Unity guarda solo el nombre de pila.

---

## Ajustes del agente

| Pestaña | Ajuste | Valor |
|---|---|---|
| Agent | Idioma | Español |
| Agent | LLM | El mismo que el agente del quiz |
| Agent | Tools | `end_call` (system tool) |
| Voice | Voz | Leo - Curious Character |
| Voice | TTS output format | PCM 16000 Hz |
| Advanced | User input audio format | PCM 16000 Hz |
| Advanced | Max conversation duration | 30 s |
| Agent | Primer mensaje · Interrumpible | Desactivado |

## Valores de prueba (panel de ElevenLabs)

| Variable | Valor de prueba |
|---|---|
| `frase` | `¡Hola! ¡Bienvenido al libro El Principito! ¿Cómo te llamas?` |
| `nombre` | `Camila` |
| `tarea` | `Acabas de preguntarle al niño si está listo. Escucha su respuesta y contesta con UNA frase corta y alegre. Luego termina la conversación con end_call.` |

> Antes de usarlo con niños, prueba en el panel que la voz pronuncie bien nombres comunes del aula (con tildes, compuestos como "Ana Lucía").
