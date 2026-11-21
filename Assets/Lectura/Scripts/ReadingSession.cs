using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using UnityEngine.Windows.Speech;
#endif

/// <summary>
/// Lectura en vivo (empieza al terminar el onboarding).
/// Cuando el niño lee una frase activadora del libro, se activa su animación. Se detecta de tres formas:
///   1) DICTADO DE WINDOWS (principal): entrega el texto PARCIAL mientras el niño habla, palabra por palabra,
///      así la animación aparece apenas termina la frase. Requiere "Reconocimiento de voz en línea" activado
///      en Windows y el idioma de voz en español. Si no está disponible, se usan las Frases Rápidas.
///   2) Agente "Principito lector" de ElevenLabs (respaldo): Unity compara lo que transcribe (llega cuando
///      el niño hace una pausa) y el agente puede llamar a la herramienta "activar_animacion".
///   3) Frases Rápidas (reconocedor de palabras clave de Windows): solo si el dictado no está disponible.
/// - Si el niño dice "Principito, tengo una duda", se pausa la lectura y se abre el agente de dudas.
/// - Al cambiar de página (ArUco), le avisa al agente con el texto de la nueva página.
/// Prueba sin voz: tecla V (reproduce la animación de la página actual).
/// </summary>
public class ReadingSession : MonoBehaviour
{
    [Serializable]
    public class SceneAnimation
    {
        [Tooltip("Identificador que usa el agente en activar_animacion")]
        public string id = "principito_volando";
        [Tooltip("Página del libro (marker + 1). 0 = cualquier página")]
        public int pagina = 1;
        [Tooltip("Frase del libro. Puedes poner alternativas separadas por | (basta con que coincida una)")]
        [TextArea(2, 3)] public string frase;
        [Tooltip("Proporción de palabras de la frase que deben escucharse para activarla")]
        [Range(0.3f, 1f)] public float coincidencia = 0.7f;
        public bool soloUnaVez = true;
        [Tooltip("Respaldo si no hay dictado: partes cortas de la frase para el reconocedor de palabras clave de Windows")]
        public string[] frasesRapidas = new string[0];
        public UnityEvent alActivar = new UnityEvent();
        [NonSerialized] public bool activada;
    }

    [Header("Agentes")]
    [Tooltip("Agente que escucha la lectura (configKey = agentIdLectura, voz silenciada)")]
    public ElevenLabsAgentClient agent;
    [Tooltip("Conecta el agente lector de ElevenLabs durante la lectura (gasta minutos). Con el dictado de Windows funcionando, es solo un respaldo")]
    public bool conectarAgente = true;
    [Tooltip("Agente de dudas: mientras conversa, la lectura se pausa")]
    public ElevenLabsAgentClient doubtAgent;
    public DoubtSession doubts;
    [Tooltip("Mientras el quiz está activo, la lectura se pausa")]
    public QuizSession quiz;
    public BookPageTracker tracker;

    [Header("Animaciones del libro")]
    public List<SceneAnimation> animaciones = new List<SceneAnimation>();
    public string toolName = "activar_animacion";
    [Tooltip("Solo activa las animaciones de la página en la que está el niño")]
    public bool soloPaginaActual = true;

    [Header("Detección")]
    [Tooltip("Segundos de lectura que se juntan para buscar una frase (el niño puede hacer pausas)")]
    public float ventanaSegundos = 20f;
    public float reconectarTras = 2f;
    [Tooltip("Abre las dudas cuando se escucha \"Principito\" y alguna de estas palabras")]
    public string[] palabrasDuda = { "duda", "pregunta" };

    [Header("Detección en tiempo real (Windows)")]
    [Tooltip("Dictado de Windows: texto parcial mientras el niño lee (lo más rápido). Requiere 'Reconocimiento de voz en línea' activado")]
    public bool dictadoWindows = true;
    [Tooltip("Muestra en la consola el texto parcial del dictado (para pruebas)")]
    public bool mostrarDictado = false;
    [Tooltip("Respaldo si no hay dictado: reconoce las Frases Rápidas")]
    public bool deteccionRapida = true;
    public TriggerPhraseListener.Confianza confianzaRapida = TriggerPhraseListener.Confianza.Baja;

    [Header("Transcripción en tiempo real (ElevenLabs Scribe v2 Realtime)")]
    [Tooltip("Texto parcial mientras el niño lee (necesita \"apiKey\" en elevenlabs.local.json). Se conecta solo durante la lectura")]
    public ScribeRealtimeClient scribe;
    public bool usarScribe = true;

    [Header("Voz desde Python (Vosk)")]
    [Tooltip("Recibe el texto parcial del script voz_vosk.py por UDP (mismo puerto del ArUco). Es la detección más rápida")]
    public ArucoUdpReceiver receptorVoz;

    [Header("Prueba")]
    public bool tecladoPrueba = true;

    public bool Activa { get; private set; }
    /// Pausa por comando de voz: no detecta frases, pero Scribe sigue escuchando para oír "continúa".
    public bool Pausada { get; set; }
    /// Todo lo que Scribe/Vosk oye, aunque la lectura esté en pausa (lo usa VoiceCommands).
    public event Action<string, bool> TextoEnVivo;

    readonly List<KeyValuePair<float, string>> heard = new List<KeyValuePair<float, string>>();     // agente
    readonly List<KeyValuePair<float, string>> dictated = new List<KeyValuePair<float, string>>();  // dictado (frases completas)
    // Diagnóstico: avisa si el agente está conectado pero no transcribe nada
    float connectedSince = -1f, lastTranscript, micPeak;
    bool warnedSilence;
    float retryAt;
    int lastPage = -1;
    bool listening;
    readonly List<KeyValuePair<float, string>> voskDone = new List<KeyValuePair<float, string>>();
    string voskPartial = "";
    bool voskAnnounced;
    float scribeRetryAt;
    bool warnedMicShare;

    void OnEnable()
    {
        if (receptorVoz) receptorVoz.TextReceived += OnVosk;
        if (scribe) scribe.TextReceived += OnScribe;
        if (!agent) return;
        agent.UserTranscript += OnTranscript;
        agent.RegisterTool(toolName, OnTool);
    }

    void OnDisable()
    {
        if (receptorVoz) receptorVoz.TextReceived -= OnVosk;
        if (scribe) { scribe.TextReceived -= OnScribe; if (scribePropio && scribe.IsRunning) scribe.StopStreaming(); scribePropio = false; }
        if (agent)
        {
            agent.UserTranscript -= OnTranscript;
            agent.RegisterTool(toolName, null);
        }
        UpdateRealtime(false);
    }

    /// Empieza a escuchar la lectura (lo llama el onboarding al terminar).
    [ContextMenu("Iniciar lectura en vivo")]
    public void Iniciar()
    {
        Activa = true;
        retryAt = Time.unscaledTime;
        Debug.Log("[Lectura] Escuchando la lectura.");
    }

    [ContextMenu("Detener lectura en vivo")]
    public void Detener()
    {
        Activa = false;
        if (agent && agent.IsRunning) agent.EndConversation();
    }

    [ContextMenu("Reiniciar animaciones")]
    public void ReiniciarAnimaciones()
    {
        foreach (var a in animaciones) a.activada = false;
        LimpiarEscuchado();
    }

    /// Olvida lo escuchado hasta ahora (para que una frase dicha antes no vuelva a contar).
    public void LimpiarEscuchado()
    {
        heard.Clear();
        dictated.Clear();
        voskDone.Clear();
        voskPartial = "";
    }

    /// Vuelve a armar las animaciones de las páginas desde..hasta (0,0 = todas): como si no se hubieran leído.
    public void RearmarPaginas(int desde, int hasta)
    {
        int n = 0;
        foreach (var a in animaciones)
        {
            bool todas = desde <= 0 && hasta <= 0;
            if (todas || a.pagina <= 0 || (a.pagina >= desde && a.pagina <= hasta)) { if (a.activada) n++; a.activada = false; }
        }
        LimpiarEscuchado();
        Debug.Log($"[Lectura] Rearmadas {n} animaciones ({(desde <= 0 && hasta <= 0 ? "todas" : $"páginas {desde} a {hasta}")}).");
    }

    /// Páginas del capítulo al que pertenece la página: cada capítulo termina en la página de su quiz ("quiz_capitulo_N").
    public void RangoCapitulo(int pagina, out int desde, out int hasta)
    {
        var finales = new List<int>();
        foreach (var a in animaciones)
            if (a.id != null && a.id.StartsWith("quiz_capitulo_", StringComparison.OrdinalIgnoreCase) && a.pagina > 0) finales.Add(a.pagina);
        finales.Sort();
        desde = 1; hasta = pagina;
        foreach (int f in finales)
        {
            if (pagina <= f) { hasta = f; return; }
            desde = f + 1;
        }
        hasta = Mathf.Max(pagina, tracker ? tracker.PageCount : pagina);   // después del último quiz: hasta el final del libro
    }

    // ───────────── Dictado y palabras clave de Windows ─────────────
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    DictationRecognizer dict;
    bool dictFailed, dictAnnounced;
    float dictRetryAt;
    readonly ConcurrentQueue<KeyValuePair<bool, string>> dictQueue = new ConcurrentQueue<KeyValuePair<bool, string>>(); // (final, texto)
    string dictPartial = "";
    float dictRunningSince = -1f;
    int dictHeardCount;
    bool dictWarnedSilence;

    KeywordRecognizer fast;
    readonly Dictionary<string, SceneAnimation> fastMap = new Dictionary<string, SceneAnimation>();
    string fastHeard;

    void Start()
    {
        if (dictadoWindows) SetupDictation();
        if (dict == null) SetupFast();
    }

    void SetupDictation()
    {
        try
        {
            dict = new DictationRecognizer(ConfidenceLevel.Low, DictationTopicConstraint.Dictation);
            dict.InitialSilenceTimeoutSeconds = 30f;
            dict.AutoSilenceTimeoutSeconds = 30f;
            dict.DictationHypothesis += t => dictQueue.Enqueue(new KeyValuePair<bool, string>(false, t));
            dict.DictationResult += (t, c) => dictQueue.Enqueue(new KeyValuePair<bool, string>(true, t));
            dict.DictationComplete += cause => { if (cause != DictationCompletionCause.Complete) Debug.Log("[Lectura] Dictado se detuvo (" + cause + "); se reinicia solo."); };
            dict.DictationError += (err, hr) =>
            {
                Debug.LogWarning($"[Lectura] Dictado de Windows no disponible ({err}, {hr}). Activa en Windows: Configuración → Privacidad y seguridad → " +
                                 "Voz → 'Reconocimiento de voz en línea', con el idioma de voz en español. Mientras tanto se usan las Frases Rápidas.");
                dictFailed = true;
            };
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Lectura] Dictado de Windows no disponible: " + e.Message);
            dict = null;
        }
    }

    void SetupFast()
    {
        if (fast != null || !deteccionRapida || !PhraseRecognitionSystem.isSupported) return;
        foreach (var a in animaciones)
            if (a.frasesRapidas != null)
                foreach (var f in a.frasesRapidas)
                    if (!string.IsNullOrWhiteSpace(f) && !fastMap.ContainsKey(f.Trim())) fastMap[f.Trim()] = a;
        if (fastMap.Count == 0) return;
        var level = confianzaRapida == TriggerPhraseListener.Confianza.Alta ? ConfidenceLevel.High
                  : confianzaRapida == TriggerPhraseListener.Confianza.Media ? ConfidenceLevel.Medium : ConfidenceLevel.Low;
        try
        {
            fast = new KeywordRecognizer(new List<string>(fastMap.Keys).ToArray(), level);
            fast.OnPhraseRecognized += args => fastHeard = args.text;
        }
        catch (Exception e) { Debug.LogWarning("[Lectura] Frases Rápidas no disponibles: " + e.Message); fast = null; }
    }

    bool DictationActive => dict != null && !dictFailed;

    void UpdateRealtime(bool listen)
    {
        if (dict != null && dictFailed)
        {
            // El dictado falló: se libera y se pasa a las Frases Rápidas y al reconocedor de la frase de dudas
            try { if (dict.Status == SpeechSystemStatus.Running) dict.Stop(); dict.Dispose(); } catch { }
            dict = null;
            if (doubts && doubts.phraseListener && doubts.enabled) doubts.phraseListener.enabled = true;
            SetupFast();
        }

        if (DictationActive)
        {
            if (listen)
            {
                if (dict.Status == SpeechSystemStatus.Stopped && Time.unscaledTime >= dictRetryAt)
                {
                    dictRetryAt = Time.unscaledTime + 2f;
                    // El dictado no puede funcionar junto con el reconocedor de palabras clave: el dictado escucha
                    // también "Principito, tengo una duda", así que el otro reconocedor se apaga.
                    if (doubts && doubts.phraseListener && doubts.phraseListener.enabled) doubts.phraseListener.enabled = false;
                    if (PhraseRecognitionSystem.Status == SpeechSystemStatus.Running) PhraseRecognitionSystem.Shutdown();
                    try { dict.Start(); }
                    catch (Exception e) { Debug.LogWarning("[Lectura] No se pudo iniciar el dictado: " + e.Message); dictFailed = true; }
                }
            }
            else if (dict.Status == SpeechSystemStatus.Running) dict.Stop();
            if (!dictAnnounced && dict.Status == SpeechSystemStatus.Running && !dictFailed)
            {
                dictAnnounced = true;
                Debug.Log("[Lectura] Dictado de Windows activo: las frases se detectan mientras el niño lee.");
            }

            // Diagnóstico: el dictado corre pero no entrega texto
            if (listen && dict.Status == SpeechSystemStatus.Running)
            {
                if (dictRunningSince < 0f) dictRunningSince = Time.unscaledTime;
                if (!dictWarnedSilence && dictHeardCount == 0 && Time.unscaledTime - dictRunningSince > 25f)
                {
                    dictWarnedSilence = true;
                    Debug.LogWarning("[Lectura] El dictado de Windows lleva 25 s sin escuchar nada. Usa el MICRÓFONO PREDETERMINADO de Windows: " +
                                     "en Configuración → Sistema → Sonido → Entrada, elige 'Micrófono (USB Audio Device)' como predeterminado.");
                }
            }

            while (dictQueue.TryDequeue(out var item))
            {
                if (!listen) continue;
                dictHeardCount++;
                if (item.Key)
                {
                    float now = Time.unscaledTime;
                    dictated.Add(new KeyValuePair<float, string>(now, item.Value));
                    dictated.RemoveAll(h => now - h.Key > ventanaSegundos);
                    dictPartial = "";
                }
                else dictPartial = item.Value;
                if (mostrarDictado) Debug.Log("[Lectura] Dictado: " + item.Value);
                var sb = new StringBuilder();
                foreach (var h in dictated) sb.Append(h.Value).Append(' ');
                sb.Append(dictPartial);
                Match(sb.ToString(), item.Value, "dictado");
            }
            return;
        }

        if (fast == null) return;
        if (listen && !fast.IsRunning) fast.Start();
        else if (!listen && fast.IsRunning) fast.Stop();
        if (fastHeard != null)
        {
            string h = fastHeard; fastHeard = null;
            if (listen && fastMap.TryGetValue(h, out var a) && PageOk(a) && !(a.soloUnaVez && a.activada))
                Activar(a, $"frase rápida (\"{h}\")", false);
        }
    }

    void OnDestroy()
    {
        if (dict != null)
        {
            try { if (dict.Status == SpeechSystemStatus.Running) dict.Stop(); dict.Dispose(); } catch { }
            dict = null;
        }
        if (fast != null)
        {
            if (fast.IsRunning) fast.Stop();
            fast.Dispose();
            fast = null;
        }
    }
#else
    void UpdateRealtime(bool listen) { }
#endif

    void Update()
    {
        bool busy = (doubtAgent && doubtAgent.IsRunning) || (quiz && quiz.IsRunning);
        listening = Activa && !busy && !Pausada;
        UpdateRealtime(listening);
        UpdateScribe();

        if (tecladoPrueba && KeyTest())
        {
            var a = animaciones.Find(x => PageOk(x)) ?? (animaciones.Count > 0 ? animaciones[0] : null);
            if (a != null) Activar(a, "tecla V", true);
        }

        int page = tracker ? tracker.CurrentPage : 1;
        if (page != lastPage)
        {
            bool first = lastPage < 0;
            lastPage = page;
            heard.Clear();
            dictated.Clear();
            voskDone.Clear();
            voskPartial = "";
            if (!first && agent && agent.IsRunning)
                agent.SendContextualUpdate($"El niño pasó a la página {page}. Texto de la página {page}:\n{TextoPagina(page)}");
        }

        if (!Activa || !agent) return;
        if (busy || !conectarAgente)
        {
            if (agent.IsRunning) { agent.EndConversation(); if (busy) Debug.Log("[Lectura] Pausa: duda o quiz en curso."); }
            retryAt = Time.unscaledTime + reconectarTras;
            return;
        }
        // Diagnóstico de transcripción
        if (agent.IsRunning)
        {
            if (connectedSince < 0f) { connectedSince = lastTranscript = Time.unscaledTime; micPeak = 0f; warnedSilence = false; }
            micPeak = Mathf.Max(micPeak, agent.MicLevel);
            if (!warnedSilence && Time.unscaledTime - lastTranscript > 45f)
            {
                warnedSilence = true;
                Debug.LogWarning($"[Lectura] El agente lector no ha transcrito nada en 45 s (nivel máximo del micrófono: {micPeak:0.000}). Si el niño estaba leyendo: revisa el micrófono '{agent.microphoneDevice}', su volumen en Windows, Mic Gain y que 'Filtrar el habla de fondo' esté desactivado en el agente.");
            }
        }
        else connectedSince = -1f;

        if (!agent.IsRunning && Time.unscaledTime >= retryAt)
        {
            retryAt = Time.unscaledTime + reconectarTras + 3f;
            agent.StartConversation(BuildVariables());
        }
    }

    Dictionary<string, string> BuildVariables()
    {
        int page = tracker ? tracker.CurrentPage : 1;
        var sb = new StringBuilder();
        foreach (var a in animaciones)
            sb.Append("- ").Append(a.id).Append(" (página ").Append(a.pagina).Append("): \"").Append(a.frase).Append("\"\n");
        return new Dictionary<string, string>
        {
            { "pagina_actual", page.ToString() },
            { "texto_pagina", TextoPagina(page) },
            { "animaciones", sb.ToString().Trim() },
        };
    }

    string TextoPagina(int page) => tracker ? tracker.GetTextoPagina(page) : "";

    // ───────────── Detección ─────────────
    /// Scribe: se conecta mientras se escucha la lectura y se desconecta en dudas, quiz o fuera de la lectura.
    /// true = la lectura encendió Scribe. Solo apaga el Scribe que encendió ella: el onboarding también lo usa
    /// unos segundos para escuchar el nombre del niño, y no se le debe cortar.
    bool scribePropio;

    void UpdateScribe()
    {
        if (!scribe || !usarScribe) return;
        if (listening || (Activa && Pausada))
        {
            if (conectarAgente && !warnedMicShare)
            {
                warnedMicShare = true;
                Debug.LogWarning("[Lectura] Scribe y el agente lector usan el mismo micrófono: desmarca 'Conectar Agente' para evitar conflictos (Scribe ya detecta las frases).");
            }
            if (!scribe.IsRunning && Time.unscaledTime >= scribeRetryAt)
            {
                scribeRetryAt = Time.unscaledTime + 3f;
                scribe.StartStreaming();
                scribePropio = true;
            }
        }
        else if (scribePropio)
        {
            if (scribe.IsRunning) scribe.StopStreaming();
            scribePropio = false;
        }
    }

    void OnScribe(string texto, bool final)
    {
        TextoEnVivo?.Invoke(texto, final);
        OnLive(texto, final, "Scribe");
    }

    /// Texto del script de Python con Vosk: llega parcial mientras el niño lee (final = frase terminada).
    void OnVosk(string texto, bool final)
    {
        if (!voskAnnounced) { voskAnnounced = true; Debug.Log("[Lectura] Recibiendo voz desde Python (Vosk)."); }
        TextoEnVivo?.Invoke(texto, final);
        OnLive(texto, final, "Vosk");
    }

    /// Texto en tiempo real (Scribe o Vosk): parcial mientras el niño lee, final cuando hace una pausa.
    void OnLive(string texto, bool final, string origen)
    {
        if (!listening || string.IsNullOrWhiteSpace(texto)) return;
        if (mostrarDictado) Debug.Log($"[Lectura] {origen} {(final ? "✔" : "…")} {texto}");
        float now = Time.unscaledTime;
        if (final)
        {
            voskDone.Add(new KeyValuePair<float, string>(now, texto));
            voskDone.RemoveAll(h => now - h.Key > ventanaSegundos);
            voskPartial = "";
        }
        else voskPartial = texto;
        var sb = new StringBuilder();
        foreach (var h in voskDone) sb.Append(h.Value).Append(' ');
        sb.Append(voskPartial);
        Match(sb.ToString(), texto, origen);
    }

    /// Transcripción del agente lector (llega cuando el niño hace una pausa).
    void OnTranscript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        float now = Time.unscaledTime;
        lastTranscript = now;
        heard.Add(new KeyValuePair<float, string>(now, text));
        heard.RemoveAll(h => now - h.Key > ventanaSegundos);
        var sb = new StringBuilder();
        foreach (var h in heard) sb.Append(h.Value).Append(' ');
        Match(sb.ToString(), text, "lectura");
    }

    /// Busca la frase de dudas (en lo último que dijo) y las frases activadoras (en lo leído recientemente).
    void Match(string windowText, string lastText, string origen)
    {
        if (VoiceCommands.ContieneActivacion(lastText)) return;   // "Atención Principito, …" es un comando, no una duda
        var last = Tokens(lastText);
        if (last.Contains("principito") && Array.Exists(palabrasDuda, w => last.Contains(Norm(w))))
        {
            if (doubts && !doubts.bloqueada && !(doubtAgent && doubtAgent.IsRunning))
            {
                Debug.Log($"[Lectura] \"Principito, tengo una duda\" ({origen}) → abriendo dudas.");
                doubts.AbrirDudas();
            }
            return;
        }

        var window = Tokens(windowText);
        foreach (var a in animaciones)
        {
            if (!PageOk(a) || (a.soloUnaVez && a.activada)) continue;
            float score = Score(a.frase, window);
            if (score >= a.coincidencia) Activar(a, $"{origen} ({score:P0})", false);
        }
    }

    string OnTool(ElevenLabsAgentClient.ToolArgs args)
    {
        string id = (args.animacion ?? "").Trim();
        var a = animaciones.Find(x => string.Equals(x.id, id, StringComparison.OrdinalIgnoreCase));
        if (a == null) return $"No existe la animación \"{id}\".";
        if (a.soloUnaVez && a.activada) return "Esa animación ya se mostró. Sigue escuchando en silencio.";
        Activar(a, "agente", false);
        return "Animación activada. Sigue escuchando en silencio.";
    }

    bool PageOk(SceneAnimation a)
    {
        if (!soloPaginaActual || a.pagina <= 0 || tracker == null) return true;
        return a.pagina == tracker.CurrentPage;
    }

    void Activar(SceneAnimation a, string origen, bool force)
    {
        if (!force && a.soloUnaVez && a.activada) return;
        a.activada = true;
        Debug.Log($"[Lectura] Animación \"{a.id}\" activada por {origen}.");
        a.alActivar?.Invoke();
    }

    /// Proporción de palabras de la frase (de 3 letras o más) que aparecen en lo escuchado.
    static float Score(string phrase, HashSet<string> heardTokens)
    {
        float best = 0f;
        foreach (var alt in (phrase ?? "").Split('|'))
            best = Mathf.Max(best, ScoreOne(alt, heardTokens));
        return best;
    }

    static float ScoreOne(string phrase, HashSet<string> heardTokens)
    {
        int total = 0, found = 0;
        foreach (var w in Tokens(phrase))
        {
            if (w.Length < 3) continue;
            total++;
            foreach (var h in heardTokens)
                if (Similar(w, h)) { found++; break; }
        }
        return total == 0 ? 0f : (float)found / total;
    }

    static bool Similar(string a, string b)
    {
        if (a == b) return true;
        if (a.Length < 4 || b.Length < 4) return false;
        string s = a.Length <= b.Length ? a : b, l = a.Length <= b.Length ? b : a;
        return l.StartsWith(s) && l.Length - s.Length <= 3;
    }

    static HashSet<string> Tokens(string text)
    {
        var set = new HashSet<string>();
        var sb = new StringBuilder();
        foreach (char ch in (text ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0) { set.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) set.Add(sb.ToString());
        return set;
    }

    static string Norm(string w)
    {
        foreach (var t in Tokens(w)) return t;
        return "";
    }

    static bool KeyTest()
    {
#if ENABLE_INPUT_SYSTEM
        var k = UnityEngine.InputSystem.Keyboard.current;
        return k != null && k.vKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.V);
#endif
    }
}
