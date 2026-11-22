# StoryScape – Mesa de lectura interactiva con El Principito

Prototipo en Unity del proyecto **StoryScape**: realidad aumentada espacial con proyector y cámara, más IA generativa, para fortalecer la comprensión lectora de niños de primaria.

El libro impreso de *El Principito* se coloca sobre la mesa. Una cámara detecta el marcador **ArUco** de cada página y el proyector muestra sobre la mesa al Principito, las reglas de uso, animaciones de escenas del libro y la ayuda con palabras difíciles.

## Flujo completo

```
ArUco 0 (página 1) ──► Onboarding ──► "¿Estás listo?" ──► niño responde ──► "¡Genial!…"
                                                                               │
                                       ┌───────────────────────────────────────┘
                                       ▼
                  Lectura en vivo (Scribe v2 Realtime transcribe mientras el niño lee)
                           │                                   │
             el niño lee una frase activadora       el niño dice "Principito, tengo una duda"
                           │                                   │
                           ▼                                   ▼
                 animación sobre la mesa           se pausa la lectura → agente de dudas
               (p. ej. Principito volando)         resalta la palabra en el libro + significado
                           │                                   │
     al terminar la página o el capítulo: quiz    al terminar, la lectura se reconecta sola
   (agente "Principito quiz", 2 preguntas al azar)
```

### 1. Onboarding

Empieza al detectar **marker_0**. Lo controla el componente `OnboardingSequence`. Los audios son pregenerados con ElevenLabs y el guion está en [`ONBOARDING_AUDIOS.md`](ONBOARDING_AUDIOS.md).

| Audio | Lo que se proyecta |
|---|---|
| `01_intro` | Título "¡Bienvenido a la mesa de la lectura!". El Principito aparece a la izquierda del libro y saluda. |
| `02_libro` | Tarjeta **1. No tapes el cuadrado**. El marco brillante del marcador está desactivado por ahora (*Mostrar Marco Marcador*). |
| `03_voz_alta` | Tarjeta **2. Lee en voz alta**. Un meteoro cruza hacia un planeta. |
| `04_quiz` | Tarjeta **3. Responde el quiz** con los círculos **A** y **B**. La A brilla y se apaga (demo de respuesta incorrecta). |
| `05_final` | La B brilla, crece y salen estrellas. "¿Estás listo?" |
| `06_listo` | Después de que el niño responde (o tras 7 s): "¡Genial!…". Todo se desvanece y empieza la lectura en vivo. |

Los momentos de cada animación están sincronizados con la voz. Los segundos se ajustan en `OnboardingSequence`: *Meteor Start Time*, *Quiz Try Time*, *Quiz Wrong Time* y *Celebrate Time*.

### 2. Lectura en vivo y animaciones

Componente `ReadingSession`, en el objeto **Lectura**:

- Al terminar el onboarding, **ElevenLabs Scribe v2 Realtime** (componente `ScribeRealtimeClient`) empieza a transcribir la voz del niño. Entrega **texto parcial mientras lee**, sin esperar a que haga una pausa.
- Unity compara ese texto con las **frases activadoras** de la página actual: junta los últimos 20 s y activa la animación si coincide al menos el 70 % de las palabras. También busca "Principito, tengo una duda".
- En las dudas y el quiz, Scribe se desconecta y libera el micrófono. Al volver a la lectura se reconecta solo. Solo se cobra mientras está conectado.
- Con *Mostrar Dictado* activo, la consola muestra el texto parcial (`[Lectura] Scribe … `). Desmárcalo para las sesiones con niños.

Alternativas, en el mismo componente:

| Vía | Cuándo usarla |
|---|---|
| **Scribe v2 Realtime** (*Usar Scribe*) | **Principal.** Texto parcial con ~1 s de retraso. Necesita `apiKey`. |
| **Agente "Principito lector"** (*Conectar Agente*) | **Desactivado.** Su transcripción llega recién cuando el niño hace una pausa y gasta minutos toda la lectura. Si se activa junto con Scribe, compiten por el micrófono. |

| Página | Frase activadora | Animación |
|---|---|---|
| 1 (marker_0) | "animaciones inspiradas en escenas del libro El Principito" | `principito_volando`: despega de su asteroide, planea con las aves sobre el libro y sale por la izquierda (~5 s, campo *Velocidad*) |
| 1 (marker_0) | "carrera de Ingeniería de Software de la Universidad Peruana de Ciencias Aplicadas" (última línea de la página) | `quiz_pagina_1`: abre el quiz de la página 1 |
| 3 (marker_2) | "…la persona mayor se mostraba muy satisfecha de haber conocido a un hombre tan razonable" (fin del capítulo I) | `quiz_capitulo_1`: abre el quiz del capítulo I |
| 7 (marker_6) | "Y fue así como conocí al principito" (fin del capítulo II) | `quiz_capitulo_2`: abre el quiz del capítulo II |

En el campo *Frase* se pueden poner alternativas separadas por `|`. Basta con que coincida una. Las frases están en la escena (*Reading Session → Animaciones*) y sus valores iniciales en `Assets/Lectura/Editor/LecturaSetup.cs`.

### 3. Quiz al terminar la página o el capítulo

Componente `QuizSession`, en el objeto **Lectura**:

- Hay tres quizzes: `pagina_1` (la introducción), `capitulo_1` y `capitulo_2`. Se activan cuando el niño lee la última frase de su página (ver la tabla de frases activadoras, arriba). Si el vuelo del Principito todavía se está reproduciendo, espera a que termine.
- **`pagina_1`** tiene 2 preguntas fijas (en la escena). **`capitulo_1` y `capitulo_2`** sortean sus preguntas de un **banco** (`Assets/StreamingAssets/quiz_preguntas.json`): en cada sesión se elige **1 pregunta literal y 1 inferencial** al azar, en orden aleatorio. Así a cada niño le toca un par distinto y no se pasan las respuestas.
- Cada banco tiene 4 preguntas literales y 4 inferenciales, con 3 alternativas cada una. Para cambiar o agregar preguntas, edita el JSON (no hace falta tocar código): `correcta` es el índice, desde 0, de la alternativa correcta en el orden en que están escritas. Cada pregunta debe tener exactamente 3 alternativas; si no, se ignora.
- Cada quiz de capítulo se hace **una vez por sesión de juego**.
- Pausa la lectura en vivo y bloquea las dudas mientras dura.
- Muestra las preguntas **una por una** en un panel a la derecha del libro, con las alternativas A, B y C.
- **Mezcla las alternativas** en cada sesión, para que la correcta no esté siempre en la misma letra.
- El agente **"Principito quiz"** lee la pregunta y las alternativas en voz alta y escucha la respuesta (letra o texto). Luego llama a `elegir_opcion(letra)`:
  - Unity ilumina la opción y le devuelve al agente si es correcta.
  - Si es correcta, crece y salen estrellas. El Principito celebra.
  - Si es incorrecta, se atenúa y el niño lo intenta otra vez (2 intentos; después el Principito dice cuál era la correcta).
  - Solo se despide y lo invita a pasar la página al terminar la **última** pregunta. Unity se lo indica en la respuesta de la herramienta.
  - Si el niño dice algo que no es A, B o C, o dice que tiene una duda, el Principito lo devuelve al quiz.
- El agente no conoce la respuesta correcta, así que no puede dar pistas.
- Guarda los resultados, sin datos personales, en `resultados_quiz.csv`. En Windows: `AppData/LocalLow/StoryScape Onboarding/Prueba Personaje/`. En Mac: `~/Library/Application Support/StoryScape Onboarding/Prueba Personaje/`.

Preguntas fijas de la página 1:

| Pregunta | Correcta |
|---|---|
| ¿Cómo vas a leer el cuento? (En silencio · En voz alta · Cantando) | En voz alta |
| ¿Para qué sirve leer este libro de forma interactiva? (Para entender mejor la historia · Para leer más rápido · Para no tener que leer) | Para entender mejor la historia |

### 4. Dudas: "Principito, tengo una duda" (No se implementará por el momento)

Componente `DoubtSession`, en el objeto **Dudas**:

- La frase se detecta en el texto en tiempo real de **Scribe** (o de Vosk) y, como respaldo, con el **reconocedor de palabras clave de Windows** (`TriggerPhraseListener`).
- Recién en ese momento se conecta el **agente de dudas**. La lectura se pausa.
- El Principito aparece y el niño pregunta por una palabra o frase. El agente llama a `resaltar_palabra(palabra, significado)` y Unity:
  - **resalta la palabra sobre el libro**, usando las posiciones extraídas del PDF;
  - muestra el **significado a la derecha del libro**.
- Cuando el agente termina (`end_call`), todo se apaga y la lectura se reconecta.

### 5. Comandos de voz: "Atención Principito, …"

Para controlar la presentación sin cerrar Unity. Componente `VoiceCommands` (se crea solo al dar Play; no hace falta ponerlo en la escena). Solo cuenta si el texto empieza con **"Atención Principito"** seguido de una orden; así no se confunde con el texto del libro ni con "Principito, tengo una duda".

| Di… | Qué hace |
|---|---|
| **"Atención Principito, pausa"** (o "espera") | Congela audio, animaciones y lectura en vivo. |
| **"Atención Principito, continúa"** (o "sigue") | Reanuda donde quedó. |
| **"Atención Principito, repite esta página"** | Vuelve a armar las animaciones y olvida lo leído de la página actual. El ArUco no cambia: sigue en la misma página. |
| **"Atención Principito, repite el capítulo"** | Lo mismo para todas las páginas del capítulo actual (cap. I = páginas 1 a 3; cap. II = 4 a 7) y permite repetir su quiz. |
| **"Atención Principito, repite el quiz"** | Nuevo sorteo de preguntas (corta el quiz en curso o repite el último). |
| **"Atención Principito, repite la pregunta"** | El Principito vuelve a leer la pregunta actual, con las mismas alternativas. |
| **"Atención Principito, ayuda"** | Dice qué comandos hay. |
| **"Atención Principito, nuevo alumno"** | Pide confirmación ("sí, nuevo alumno"). Luego corta el onboarding, el quiz y la lectura, borra el nombre, vuelve a la página 1 y rearma todas las animaciones y quizzes. El **ArUco de la página 1** vuelve a iniciar el onboarding (¿Cómo te llamas?). |

Después de cada comando, el Principito confirma con una frase corta ("Listo, hago una pausa.", "Muy bien, sigamos."…): audio en `Assets/Resources/Comandos/<clave>.mp3` y el texto en la parte baja de la pantalla. Si falta el audio, solo sale el texto. Las claves y frases están en `VoiceCommands.Frases`.

**Teclas de respaldo** (si falla la voz): **P** pausa/continúa · **R** repite la página · **G** repite el capítulo · **U** repite el quiz · **Y** repite la pregunta · **H** ayuda · **N** nuevo alumno (pulsar dos veces para confirmar).

Dónde se oye la voz:

- **Durante la lectura y en pausa:** por **Scribe**.
- **Durante el quiz:** por la transcripción del **agente del quiz**. Aquí "pausa" cuelga al agente y, al continuar, se vuelve a leer la misma pregunta.
- **Durante el onboarding:** Scribe no escucha (solo unos segundos para el nombre), así que ahí solo valen las **teclas** (por ejemplo **P**).

Otros datos: tras un comando se ignora lo que se oiga durante 3,5 s (*Enfriamiento*); la confirmación de "nuevo alumno" dura 8 s. "Nuevo alumno" no escribe nada en el CSV del quiz, que sigue siendo un solo archivo con todos los alumnos mezclados y sin nombres.

**Audios de confirmación (pendientes de generar):** se crean una sola vez con ElevenLabs Text to Speech, con la voz del Principito (`ihKwLOjVUMG4lgUI6meZ`), modelo `eleven_multilingual_v2`, y se guardan como `Assets/Resources/Comandos/<clave>.mp3` con las claves: `pausa`, `continua`, `repite_pagina`, `repite_capitulo`, `repite_quiz`, `repite_pregunta`, `ayuda`, `nuevo_alumno_pide`, `nuevo_alumno`, `no_entendi`, `no_hay_quiz`. Por ahora **no hay audios**: los comandos funcionan igual y la confirmación sale solo escrita en pantalla. Para generarlos: por la API hace falta una key con el permiso *Text to Speech*, créditos y un **plan de pago** (las voces de la biblioteca no se pueden usar por API en el plan gratuito); sin plan de pago, se generan en la web de ElevenLabs (*Text to Speech*, con esa voz) y se guardan con esos nombres. La carpeta `Assets/Resources/Comandos/` se crea al agregarlos.

## Requisitos

- **Unity 6000.3.23f1** (Unity 6.3 LTS), 2D URP
- Windows 10/11 con micrófono. El reconocedor de la frase usa el **micrófono predeterminado** y el **idioma de voz en español** de Windows.
- Cámara (C920 o webcam) y **Python** con OpenCV para detectar los ArUco
- Proyector por HDMI como segunda pantalla, en 16:9
- Cuenta de **ElevenLabs** con dos agentes (dudas y quiz) y una **API key** para Scribe v2 Realtime (ver abajo)

## Instalación

1. Clona el repositorio y ábrelo desde **Unity Hub → Add project from disk**.
2. Copia `Assets/StreamingAssets/elevenlabs.example.json` como `elevenlabs.local.json` y pega los Agent ID y la API key:
   ```json
   {
     "agentId": "agent_… (agente de dudas)",
     "apiKey": "sk_… (solo permiso Speech to Text, para Scribe)"
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

| Servicio | Cuándo se conecta | Campo en `elevenlabs.local.json` | Prompt y configuración | Herramienta de cliente |
|---|---|---|---|---|
| **Agente "Principito bienvenida"** | En el onboarding, después de "¿Cómo te llamas?": dice "¡Qué lindo nombre, {{nombre}}!" y Unity cuelga | `agentIdBienvenida` | [`docs/prompt_principito_bienvenida.md`](docs/prompt_principito_bienvenida.md) | — |
| **Agente "Principito quiz"** | Una conversación por pregunta del quiz | `agentIdQuiz` | [`docs/prompt_principito_quiz.md`](docs/prompt_principito_quiz.md) | `elegir_opcion` (`letra`) |
| **Scribe v2 Realtime** (no es un agente) | Durante la lectura y unos segundos en el onboarding, para escuchar el nombre | `apiKey` | Sin prompt | — |

**Ajustes comunes de los agentes**
- Idioma español y **PCM 16000 Hz** de entrada y salida.
- Configuración usada en el agente del quiz: voz **Leo - Curious Character**, LLM **Claude Haiku 5.5** y duración máxima de **120 s** por conversación.
- System tool **End call** activada en dudas y quiz: Unity continúa cuando el agente termina la conversación.
- En **Eventos del cliente**, incluir `user_transcript`, `audio`, `interruption` y **`client_tool_call`**. Sin este último no llegan las herramientas a Unity.
- *Disposición* en **Normal** para dudas y quiz, así el niño puede pensar su respuesta.
- Herramientas de cliente con **Esperar respuesta** activado en `resaltar_palabra` y `elegir_opcion`. En `elegir_opcion`, que no hable antes de la herramienta.
- **Términos clave** del quiz: `A`, `B`, `C`, `la A`, `la B`, `la C` y el texto de las alternativas.
- **Privacidad (niños):** desactiva *Guardar audio de llamadas* y pon un período de retención en días en lugar de ilimitado.

**Scribe v2 Realtime**
- Usa una API key **restringida a Speech to Text** y, si es posible, con límite de créditos. Queda dentro del `.exe`, así que no compartas el build fuera del equipo del proyecto.
- Se conecta a `wss://api.elevenlabs.io/v1/speech-to-text/realtime` con `scribe_v2_realtime`, en español, PCM 16 kHz y cierre de frase por silencio (*Vad Silence Secs*). Se cobra todo el audio enviado mientras está conectado.

**Variables que envía Unity**
- Agente del quiz: `{{intro}}`, `{{numero_pregunta}}`, `{{total_preguntas}}`, `{{pregunta}}`, `{{opcion_a}}`, `{{opcion_b}}` y `{{opcion_c}}`. Las alternativas van en el orden mezclado que se proyecta, y el agente no sabe cuál es la correcta.

## Escenas

| Escena | Uso |
|---|---|
| `Scenes/Onboarding.unity` | **Escena principal** (primera del build): onboarding, lectura en vivo, quiz y dudas. Incluye **SimulacionLibro**, que muestra la página del libro en pantalla desde el inicio del onboarding para probar sin proyector. ⚠️ Desmarca *Simulación Activa* antes de proyectar sobre el libro real. |
| `Scenes/PruebaDudas.unity` | Prueba **sin proyector**: dibuja la página en pantalla para revisar resaltados y animaciones. ⚠️ Desmarca *Show* en *Simulated Book Page* antes de proyectar. |
| `Scenes/StoryScapeDemo.unity` | Demo anterior (personaje caminando + conversación por término). Su prompt está en [`PROMPTS_ELEVENLABS.md`](PROMPTS_ELEVENLABS.md). |

## Atajos de prueba (en Play)

| Tecla | Acción |
|---|---|
| **D** | Abre la conversación de dudas (sin decir la frase). |
| **Esc** | Cierra la conversación de dudas. |
| **T** | Prueba el resaltado y el significado **sin ElevenLabs** con la lista *Test Words* de `DoubtSession`. |
| **V** | Reproduce la animación de la página actual **sin ElevenLabs**. |
| **Q** | Inicia el quiz de la página 1. Sin el agente del quiz configurado, se responde con **A**, **B** o **C**. |
| **1** / **2** | Inician el quiz del capítulo I / II (con sorteo de preguntas). Mismo comportamiento que **Q**. |
| **P** · **R** · **G** · **U** · **Y** · **H** · **N** | Teclas de respaldo de los comandos de voz (ver *5. Comandos de voz*). |

**Cambiar de página sin cámara:** en `Onboarding` no hay teclas para pasar de página. Se simula un ArUco enviando varios paquetes UDP seguidos a `127.0.0.1:5005` (el receptor pide al menos 2 detecciones en 1,5 s). Por ejemplo, para la página 3 (`marker_2`): `{"tipo": "aruco", "id": 2, "x": 0.5, "y": 0.5}`. Después, lee en voz alta la última frase de esa página para que Scribe dispare el quiz. Para que la lectura en vivo empiece sin esperar el onboarding, usa el menú ⋮ de *Reading Session → Iniciar lectura en vivo* **con el Play activo**.

También están en el menú contextual (⋮) de cada componente: *Iniciar onboarding*, *Abrir dudas*, *Iniciar lectura en vivo*, *Reiniciar animaciones* y *Reproducir*.

## Datos del libro

En `Assets/StreamingAssets/` (se generan desde el PDF del libro; los fuentes están en `Assets/Lectura/Datos/`):

| Archivo | Contenido |
|---|---|
| `libro_paginas.json` | Texto de cada página. **marker_n = página n+1** (7 páginas: marker_0 a marker_6). |
| `palabras_paginas.json` | Posición de cada palabra en la página (0–1 del PDF), para resaltar sobre el libro. |
| `quiz_preguntas.json` | Banco de preguntas de los quizzes de capítulo (`capitulo_1` y `capitulo_2`): literales e inferenciales, con 3 alternativas y la correcta. |
| `elevenlabs.local.json` | Agent IDs y API key de Scribe (no se sube). |

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
│   ├── Scripts/               # ReadingSession, ScribeRealtimeClient, PrincipitoVolando, QuizSession, VoiceCommands
│   └── Sprites/Vuelo/         # Poses del vuelo con las aves y asteroide
├── Resources/Comandos/        # (pendiente) audios de confirmación de los comandos de voz: pausa.mp3, continua.mp3…
├── Scenes/                    # Onboarding, PruebaDudas, StoryScapeDemo
└── StreamingAssets/           # libro_paginas.json, palabras_paginas.json, elevenlabs.*.json
docs/                          # Prompts de los agentes de ElevenLabs
python/voz_vosk.py             # Alternativa sin costo: voz local (Vosk) → Unity por UDP
```

## Solución de problemas

| Síntoma | Causa o solución |
|---|---|
| El Play no avanza o no escucha | Haz clic en la ventana de Unity: sin foco, el Play se detiene. |
| No reconoce "Principito, tengo una duda" | Pon como predeterminado el micrófono USB en Windows y el idioma de voz en español. Baja *Min Confidence* a **Baja**. |
| La animación tarda en aparecer | Revisa que *Usar Scribe* esté marcado y que en la consola aparezca `[Scribe] Transcripción en tiempo real conectada.` Con *Mostrar Dictado* verás el texto parcial. |
| `[Scribe] auth_error` u otro error de Scribe | Revisa `apiKey` en `elevenlabs.local.json` (y en la copia del build) y que la key tenga permiso de Speech to Text. |
| No se activa la animación al leer | Revisa en la consola las líneas `[Lectura] Scribe …` (lo que se transcribió). Baja *Coincidencia* en *Reading Session* si hace falta. |
| El Principito no habla en el quiz (otro equipo) | El build no tiene `agentIdQuiz`: revisa `<Nombre>_Data/StreamingAssets/elevenlabs.local.json` y el `Player.log`. |
| El agente de dudas no resalta la palabra | Revisa que la Client tool se llame exactamente `resaltar_palabra` y que la palabra esté en la página actual. |
| El quiz de capítulo no se abre al leer la última frase | Revisa que la página actual sea la correcta (`[Libro] Página actual: 3` o `7` en la consola): los disparadores solo valen en su página. Sin cámara, hay que enviar el ArUco varias veces seguidas (ver *Atajos de prueba*). Revisa también que Scribe esté conectado (`[Scribe] Transcripción en tiempo real conectada.`). |
| El Principito cuenta respuestas que el niño no dijo | Eco: el micrófono capta la voz del agente. Usa audífonos y baja *Mic Gain* (con la ganancia x8 y parlantes, se mezcla). |
| Un comando de voz no responde | Debe empezar con "Atención Principito" y estar en la lectura (Scribe conectado) o en el quiz. En el onboarding usa las teclas. Revisa en la consola las líneas `[Comandos] (Scribe) "…" → Pausa`. |
| El Principito confirma solo con texto | Es lo esperado mientras no estén los audios de `Assets/Resources/Comandos/` (ver *5. Comandos de voz*). |
| Quedó en pausa y no continúa | Di "Atención Principito, continúa" o pulsa **P**. La pausa usa `Time.timeScale = 0`. |
| El quiz se queda esperando | Revisa que `agentIdQuiz` esté configurado y que el agente tenga **End call** activado: Unity pasa a la siguiente pregunta cuando el agente termina. |
| No se oye la voz en el build | Revisa la salida de audio de Windows (puede estar yendo al HDMI del proyector). |
| La barra del micrófono apenas se mueve | Sube *Mic Gain* y el volumen de entrada en Windows. |
| Se cortan los bordes de la proyección | Envía 16:9 desde Windows. |

## Próximos pasos

- Animaciones para las otras 11 frases activadoras (lista en `docs/prompt_principito_lector.md`).
- Calibración cámara-proyector (homografía), para que los resaltados sigan al libro aunque se mueva.
- Agentes privados con URL firmada y **tokens de un solo uso para Scribe** emitidos desde un servidor, para no guardar la API key en el `.exe`.
