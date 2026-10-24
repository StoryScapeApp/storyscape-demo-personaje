# StoryScape – Demo de personaje conversacional proyectado

Demo en Unity del proyecto **StoryScape** (realidad aumentada espacial con proyector + cámara). Un personaje 2D en pixel art entra al área proyectada sobre la mesa, camina hasta un punto final y, al llegar, **conversa por voz con el niño** (ElevenLabs) sobre un término o frase resaltada del libro.

## Flujo de la demo

1. Se proyecta un **resaltado** amarillo que pulsa sobre la zona del libro donde está el término.
2. El personaje entra caminando, hace algunas paradas y se detiene en el **punto final**, mirando al lector (aparece un anillo de luz bajo sus pies).
3. Al llegar, se conecta con un **agente de ElevenLabs** y le envía el término y su significado esperado.
4. El personaje saluda, pregunta qué significa el término, escucha al niño por micrófono, da pistas y retroalimentación. Su **boca se mueve** según el volumen de la voz.
5. Al terminar la conversación, el resaltado se apaga.

## Requisitos

- **Unity 6000.3.23f1** (Unity 6.3 LTS), plantilla 2D (URP)
- Windows 10/11 con micrófono
- Cuenta de **ElevenLabs** con un agente configurado (ver abajo)
- (Opcional) Proyector conectado por HDMI como segunda pantalla

## Instalación

1. Clona el repositorio:
   ```bash
   git clone https://github.com/StoryScapeApp/storyscape-demo-personaje.git
   ```
2. En **Unity Hub → Add → Add project from disk**, selecciona la carpeta clonada (la primera apertura tarda: se regenera `Library/`).
3. Abre la escena `Assets/Scenes/StoryScapeDemo.unity`.
4. Copia `Assets/StreamingAssets/elevenlabs.example.json` como `elevenlabs.local.json` en la misma carpeta y coloca tu Agent ID:
   ```json
   { "agentId": "agent_xxxxxxxxxxxxxxxxxxxx" }
   ```
   > `elevenlabs.local.json` está en el `.gitignore`: **no se sube al repositorio**.
5. En el objeto `AgenteElevenLabs`, escribe en **Microphone Device** el nombre de tu micrófono (aparece en la consola al dar Play) y desmarca **Simulate**.
6. Presiona **Play** y haz clic en la ventana de Unity (sin foco, el Play no avanza ni envía el micrófono).

## Configuración del agente en ElevenLabs

En **Agents → tu agente**:

| Ajuste | Valor |
|---|---|
| Idioma | Español |
| LLM | GPT-4o (u otro) |
| System prompt | Usa las variables `{{termino}}` y `{{significado}}` (Unity las envía al conectar) |
| First message | `¡Hola! Qué bueno verte leyendo conmigo. En esta página está resaltado "{{termino}}". ¿Me ayudas a descubrir qué significa?` |
| Voice → TTS output format | **PCM 16000 Hz** |
| Advanced → User input audio format | **PCM 16000 Hz** |
| Advanced → Call limits → Max conversation duration | 120 s (desarrollo) / ~300 s (validación) |
| Advanced → Client Events | `interruption` activado |
| Seguridad | Agente público (demo). Para producción: agente privado + URL firmada desde el servidor |

> El plan gratuito incluye pocos minutos de agente al mes: usa el **modo simulado** mientras desarrollas.

## Componentes principales

| Objeto / Script | Función |
|---|---|
| `Personaje` → `CharacterPathWalker2D` | Recorrido hasta el punto final (paradas al azar, velocidad, zona, `Loop`) y evento `On Arrived`. |
| `Personaje` → `CharacterTalkAnimator` | Mueve la boca (cerrada / entreabierta / abierta) según el volumen de la voz del agente. |
| `AgenteElevenLabs` → `ElevenLabsAgentClient` | Conexión WebSocket con ElevenLabs, micrófono, reproducción de voz y modo simulado. |
| `FlujoDemo` → `StoryScapeDemoFlow` | Une todo: resaltado del término, llegada del personaje e inicio de la conversación. |
| `Assets/Demo2D/Data/BancoTerminos.asset` | Banco de términos: término, significado, página y posición/tamaño del resaltado. |
| `Recorrido/PuntoInicio` y `Recorrido/PuntoFinal` | Desde dónde entra el personaje y dónde se detiene. |

### Opciones de `ElevenLabsAgentClient`

| Campo | Descripción |
|---|---|
| `Simulate` | Diálogo de prueba sin conectarse (no gasta minutos). |
| `Microphone Device` | Nombre exacto del micrófono; vacío = predeterminado de Windows. |
| `Mic Gain` | Amplificación del micrófono (1–30). Al hablar normal, la barra del medidor debe llegar a la mitad. |
| `Mute Mic While Agent Speaks` | Silencia el micrófono mientras habla el personaje (evita que se escuche por los parlantes). Desactívalo con audífonos para poder interrumpirlo. |
| `Show Mic Level` | Medidor del micrófono en pantalla (útil para pruebas; desactivar al proyectar). |
| `Show Subtitles` | Subtítulos en pantalla (desactivados; la conversación se registra en la consola). |

## Banco de términos

Términos de ejemplo de *El Principito*: **domesticar**, **baobab**, **asteroide**, **efímero** y **"lo esencial es invisible a los ojos"**.
Edita `BancoTerminos.asset` para cambiarlos y elige cuál usar con **Term Index** en `FlujoDemo`.

## Proyectar sobre la mesa

1. Conecta el proyector y en Windows presiona **Win + P → Extender**.
2. Compila con **File → Build Profiles → Windows → Build** (*Fullscreen Window*; *Run In Background* ya está activado).
3. Ejecuta el `.exe` con el argumento `-monitor 2` para abrirlo en el proyector.
4. Corrige orientación y trapecio desde el menú del proyector.
5. Calibra con el proyector encendido:
   - `Recorrido/PuntoFinal`: dónde se detiene el personaje.
   - `Highlight Position / Size` de cada término: que el resaltado caiga sobre la palabra en el papel.
   - Tamaño: *Scale* de `Personaje` o *Size* de la cámara.

## Menús de editor (StoryScape)

| Menú | Qué hace |
|---|---|
| **Generar demo personaje caminando** | Regenera sprites y escena desde cero ⚠️ descarta ajustes manuales (escalas, conversación, boca). |
| **Configurar conversación ElevenLabs** | Agrega banco de términos, resaltado, agente y flujo a la escena actual. |
| **Agregar boca al personaje** | Dibuja la boca y crea los sprites de habla. Ejecutar de nuevo si se regeneran los sprites. |

## Estructura

```
Assets/
├── Demo2D/
│   ├── Data/BancoTerminos.asset           # Términos, significados y posición del resaltado
│   ├── Editor/                            # Menús de generación y configuración
│   ├── Scripts/                           # Recorrido, conversación, boca y flujo de la demo
│   └── Sprites/                           # Personaje (pixel art), anillo y resaltado
├── Scenes/StoryScapeDemo.unity            # Escena principal
└── StreamingAssets/
    ├── elevenlabs.example.json            # Plantilla (sí se sube)
    └── elevenlabs.local.json              # Tu Agent ID (no se sube)
```

## Solución de problemas

| Síntoma | Causa / solución |
|---|---|
| El personaje no avanza o no te escucha | Haz clic en la ventana de Unity: sin foco, el Play se detiene. |
| La barra del micrófono apenas se mueve | Sube **Mic Gain** y el volumen de entrada del micrófono en Windows (*Sistema → Sonido → Entrada*). |
| El agente responde "¿Sigues ahí?" | No le llega voz: revisa el micrófono elegido y la ganancia. |
| La barra no se mueve con ningún micrófono | Windows → *Privacidad y seguridad → Micrófono* → permitir acceso a aplicaciones de escritorio. |
| Advertencia de formato de audio | Configura el agente en PCM 16000 Hz (salida y entrada). |

## Próximos pasos

- Anclar `PuntoFinal` y el resaltado a marcadores **ArUco** detectados con la cámara (OpenCV).
- Elegir el término según la página detectada.
- Reemplazar los sprites placeholder por los del personaje de StoryScape.
- Agente privado con URL firmada desde el servidor de la aplicación.
