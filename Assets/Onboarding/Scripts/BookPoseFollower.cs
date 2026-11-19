using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Hace que TODA la proyección siga al libro físico: con la posición (x, y) del ArUco en la cámara
/// calcula dónde está el libro sobre la mesa y mueve/gira la Main Camera para que la escena
/// (LibroReferencia, resaltados, Principito, tarjetas…) caiga sobre el libro donde sea que lo pongan.
///
/// Se mueve la cámara (y no los objetos) porque varios scripts colocan cosas en coordenadas de mundo
/// (vuelo del Principito, meteoro, zorro, estrellas): así todo se desplaza junto sin tocar esos scripts.
///
/// 1) CALIBRAR (una vez por montaje cámara + proyector, tecla K):
///    se proyectan 4 cruces, una por una. Pon el centro del ArUco sobre la cruz y quédate quieto ~1 s
///    (se captura sola; Enter la captura a mano). Con las 4 se calcula la correspondencia cámara → proyección
///    y se guarda en calibracion_libro.json (carpeta persistentDataPath).
/// 2) Dónde está cada ArUco dentro del libro: lista 'Marker Positions' (medida del PDF; no se asume que el ArUco es el centro).
///    REFERENCIA (opcional, tecla J): con el libro abierto justo sobre el rectángulo \"Libro físico\" de la escena,
///    mide en vivo dónde está el marcador visible dentro del libro (y su ángulo) y reemplaza el valor del PDF.
/// L activa/desactiva el seguimiento. Sin calibración, la proyección se queda fija como antes.
///
/// Para girar con el libro, el script de Python debe enviar además \"ax\", \"ay\" (punto medio del borde
/// derecho del marcador, 0–1). Si no los envía, solo se sigue la posición.
/// </summary>
public class BookPoseFollower : MonoBehaviour
{
    [Header("Referencias")]
    public ArucoUdpReceiver receiver;
    [Tooltip("Cámara que se mueve (Main Camera)")]
    public Camera cam;
    [Tooltip("LibroReferencia: posición del libro en la escena cuando la proyección está alineada")]
    public Transform book;
    [Tooltip("Solo se usa para IDs que no están en 'Marker Positions'")]
    public Transform markerInBook;

    [Header("Dónde está cada ArUco en el libro")]
    [Tooltip("Tamaño del libro en unidades de escena (se toma de WordHighlighter si existe)")]
    public Vector2 bookSize = new Vector2(5.511f, 3.733f);
    [Serializable] public class MarkerOnPage { public int id; [Tooltip("Centro del ArUco en el pliego del PDF (0–1, origen arriba a la izquierda)")] public Vector2 pdfCenter; }
    [Tooltip("Medido del PDF Libro_principito_1: 0, 1, 3, 5 arriba a la derecha; 2, 4, 6 arriba a la izquierda")]
    public List<MarkerOnPage> markerPositions = new List<MarkerOnPage>
    {
        new MarkerOnPage { id = 0, pdfCenter = new Vector2(0.8133f, 0.2387f) },
        new MarkerOnPage { id = 1, pdfCenter = new Vector2(0.8133f, 0.2387f) },
        new MarkerOnPage { id = 2, pdfCenter = new Vector2(0.1859f, 0.2387f) },
        new MarkerOnPage { id = 3, pdfCenter = new Vector2(0.8133f, 0.2387f) },
        new MarkerOnPage { id = 4, pdfCenter = new Vector2(0.1859f, 0.2387f) },
        new MarkerOnPage { id = 5, pdfCenter = new Vector2(0.8133f, 0.2387f) },
        new MarkerOnPage { id = 6, pdfCenter = new Vector2(0.1859f, 0.2387f) },
    };

    [Header("Tapar el ArUco (evita que la proyección lo ilumine)")]
    [Tooltip("Proyecta un cuadro negro sobre el ArUco activo: si la proyección lo ilumina, la cámara lo ve distinto, la proyección se mueve y vuelve a cambiar la luz (rebote)")]
    public bool taparArUco = true;
    [Tooltip("Tamaño del ArUco en el pliego del PDF (0–1)")]
    public Vector2 markerPdfSize = new Vector2(0.1086f, 0.1501f);
    [Tooltip("Cuánto más grande que el ArUco es el cuadro negro")]
    public float maskScale = 1.5f;

    [Header("Seguimiento")]
    public bool follow = true;
    public bool followRotation = true;
    [Tooltip("IDs que se usan para ubicar el libro (vacío = todos)")]
    public List<int> markerIds = new List<int> { 0, 1, 2, 3, 4, 5, 6 };
    [Tooltip("Suavizado (s): mayor = más lento pero más estable")]
    public float smoothTime = 0.3f;

    [Header("Estabilidad (anti-rebote)")]
    [Tooltip("Lecturas que se combinan (mediana). Más = más estable pero responde más lento")]
    public int medianSamples = 5;
    [Tooltip("Con el libro quieto, la proyección solo se mueve si el libro se desplaza más que esto (unidades de escena)")]
    public float deadzone = 0.08f;
    [Tooltip("Con el libro quieto, solo se gira si el libro gira más que esto (grados)")]
    public float deadzoneAngle = 3f;
    [Tooltip("Un salto mayor a esto (unidades) se toma como lectura errónea hasta que se repita")]
    public float jumpThreshold = 0.6f;
    [Tooltip("Lecturas seguidas que confirman un salto grande (el libro sí se movió)")]
    public int jumpConfirm = 3;
    [Tooltip("Se usa un solo ArUco a la vez: se cambia a otro solo si el actual deja de verse este tiempo (s), p. ej. al pasar la hoja")]
    public float markerSwitchDelay = 0.6f;
    [Tooltip("Lecturas seguidas casi sin cambio para dar el libro por quieto (y fijar la proyección)")]
    public int quietSamples = 4;
    [Tooltip("Si el ángulo medido supera esto (grados) se ignora el giro (lectura errónea)")]
    public float maxAngle = 60f;

    [Header("Calibrar antes del onboarding")]
    [Tooltip("Al dar Play se abre la calibración (K) y el onboarding espera a que termine. K la cancela y se sigue con la calibración guardada")]
    public bool calibrarAntesDelOnboarding = false;
    [Tooltip("Si ya hay una calibración guardada, no se pide de nuevo al dar Play")]
    public bool omitirSiYaHayCalibracion = false;
    public OnboardingSequence onboarding;
    [Tooltip("ArUco que inicia el onboarding después de calibrar (página 1)")]
    public int markerInicio = 0;

    [Header("Calibración")]
    [Tooltip("Posición de las 4 cruces en la pantalla proyectada (0–1)")]
    public Vector2[] calibrationViewport = { new Vector2(0.2f, 0.25f), new Vector2(0.8f, 0.25f), new Vector2(0.8f, 0.75f), new Vector2(0.2f, 0.75f) };
    [Tooltip("Segundos quieto sobre la cruz para capturar sola")]
    public float holdToCapture = 1.0f;
    [Tooltip("Movimiento máximo (0–1 de la cámara) para considerar el marcador quieto")]
    public float stillTolerance = 0.006f;
    [Tooltip("Distancia mínima (0–1 de la cámara) entre cruces capturadas: evita capturar dos veces el mismo punto")]
    public float minSeparation = 0.08f;
    public string calibrationFile = "calibracion_libro.json";
    public bool showDebug = false;
    [Tooltip("Escribe en la consola / Player.log cada vez que la proyección se mueve (para diagnosticar rebotes)")]
    public bool logMovimientos = true;

    // ───────────── Datos guardados ─────────────
    [Serializable] class MarkerRef { public int id; public Vector2 offset; public float angle; }
    [Serializable] class CalibData { public float[] h; public List<MarkerRef> refs = new List<MarkerRef>(); }

    CalibData data = new CalibData();
    bool calibrated;
    Vector3 camRefPos;
    float camRefRot;
    Vector2 bookRef;

    // Pose actual del libro (en el marco de la proyección de referencia)
    Vector2 targetPos, curPos, velPos;
    float targetAng, curAng, velAng;
    bool hasPose;

    // Anti-rebote
    struct Sample { public Vector2 p; public float a; }
    readonly List<Sample> samples = new List<Sample>();
    int activeId = -1;
    float activeSeen = -99f;
    Vector2 candPos;
    int candCount;
    bool moving;
    Vector2 quietAnchor;
    float quietAnchorAng;
    int quietCount;

    // Último dato crudo por ID
    struct Raw { public Vector2 c, a; public float t; }
    readonly Dictionary<int, Raw> raw = new Dictionary<int, Raw>();
    int lastId = -1;

    // Calibración
    bool calibrating;
    int calibIndex;
    readonly Vector2[] calibCam = new Vector2[4];
    Vector2 stillAnchor;
    float stillSince = -1f;

    // Calibración inicial (antes del onboarding)
    bool calibracionInicial, onboardingPendiente, esperandoMarcadorInicio;
    /// El onboarding no debe empezar mientras se hace la calibración inicial.
    public bool BloqueaOnboarding => calibracionInicial;
    /// Lo llama el onboarding si intentó empezar durante la calibración: empezará después.
    public void SolicitarOnboarding() => onboardingPendiente = true;

    SpriteRenderer mask;

    string FilePath => Path.Combine(Application.persistentDataPath, calibrationFile);

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!receiver) receiver = FindFirstObjectByType<ArucoUdpReceiver>();
        camRefPos = cam.transform.position;
        camRefRot = cam.transform.eulerAngles.z;
        bookRef = book ? (Vector2)book.position : Vector2.zero;
        Load();
        if (!onboarding) onboarding = FindFirstObjectByType<OnboardingSequence>();
        var wh = FindFirstObjectByType<WordHighlighter>();
        if (wh) bookSize = wh.bookSize;   // mismo tamaño que usan los resaltados
        calibracionInicial = calibrarAntesDelOnboarding && !(omitirSiYaHayCalibracion && calibrated);
    }

    void Start()
    {
        if (calibracionInicial) StartCalibration();
    }

    /// Fin de la calibración inicial (lista o cancelada): libera el onboarding.
    void FinCalibracionInicial()
    {
        if (!calibracionInicial) return;
        calibracionInicial = false;
        if (!onboarding || !onboardingPendiente) return;   // el ArUco de inicio aún no llegó: lo iniciará como siempre
        onboardingPendiente = false;
        if (onboarding.startOnPlay) { onboarding.IniciarOnboarding(); return; }
        esperandoMarcadorInicio = true;   // la página 1 se vio durante la calibración: se espera a que vuelvan a poner el libro
        Say("Calibración lista. Coloca el libro abierto en la página 1 para empezar.");
    }

    void OnMarkerDetected(int id, Vector2 pos)
    {
        if (!esperandoMarcadorInicio || id != markerInicio || calibrating) return;
        esperandoMarcadorInicio = false;
        if (onboarding) onboarding.IniciarOnboarding();
    }

    void OnEnable() { if (receiver) { receiver.RawPoseReceived += OnRaw; receiver.MarkerDetected += OnMarkerDetected; } }
    void OnDisable()
    {
        if (receiver) { receiver.RawPoseReceived -= OnRaw; receiver.MarkerDetected -= OnMarkerDetected; }
        ResetCamera();
    }

    // ───────────── Recepción ─────────────
    void OnRaw(int id, Vector2 c, Vector2 a)
    {
        if (c.x < 0f) return;
        raw[id] = new Raw { c = c, a = a, t = Time.unscaledTime };
        lastId = id;

        if (calibrating || !calibrated) return;
        if (markerIds.Count > 0 && !markerIds.Contains(id)) return;

        Vector2 m = Map(c);
        var r = GetRef(id);
        float ang = 0f;
        if (followRotation && a.x >= 0f)
        {
            Vector2 d = Map(a) - m;
            if (d.sqrMagnitude > 1e-6f)
            {
                float measured = Mathf.DeltaAngle(r.angle, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                if (Mathf.Abs(measured) <= maxAngle) ang = measured;
                else ang = targetAng;
            }
        }
        // Centro del libro = marcador − (offset del marcador girado con el libro)
        Vector2 center = m - Rotate(r.offset, ang);

        float now = Time.unscaledTime;

        if (!hasPose)
        {
            curPos = targetPos = center;
            curAng = targetAng = ang;
            velPos = Vector2.zero; velAng = 0f;
            samples.Clear();
            samples.Add(new Sample { p = m, a = ang });
            activeId = id; activeSeen = now; candCount = 0; moving = false;
            hasPose = true;
            return;
        }

        // 1) Un solo ArUco a la vez (al pasar la hoja se ven dos y cada uno da un centro un poco distinto)
        if (id != activeId)
        {
            if (now - activeSeen < markerSwitchDelay) return;
            if (logMovimientos) Debug.Log($"[Libro] Cambio de ArUco: {activeId} → {id}");
            activeId = id;
            samples.Clear();
            candCount = 0;
        }
        activeSeen = now;

        // 2) Un salto grande aislado es una lectura errónea: solo se acepta si se repite
        if ((center - targetPos).magnitude > jumpThreshold)
        {
            if (candCount > 0 && (center - candPos).magnitude < jumpThreshold * 0.5f) candCount++;
            else { candPos = center; candCount = 1; }
            if (candCount < jumpConfirm) return;
            samples.Clear();   // confirmado: el libro sí se movió
        }
        candCount = 0;

        // 3) Mediana de las últimas lecturas (quita el temblor de la detección)
        samples.Add(new Sample { p = m, a = ang });
        while (samples.Count > Mathf.Max(1, medianSamples)) samples.RemoveAt(0);
        Vector2 mm = MedianPos();   // marcador
        float ma = MedianAng();
        // Libro quieto y giro menor a la zona muerta: se usa el ángulo ya fijado, así el ruido del ángulo
        // no mueve el centro (el ArUco está lejos del centro y cada grado de error lo desplaza)
        if (!moving && Mathf.Abs(Mathf.DeltaAngle(targetAng, ma)) <= deadzoneAngle) ma = targetAng;
        Vector2 mp = mm - Rotate(r.offset, ma);   // centro del libro

        // 4) Histéresis: quieto → solo se mueve si el cambio supera la zona muerta; moviéndose → sigue cada lectura
        //    y vuelve a 'quieto' cuando el libro se detiene medio segundo
        float dPos = (mp - targetPos).magnitude, dAng = Mathf.Abs(Mathf.DeltaAngle(targetAng, ma));
        if (!moving)
        {
            if (dPos <= deadzone && dAng <= deadzoneAngle) return;
            moving = true;
            if (logMovimientos) Debug.Log($"[Libro] Se mueve la proyección: ArUco {id}, desplazamiento {dPos:0.000}, giro {dAng:0.0}° (cámara {c.x:0.000},{c.y:0.000})");
            quietAnchor = mp; quietAnchorAng = ma; quietCount = 0;
        }
        targetPos = mp;
        targetAng = ma;
        // ¿Se detuvo? (varias lecturas seguidas cerca del mismo punto)
        if ((mp - quietAnchor).magnitude < deadzone * 0.5f && Mathf.Abs(Mathf.DeltaAngle(quietAnchorAng, ma)) < deadzoneAngle * 0.5f)
        {
            if (++quietCount >= quietSamples) moving = false;
        }
        else { quietAnchor = mp; quietAnchorAng = ma; quietCount = 0; }
    }

    Vector2 MedianPos()
    {
        var xs = new List<float>(samples.Count); var ys = new List<float>(samples.Count);
        foreach (var s in samples) { xs.Add(s.p.x); ys.Add(s.p.y); }
        return new Vector2(Median(xs), Median(ys));
    }

    float MedianAng()
    {
        float a0 = samples[0].a;
        var ds = new List<float>(samples.Count);
        foreach (var s in samples) ds.Add(Mathf.DeltaAngle(a0, s.a));
        return a0 + Median(ds);
    }

    static float Median(List<float> v)
    {
        v.Sort();
        int m = v.Count / 2;
        return v.Count % 2 == 1 ? v[m] : 0.5f * (v[m - 1] + v[m]);
    }

    MarkerRef GetRef(int id)
    {
        foreach (var r in data.refs) if (r.id == id) return r;
        // Posición del ArUco en el PDF → unidades del libro (centro del libro = 0,0; y hacia arriba)
        foreach (var mp in markerPositions)
            if (mp.id == id)
                return new MarkerRef { id = id, offset = new Vector2((mp.pdfCenter.x - 0.5f) * bookSize.x, (0.5f - mp.pdfCenter.y) * bookSize.y), angle = 0f };
        // Último recurso: donde está MarcoMarcador
        Vector2 off = markerInBook && book ? (Vector2)(markerInBook.position - book.position) : Vector2.zero;
        return new MarkerRef { id = id, offset = off, angle = 0f };
    }

    // ───────────── Cada cuadro ─────────────
    void Update()
    {
        HandleKeys();
        UpdateMask();
        if (calibrating) { UpdateCalibration(); return; }

        if (!follow || !calibrated || !hasPose) return;
        float dt = Time.unscaledDeltaTime;
        curPos = Vector2.SmoothDamp(curPos, targetPos, ref velPos, smoothTime, Mathf.Infinity, dt);
        curAng = Mathf.SmoothDampAngle(curAng, targetAng, ref velAng, smoothTime, Mathf.Infinity, dt);
        ApplyCamera(curPos, followRotation ? curAng : 0f);
    }

    /// Cuadro negro sobre el ArUco activo (hijo de LibroReferencia: sigue al libro junto con todo lo demás).
    void UpdateMask()
    {
        int id = activeId >= 0 ? activeId : lastId;
        bool show = taparArUco && !calibrating && book && id >= 0 && (markerIds.Count == 0 || markerIds.Contains(id));
        if (!show) { if (mask) mask.enabled = false; return; }
        if (!mask)
        {
            var tex = new Texture2D(4, 4);
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            var go = new GameObject("TapaArUco");
            go.transform.SetParent(book, false);
            mask = go.AddComponent<SpriteRenderer>();
            mask.sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            mask.color = Color.black;
            mask.sortingOrder = 32000;   // encima de todo lo proyectado
        }
        var r = GetRef(id);
        mask.transform.localPosition = new Vector3(r.offset.x, r.offset.y, 0f);
        mask.transform.localRotation = Quaternion.Euler(0f, 0f, r.angle);
        mask.transform.localScale = new Vector3(markerPdfSize.x * bookSize.x * maskScale, markerPdfSize.y * bookSize.y * maskScale, 1f);
        mask.enabled = true;
    }

    /// Mueve la cámara para que la escena (alineada con el libro en bookRef) caiga sobre el libro en (pos, ang).
    void ApplyCamera(Vector2 pos, float ang)
    {
        Vector2 c0 = camRefPos;
        Vector2 c = bookRef - Rotate(pos - c0, -ang);
        cam.transform.position = new Vector3(c.x, c.y, camRefPos.z);
        cam.transform.rotation = Quaternion.Euler(0f, 0f, camRefRot - ang);
    }

    public void ResetCamera()
    {
        if (!cam) return;
        cam.transform.position = camRefPos;
        cam.transform.rotation = Quaternion.Euler(0f, 0f, camRefRot);
    }

    // ───────────── Teclas ─────────────
    void HandleKeys()
    {
        bool k = false, j = false, l = false, enter = false;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null)
        {
            k = kb.kKey.wasPressedThisFrame; j = kb.jKey.wasPressedThisFrame; l = kb.lKey.wasPressedThisFrame;
            enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        k = Input.GetKeyDown(KeyCode.K); j = Input.GetKeyDown(KeyCode.J); l = Input.GetKeyDown(KeyCode.L);
        enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
        if (k) { if (calibrating) CancelCalibration(); else StartCalibration(); }
        if (j && !calibrating) SetReference();
        if (l && !calibrating)
        {
            follow = !follow;
            if (!follow) ResetCamera();
            Say(follow ? "Seguimiento del libro: ACTIVADO" : "Seguimiento del libro: desactivado (proyección fija)");
        }
        if (enter && calibrating) TryCapture(force: true);
    }

    // ───────────── Calibración ─────────────
    [ContextMenu("Calibrar cámara → proyección")]
    public void StartCalibration()
    {
        calibrating = true;
        calibIndex = 0;
        stillSince = -1f;
        ResetCamera();
        Say("Calibración: pon el centro del ArUco sobre la cruz 1 y no lo muevas.");
    }

    void CancelCalibration()
    {
        calibrating = false;
        Say(calibracionInicial ? (calibrated ? "Calibración cancelada: se usa la calibración guardada." : "Calibración cancelada: la proyección queda fija.") : "Calibración cancelada.");
        FinCalibracionInicial();
    }

    void UpdateCalibration() => TryCapture(force: false);

    void TryCapture(bool force)
    {
        if (lastId < 0 || !raw.TryGetValue(lastId, out var r) || Time.unscaledTime - r.t > 0.8f)
        {
            stillSince = -1f;
            if (force) Say("No se ve ningún ArUco: revisa la cámara / el script de Python.");
            return;
        }
        // Debe estar lejos de las cruces ya capturadas (si no, sería el mismo punto otra vez)
        for (int i = 0; i < calibIndex; i++)
        {
            if ((r.c - calibCam[i]).magnitude < minSeparation)
            {
                stillSince = -1f;
                if (force) Say($"El ArUco sigue donde la cruz {i + 1}: muévelo a la cruz {calibIndex + 1}.");
                return;
            }
        }
        if (!force)
        {
            if (stillSince < 0f || (r.c - stillAnchor).magnitude > stillTolerance)
            {
                stillAnchor = r.c;
                stillSince = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - stillSince < holdToCapture) return;
        }

        calibCam[calibIndex] = r.c;
        calibIndex++;
        stillSince = -1f;
        if (calibIndex < 4)
        {
            Say($"Cruz {calibIndex} capturada. Mueve el ArUco a la cruz {calibIndex + 1}.");
            return;
        }

        var world = new Vector2[4];
        for (int i = 0; i < 4; i++) world[i] = ViewportToRefWorld(calibrationViewport[i]);
        var h = SolveHomography(calibCam, world);
        calibrating = false;
        if (h == null)
        {
            if (calibracionInicial) { StartCalibration(); Say("No se pudo calcular la calibración: empieza de nuevo desde la cruz 1."); }
            else Say("No se pudo calcular la calibración (¿puntos repetidos?). Repite con K.");
            return;
        }
        data.h = h;
        calibrated = true;
        hasPose = false;
        Save();
        Say("Calibración lista y guardada. Mueve el libro: la proyección lo seguirá.");
        FinCalibracionInicial();
    }

    Vector2 ViewportToRefWorld(Vector2 v)
    {
        float hh = cam.orthographicSize, hw = hh * cam.aspect;
        return (Vector2)camRefPos + new Vector2((v.x - 0.5f) * 2f * hw, (v.y - 0.5f) * 2f * hh);
    }

    /// J: el libro está justo sobre LibroReferencia → guarda dónde está el marcador visible dentro del libro.
    [ContextMenu("Fijar referencia (libro sobre LibroReferencia)")]
    public void SetReference()
    {
        if (!calibrated) { Say("Primero calibra con K."); return; }
        if (lastId < 0 || !raw.TryGetValue(lastId, out var r) || Time.unscaledTime - r.t > 0.8f) { Say("No se ve ningún ArUco."); return; }
        Vector2 m = Map(r.c);
        float ang = 0f;
        if (r.a.x >= 0f) { Vector2 d = Map(r.a) - m; ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; }
        data.refs.RemoveAll(x => x.id == lastId);
        data.refs.Add(new MarkerRef { id = lastId, offset = m - bookRef, angle = ang });
        Save();
        hasPose = false;
        Say($"Referencia del marcador {lastId} guardada (posición en el libro {m - bookRef:0.00}, ángulo {ang:0.0}°).");
    }

    [ContextMenu("Borrar calibración")]
    public void ClearCalibration()
    {
        data = new CalibData();
        calibrated = false;
        hasPose = false;
        ResetCamera();
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
        Say("Calibración borrada.");
    }

    void Save()
    {
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(data, true)); }
        catch (Exception e) { Debug.LogWarning("[Libro] No se pudo guardar la calibración: " + e.Message); }
    }

    void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { Debug.Log($"[Libro] Sin calibración ({FilePath}). Presiona K para calibrar."); return; }
            data = JsonUtility.FromJson<CalibData>(File.ReadAllText(FilePath)) ?? new CalibData();
            if (data.refs == null) data.refs = new List<MarkerRef>();
            calibrated = data.h != null && data.h.Length == 9;
            Debug.Log($"[Libro] Calibración cargada de {FilePath}.");
        }
        catch (Exception e) { Debug.LogWarning("[Libro] No se pudo leer la calibración: " + e.Message); }
    }

    // ───────────── Matemática ─────────────
    Vector2 Map(Vector2 p)
    {
        var h = data.h;
        float w = h[6] * p.x + h[7] * p.y + h[8];
        return new Vector2((h[0] * p.x + h[1] * p.y + h[2]) / w, (h[3] * p.x + h[4] * p.y + h[5]) / w);
    }

    static Vector2 Rotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
    }

    /// Homografía de 4 puntos (cámara 0–1 → mundo), h8 = 1.
    static float[] SolveHomography(Vector2[] src, Vector2[] dst)
    {
        var A = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            double x = src[i].x, y = src[i].y, u = dst[i].x, v = dst[i].y;
            int r = 2 * i;
            A[r, 0] = x; A[r, 1] = y; A[r, 2] = 1; A[r, 6] = -u * x; A[r, 7] = -u * y; A[r, 8] = u;
            A[r + 1, 3] = x; A[r + 1, 4] = y; A[r + 1, 5] = 1; A[r + 1, 6] = -v * x; A[r + 1, 7] = -v * y; A[r + 1, 8] = v;
        }
        for (int col = 0; col < 8; col++)
        {
            int piv = col;
            for (int r = col + 1; r < 8; r++) if (Math.Abs(A[r, col]) > Math.Abs(A[piv, col])) piv = r;
            if (Math.Abs(A[piv, col]) < 1e-12) return null;
            if (piv != col) for (int k = 0; k < 9; k++) { var t = A[col, k]; A[col, k] = A[piv, k]; A[piv, k] = t; }
            for (int r = 0; r < 8; r++)
            {
                if (r == col) continue;
                double f = A[r, col] / A[col, col];
                for (int k = col; k < 9; k++) A[r, k] -= f * A[col, k];
            }
        }
        var h = new float[9];
        for (int i = 0; i < 8; i++) h[i] = (float)(A[i, 8] / A[i, i]);
        h[8] = 1f;
        return h;
    }

    // ───────────── Pantalla ─────────────
    string msg = "";
    float msgUntil;
    void Say(string s) { msg = s; msgUntil = Time.unscaledTime + 6f; Debug.Log("[Libro] " + s); }

    GUIStyle style;
    void OnGUI()
    {
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };

        if (calibrating)
        {
            // Fondo negro: solo se proyecta la cruz
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            Vector2 v = calibrationViewport[calibIndex];
            float x = v.x * Screen.width, y = (1f - v.y) * Screen.height;
            bool holding = stillSince >= 0f;
            GUI.color = holding ? new Color(0.4f, 1f, 0.5f) : Color.white;
            GUI.DrawTexture(new Rect(x - 60, y - 2, 120, 4), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 2, y - 60, 4, 120), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, Screen.height * 0.45f, Screen.width, 80),
                $"Calibración {calibIndex + 1}/4 · centro del ArUco sobre la cruz y quieto (Enter = capturar · K = cancelar)", style);
        }

        if (Time.unscaledTime < msgUntil)
        {
            GUI.color = Color.white;
            GUI.Label(new Rect(0, Screen.height - 70, Screen.width, 50), msg, style);
        }
        if (showDebug)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(new Rect(16, 16, 900, 30),
                $"Libro: {(calibrated ? "calibrado" : "SIN calibrar (K)")} · seguir {(follow ? "sí" : "no")} · ArUco {activeId} · {(moving ? "moviéndose" : "quieto")} · pos {curPos:0.00} · ángulo {curAng:0.0}°");
        }
    }
}
