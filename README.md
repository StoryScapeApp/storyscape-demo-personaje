# StoryScape – Mesa de lectura interactiva con El Principito

Prototipo en Unity del proyecto **StoryScape**: realidad aumentada espacial con proyector y cámara, más IA generativa, para fortalecer la comprensión lectora de niños de primaria.

El libro impreso de *El Principito* se coloca sobre la mesa. Una cámara detecta el marcador **ArUco** de cada página y el proyector muestra sobre la mesa al Principito, las reglas de uso, animaciones de escenas del libro y la ayuda con palabras difíciles.

## Flujo completo

```
ArUco 0 (página 1) ──► Onboarding ──► "¿Estás listo?" ──► niño responde ──► "¡Genial!…"
                                                                               │
                                       ┌───────────────────────────────────────┘
                                       ▼
                         Lectura en vivo (agente "Principito lector" escucha en silencio)
                           │                                   │
             el niño lee una frase activadora       el niño dice "Principito, tengo una duda"
                           │                                   │
                           ▼                                   ▼
                 animación sobre la mesa           se pausa la lectura → agente de dudas
               (p. ej. Principito volando)         resalta la palabra en el libro + significado
                           │                                   │
            al terminar la página: mini quiz       al terminar, la lectura se reconecta sola
         (agente "Principito quiz", 2 preguntas)
```

### 1. Onboarding

Empieza al detectar **marker_0**. Lo controla el componente `OnboardingSequence`. Los audios son pregenerados con ElevenLabs y el guion está en [`ONBOARDING_AUDIOS.md`](ONBOARDING_AUDIOS.md).

| Audio | Lo que se proyecta |
|---|---|
| `01_intro` | Título "¡Bienvenido a la mesa de la lectura!". El Principito aparece a la izquierda del libro y saluda. |
| `02_libro` | Tarjeta **1. No tapes el cuadrado** y se ilumina el marco del marcador. |
| `03_voz_alta` | Tarjeta **2. Lee en voz alta**. Un meteoro cruza hacia un planeta. |
| `04_quiz` | Tarjeta **3. Responde el mini quiz** con los círculos **A** y **B**. La A brilla y se apaga (demo de respuesta incorrecta). |
| `05_final` | La B brilla, crece y salen estrellas. "¿Estás listo?" |
| `06_listo` | Después de que el niño responde (o tras 7 s): "¡Genial!…". Todo se desvanece y empieza la lectura en vivo. |

Los momentos de cada animación están sincronizados con la voz. Los segundos se ajustan en `OnboardingSequence`: *Meteor Start Time*, *Quiz Try Time*, *Quiz Wrong Time* y *Celebrate Time*.

### 2. Lectura en vivo y animaciones

Componente `ReadingSession`, en el objeto **Lectura**:

- Al terminar el onboarding se conecta el agente **"Principito lector"**, que solo escucha. Unity silencia su voz.
- Cuando el niño lee una **frase activadora** del libro, se reproduce su animación. Se detecta de dos formas:
  1. **Unity** compara lo que transcribe el agente con la frase. Junta los últimos 20 s y la activa si coincide al menos el 70 % de las palabras.
  2. **El agente** llama a la herramienta de cliente `activar_animacion`, como respaldo.
- Al cambiar de página (ArUco), Unity le envía al agente el texto de la nueva página.
- Si la conexión se corta (por ejemplo, por el límite de duración), se reconecta sola.

| Página | Frase | Animación |
|---|---|---|
| 1 (marker_0) | "animaciones inspiradas en escenas del libro El Principito" | `principito_volando`: despega de su asteroide, planea con las aves sobre el libro y sale por la izquierda |
| 1 (marker_0) | "estimula su imaginación, creatividad y conexión con el relato" **o** "Desarrollado por los alumnos de la carrera de Ingeniería de Software" (final de la página) | `quiz_pagina_1`: abre el mini quiz |

En el campo *Frase* se pueden poner alternativas separadas por `|`. Basta con que coincida una.

> ⚠️ Mientras está conectado, el agente lector consume minutos de ElevenLabs durante toda la lectura.

### 3. Mini quiz al terminar la página

Componente `QuizSession`, en el objeto **Lectura**:

- Se activa cuando el niño lee el final de la página 1. Si el vuelo del Principito todavía se está reproduciendo, espera a que termine.
- Pausa la lectura en vivo y bloquea las dudas mientras dura.
- Muestra las preguntas **una por una** en un panel a la derecha del libro, con las alternativas A, B y C.
- **Mezcla las alternativas** en cada sesión, para que la correcta no esté siempre en la misma letra.
- El agente **"Principito quiz"** lee la pregunta y las alternativas en voz alta y escucha la respuesta (letra o texto). Luego llama a `elegir_opcion(letra)`:
  - Unity ilumina la opción y le devuelve al agente si es correcta.
  - Si es correcta, crece y salen estrellas. El Principito celebra.
  - Si es incorrecta, se atenúa y el niño lo intenta otra vez (2 intentos; después el Principito dice cuál era la correcta).
- El agente no conoce la respuesta correcta, así que no puede dar pistas.
- Guarda los resultados, sin datos personales, en `resultados_quiz.csv` (en `AppData/LocalLow/StoryScape Onboarding/Prueba Personaje/`).

| Pregunta | Correcta |
|---|---|
| ¿Cómo vas a leer el cuento? (En silencio · En voz alta · Cantando) | En voz alta |
| ¿Para qué sirve leer este libro de forma interactiva? (Para entender mejor la historia · Para leer más rápido · Para no tener que leer) | Para entender mejor la historia |

### 4. Dudas: "Principito, tengo una duda"

Componente `DoubtSession`, en el objeto **Dudas**:

- La frase se detecta con el **reconocedor de voz de Windows** (`TriggerPhraseListener`, gratis y sin internet) o en la transcripción del agente lector.
- Recién en ese momento se conecta el **agente de dudas**. La lectura se pausa.
- El Principito aparece y el niño pregunta por una palabra o frase. El agente llama a `resaltar_palabra(palabra, significado)` y Unity:
  - **resalta la palabra sobre el libro**, usando las posiciones extraídas del PDF;
  - muestra el **significado a la derecha del libro**.
- Cuando el agente termina (`end_call`), todo se apaga y la lectura se reconecta.

## Requisitos

- **Unity 6000.3.23f1** (Unity 6.3 LTS), 2D URP
- Windows 10/11 con micrófono. El reconocedor de la frase usa el **micrófono predeterminado** y el **idioma de voz en español** de Windows.
- Cámara (C920 o webcam) y **Python** con OpenCV para detectar los ArUco
- Proyector por HDMI como segunda pantalla, en 16:9
- Cuenta de **ElevenLabs** con dos agentes (ver abajo)

## Instalación

1. Clona el repositorio y ábrelo desde **Unity Hub → Add project from disk**.
2. Copia `Assets/StreamingAssets/elevenlabs.example.json` como `elevenlabs.local.json` y pega los Agent ID:
   ```json
   {
     "agentId": "agent_… (agente de dudas)",
     "agentIdLectura": "agent_… (Principito lector)",
     "agentIdQuiz": "agent_… (Principito quiz)"
   }
   ```
   > `elevenlabs.local.json` está en el `.gitignore` y **no se sube** al repositorio. Revisa siempre `git status` antes de hacer un commit.
3. Abre `Assets/Scenes/Onboarding.unity`. En `VozPrincipito` y en los agentes, revisa el nombre del micrófono (*Microphone Device*) y *Mic Gain*.
4. Ejecuta el script de Python que envía los ArUco por UDP a `127.0.0.1:5005`:
   ```json
   {"tipo": "aruco", "id": 0, "x": 0.5, "y": 0.5}
   ```
   También acepta `{"tipo": "duda"}` para abrir las dudas desde Python.
5. Presiona **Play** y muestra la página 1 a la cámara. Haz clic en la ventana de Unity: sin foco, el Play no avanza.

## Agentes de ElevenLabs

| Agente | Campo en `elevenlabs.local.json` | Prompt y configuración | Herramienta de cliente |
|---|---|---|---|
| Dudas | `agentId` | [`docs/prompt_principito_dudas.md`](docs/prompt_principito_dudas.md) | `resaltar_palabra` (`palabra`, `significado`) |
| Principito lector | `agentIdLectura` | [`docs/prompt_principito_lector.md`](docs/prompt_principito_lector.md) | `activar_animacion` (`animacion`) |
| Principito quiz | `agentIdQuiz` | [`docs/prompt_principito_quiz.md`](docs/prompt_principito_quiz.md) | `elegir_opcion` (`letra`) |

Ajustes comunes:
- Idioma español.
- **PCM 16000 Hz** de entrada y salida.
- El agente de dudas con la System tool **End call**.
- El lector con el **First message vacío**, **Skip turn** activado y **End call** desactivado.

Variables que envía Unity:
- Al agente de dudas: `{{pagina_actual}}` y `{{texto_leido}}` (páginas 1 a la actual, nunca las siguientes).
- Al agente lector: `{{pagina_actual}}`, `{{texto_pagina}}` y `{{animaciones}}`.
- Al agente del quiz: `{{intro}}`, `{{numero_pregunta}}`, `{{total_preguntas}}`, `{{pregunta}}`, `{{opcion_a}}`, `{{opcion_b}}` y `{{opcion_c}}`.

## Escenas

| Escena | Uso |
|---|---|
| `Scenes/Onboarding.unity` | **Escena principal** (primera del build): onboarding, lectura en vivo y dudas. |
| `Scenes/PruebaDudas.unity` | Prueba **sin proyector**: dibuja la página en pantalla para revisar resaltados y animaciones. ⚠️ Desmarca *Show* en *Simulated Book Page* antes de proyectar. |
| `Scenes/StoryScapeDemo.unity` | Demo anterior (personaje caminando + conversación por término). Su prompt está en [`PROMPTS_ELEVENLABS.md`](PROMPTS_ELEVENLABS.md). |

## Atajos de prueba (en Play)

| Tecla | Acción |
|---|---|
| **D** | Abre la conversación de dudas (sin decir la frase). |
| **Esc** | Cierra la conversación de dudas. |
| **T** | Prueba el resaltado y el significado **sin ElevenLabs** con la lista *Test Words* de `DoubtSession`. |
| **V** | Reproduce la animación de la página actual **sin ElevenLabs**. |
| **Q** | Inicia el mini quiz de la página 1. Sin el agente del quiz configurado, se responde con **A**, **B** o **C**. |

También están en el menú contextual (⋮) de cada componente: *Iniciar onboarding*, *Abrir dudas*, *Iniciar lectura en vivo*, *Reiniciar animaciones* y *Reproducir*.

## Datos del libro

En `Assets/StreamingAssets/` (se generan desde el PDF del libro; los fuentes están en `Assets/Lectura/Datos/`):

| Archivo | Contenido |
|---|---|
| `libro_paginas.json` | Texto de cada página. **marker_n = página n+1** (7 páginas: marker_0 a marker_6). |
| `palabras_paginas.json` | Posición de cada palabra en la página (0–1 del PDF), para resaltar sobre el libro. |
| `elevenlabs.local.json` | Agent IDs (no se sube). |

## Menús de editor (StoryScape)

| Menú | Qué hace |
|---|---|
| **Onboarding → Generar escena** | Regenera `Onboarding.unity` (incluye Dudas y Lectura). Conserva el micrófono. ⚠️ Descarta ajustes manuales de posición. |
| **Onboarding → Importar sprites nuevos** | Importa las animaciones del Principito desde `raw/<carpeta>` (260 px de alto). Ejecutar después de *Generar escena*. |
| **Onboarding → Asignar audios** | Asigna `Assets/Onboarding/Audio/<id>.mp3` a cada línea. |
| **Dudas → Generar escena de prueba** | Crea `PruebaDudas.unity` a partir del onboarding, con la página simulada. |
| **Lectura → Configurar lectura en vivo** | Configura los sprites del vuelo y agrega el objeto **Lectura** a la escena abierta. |

## Proyección y calibración

- Área proyectada: 1920×1080 px = 80×44.5 cm. Con la cámara ortográfica de tamaño 4, 1 unidad = 135 px ≈ 5.56 cm.
- Libro: 744×504 px (31×21 cm), centrado, representado por `LibroReferencia`, que solo se ve en la vista Scene.
- El Principito mide 260 px de alto, en proyección 1:1.
- Para calibrar con el proyector, mueve `LibroReferencia` (con él se mueven el marco del marcador y los resaltados).
- Build: **File → Build Profiles → Windows**, *Fullscreen Window*. Ejecuta el `.exe` con `-monitor 2` y envía 16:9 desde Windows.

## Estructura

```
Assets/
├── Onboarding/
│   ├── Audio/                 # 01_intro … 06_listo (ElevenLabs TTS)
│   ├── Editor/                # Generador de escenas, sprites y prueba de dudas
│   ├── Scripts/               # OnboardingSequence, DoubtSession, WordHighlighter,
│   │                          # TriggerPhraseListener, BookPageTracker, ArucoUdpReceiver…
│   └── Sprites/               # Tarjetas, marco, planeta, círculos
├── Demo2D/
│   ├── Scripts/ElevenLabsAgentClient.cs   # WebSocket, micrófono, herramientas de cliente
│   └── Sprites/raw/<animación>/           # Sprites del Principito (Reposo, Hablando, Saludo…)
├── Lectura/
│   ├── Datos/                 # JSON del libro (fuente) y versión anterior
│   ├── Editor/LecturaSetup.cs
│   ├── Scripts/               # ReadingSession, PrincipitoVolando, QuizSession
│   └── Sprites/Vuelo/         # Poses del vuelo con las aves y asteroide
├── Scenes/                    # Onboarding, PruebaDudas, StoryScapeDemo
└── StreamingAssets/           # libro_paginas.json, palabras_paginas.json, elevenlabs.*.json
docs/                          # Prompts de los agentes de ElevenLabs
```

## Solución de problemas

| Síntoma | Causa o solución |
|---|---|
| El Play no avanza o no escucha | Haz clic en la ventana de Unity: sin foco, el Play se detiene. |
| No reconoce "Principito, tengo una duda" | Pon como predeterminado el micrófono USB en Windows y el idioma de voz en español. Baja *Min Confidence* a **Baja**. |
| No se activa la animación al leer | Revisa en la consola las líneas `[ElevenLabs] Niño: …` (lo que transcribió el agente). Baja *Coincidencia* en *Reading Session* si hace falta. |
| El agente lector no se conecta | Revisa que `agentIdLectura` esté en `elevenlabs.local.json` y no diga `PEGA_AQUI…`. |
| El agente de dudas no resalta la palabra | Revisa que la Client tool se llame exactamente `resaltar_palabra` y que la palabra esté en la página actual. |
| El quiz se queda esperando | Revisa que `agentIdQuiz` esté configurado y que el agente tenga **End call** activado: Unity pasa a la siguiente pregunta cuando el agente termina. |
| No se oye la voz en el build | Revisa la salida de audio de Windows (puede estar yendo al HDMI del proyector). |
| La barra del micrófono apenas se mueve | Sube *Mic Gain* y el volumen de entrada en Windows. |
| Se cortan los bordes de la proyección | Envía 16:9 desde Windows. |

## Próximos pasos

- Animaciones para las otras 11 frases activadoras (lista en `docs/prompt_principito_lector.md`).
- Calibración cámara-proyector (homografía), para que los resaltados sigan al libro aunque se mueva.
- Agentes privados con URL firmada desde un servidor.
