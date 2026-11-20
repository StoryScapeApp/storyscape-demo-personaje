using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Prueba "Detección de páginas": ¿el sistema reconoce la doble página correcta en condiciones de aula?
///
/// Versión con seguimiento del libro: la proyección se mueve con el libro (BookPoseFollower), así que además de la
/// página se mide si la proyección se queda quieta sobre el libro o "rebota".
///
/// Cómo se usa (detalle en Pruebas/deteccion_paginas_seguimiento/LEEME.md):
///  1. Cierra aruco_sender.py y abre python/grabar_deteccion.py (usa la cámara del sistema, envía los ArUco
///     a Unity igual que el sistema y graba el video cuando Unity se lo pide).
///  2. Activa el objeto "PruebaDeteccion", da Play y calibra la proyección (K) si no está calibrada. El proyector repite el onboarding y el vuelo del Principito
///     (contenido real; el vuelo solo si "Incluir Vuelo" está marcado), sin lectura en vivo, quiz ni dudas.
///  3. Con el libro abierto en la página 1: N empieza el video. Espacio = nueva doble página a la vista,
///     P = empieza a pasar la hoja, F = fin del video (después de la página 7), R = repetir el video.
///     ←/→ eligen el video, H oculta el panel.
///
/// Resultados en Pruebas/deteccion_paginas_seguimiento/:
///   etiquetas_&lt;sesion&gt;.csv  video,inicio_s,fin_s,pagina_esperada (tramos de página y de "transicion")
///   eventos_&lt;sesion&gt;.csv    cada mensaje ArUco, cada confirmación (2 en 1,5 s) y cada cambio de página
///   resumen_&lt;sesion&gt;.csv    por tramo: detectada, latencia, % del tiempo con la página correcta, falsas detecciones
///                              y estabilidad de la proyección (movimientos, recorrido, desvío máximo)
///   proyeccion_&lt;sesion&gt;.csv  posición y giro de la proyección cada 0,1 s
/// Los tiempos son segundos desde el primer cuadro del video (el grabador avisa cuándo empezó).
/// </summary>
public class PruebaDeteccionPaginas : MonoBehaviour
{
    [Header("Sistema")]
    public ArucoUdpReceiver receiver;
    public BookPageTracker tracker;
    [Tooltip("Seguimiento del libro (SeguimientoLibro). Vacío = se busca solo")]
    public BookPoseFollower seguimiento;
    [Tooltip("No deja empezar un video (N) si el seguimiento está activo y la proyección no está calibrada")]
    public bool exigirCalibracion = true;
    [Tooltip("Cada cuánto se guarda la posición de la proyección (s)")]
    public float muestreoProyeccion = 0.1f;

    [Header("Contenido proyectado durante la prueba")]
    [Tooltip("Repite el onboarding y el vuelo del Principito sin esperar el ArUco, para que el proyector ilumine el libro como en el aula")]
    public bool proyectarContenido = true;
    public OnboardingSequence onboarding;
    [Tooltip("Agrega el vuelo del Principito (animación 1) entre onboardings. Desmarcado = solo onboarding")]
    public bool incluirVuelo = false;
    public PrincipitoVolando vuelo;
    [Tooltip("Segundos sin proyección entre un onboarding y el siguiente")]
    public float pausaEntreRepeticiones = 0.3f;
    [Tooltip("Se desactivan durante la prueba: la lectura en vivo (Scribe), el quiz y las dudas no deben conectarse, usar el micrófono ni las teclas")]
    public Behaviour[] desactivarDurantePrueba = new Behaviour[0];

    [Header("Diseño: luces × posiciones × repeticiones")]
    public string[] luces = { "luzBaja", "luzNormal", "luzAlta" };
    public string[] posiciones = { "centrado", "rotado", "oclusion" };
    [Tooltip("De preferencia, una repetición por autor")]
    public int repeticiones = 2;
    public int paginas = 7;

    [Header("Grabador (python/grabar_deteccion.py)")]
    public bool usarGrabador = true;
    [Tooltip("Puerto en el que escucha el grabador (Unity le envía grabar / parar / descartar)")]
    public int puertoGrabador = 5007;
    [Tooltip("Segundos que se espera la confirmación del grabador antes de empezar sin él")]
    public float esperaGrabador = 3f;

    [Header("Resultados")]
    [Tooltip("Vacío = <carpeta del proyecto o del .exe>/Pruebas/deteccion_paginas_seguimiento")]
    public string carpeta = "";
    public bool mostrarPanel = true;

    // ───────────── Estado ─────────────
    class VideoDef { public string nombre, luz, posicion; public int rep; }
    class Tramo { public double inicio, fin = -1; public int pagina; public int desde; }   // pagina = 0 → transición (desde = página anterior)
    struct Ev { public double t; public string tipo; public int marcador; public int pagina; }
    struct Muestra { public double t; public Vector2 pos; public float ang; }
    enum Estado { Libre, EsperandoGrabador, Grabando }

    readonly List<VideoDef> videos = new List<VideoDef>();
    readonly List<Tramo> tramos = new List<Tramo>();
    readonly List<Ev> eventos = new List<Ev>();
    readonly List<Muestra> muestras = new List<Muestra>();
    readonly HashSet<string> hechos = new HashSet<string>();
    readonly List<KeyValuePair<Behaviour, bool>> desactivados = new List<KeyValuePair<Behaviour, bool>>();
    readonly List<(UnityEventBase ev, int i, UnityEventCallState antes)> eventosApagados = new List<(UnityEventBase, int, UnityEventCallState)>();

    Estado estado = Estado.Libre;
    int idx;
    VideoDef actual;
    int paginaVista;
    double t0, esperaHasta;
    int paginaInicialSistema;
    int ultimoMarcador = -1;
    bool grabadorListo;
    string infoGrabador = "sin respuesta";
    double proximoPing;
    string aviso = "";
    double avisoHasta;
    string dir, sesion, fEventos, fEtiquetas, fResumen, fProyeccion;
    double proximaMuestra;
    UdpClient udp;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Serializable] class AvisoGrabador { public string tipo; public string video; public float fps; public int cuadros; public float duracion_s; public int ancho; public int alto; }

    double Ahora => Time.realtimeSinceStartupAsDouble;
    double T => Ahora - t0;

    // ───────────── Ciclo de vida ─────────────
    /// En un build: abre el .exe con -pruebaDeteccion para activar la prueba (sin el parámetro, el build funciona normal).
    /// Opcional: -carpetaPrueba "C:\ruta" para guardar los resultados en otra carpeta.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ActivarDesdeLineaDeComandos()
    {
        var args = Environment.GetCommandLineArgs();
        if (Array.FindIndex(args, a => string.Equals(a, "-pruebaDeteccion", StringComparison.OrdinalIgnoreCase)) < 0) return;
        int j = Array.FindIndex(args, a => string.Equals(a, "-carpetaPrueba", StringComparison.OrdinalIgnoreCase));
        foreach (var c in Resources.FindObjectsOfTypeAll<PruebaDeteccionPaginas>())
        {
            if (!c.gameObject.scene.IsValid()) continue;
            if (j >= 0 && j + 1 < args.Length) c.carpeta = args[j + 1];
            c.gameObject.SetActive(true);   // su Awake desactiva la lectura, el quiz y las dudas antes de que arranquen
            Debug.Log("[Prueba detección] Activada con -pruebaDeteccion.");
            return;
        }
        Debug.LogWarning("[Prueba detección] -pruebaDeteccion: no está el objeto PruebaDeteccion en la escena.");
    }

    void Awake()
    {
        if (!receiver) receiver = FindFirstObjectByType<ArucoUdpReceiver>();
        if (!tracker) tracker = FindFirstObjectByType<BookPageTracker>();
        if (!onboarding) onboarding = FindFirstObjectByType<OnboardingSequence>();
        if (!vuelo) vuelo = FindFirstObjectByType<PrincipitoVolando>();
        if (!seguimiento) seguimiento = FindFirstObjectByType<BookPoseFollower>();

        foreach (var luz in luces)
            foreach (var pos in posiciones)
                for (int r = 1; r <= Mathf.Max(1, repeticiones); r++)
                    videos.Add(new VideoDef { nombre = $"{luz}_{pos}_rep{r}.mp4", luz = luz, posicion = pos, rep = r });

        // Desactiva lo que no debe correr durante la prueba (antes de su Start: no abren el micrófono ni Scribe)
        foreach (var b in desactivarDurantePrueba)
            if (b) { desactivados.Add(new KeyValuePair<Behaviour, bool>(b, b.enabled)); b.enabled = false; }
        if (onboarding)
        {
            onboarding.startOnPlay = false;
            onboarding.esperarRespuestaFinal = false;   // no espera la respuesta del niño (micrófono)
            // Al empezar/terminar, el onboarding activa la lectura en vivo, las dudas y la página simulada: se apaga durante la prueba
            Apagar(onboarding.onStarted);
            Apagar(onboarding.onFinished);
        }

        dir = string.IsNullOrWhiteSpace(carpeta)
            ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Pruebas", "deteccion_paginas_seguimiento")
            : carpeta;
        Directory.CreateDirectory(dir);
        sesion = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        fEventos = Path.Combine(dir, $"eventos_{sesion}.csv");
        fEtiquetas = Path.Combine(dir, $"etiquetas_{sesion}.csv");
        fResumen = Path.Combine(dir, $"resumen_{sesion}.csv");
        fProyeccion = Path.Combine(dir, $"proyeccion_{sesion}.csv");
        File.WriteAllText(fEventos, "video,t_s,unix_ms,evento,marcador,pagina\n");
        File.WriteAllText(fEtiquetas, "video,inicio_s,fin_s,pagina_esperada\n");
        File.WriteAllText(fResumen, "video,luz,posicion,rep,tramo,pagina_esperada,inicio_s,fin_s,duracion_s,detectada,latencia_s," +
                                    "pct_tiempo_pagina_correcta,mensajes_correctos,mensajes_otros,confirmaciones_incorrectas,cambios_de_pagina," +
                                    "movimientos_proyeccion,recorrido_proyeccion,desvio_max_proyeccion\n");
        File.WriteAllText(fProyeccion, "video,t_s,x,y,angulo,moviendose,aruco\n");
        Debug.Log($"[Prueba detección] {videos.Count} videos. Resultados en {dir} (sesión {sesion}). " +
                  $"Criterio del sistema: {(receiver ? receiver.minHits : 2)} mensajes en {(receiver ? receiver.hitWindow : 1.5f)} s. " +
                  (seguimiento ? "Seguimiento del libro: sí." : "Sin seguimiento del libro (no está SeguimientoLibro)."));
    }

    void OnEnable()
    {
        udp = new UdpClient();
        if (receiver)
        {
            receiver.RawMarkerReceived += OnRaw;
            receiver.MarkerDetected += OnConfirmado;
            receiver.MessageReceived += OnMensaje;
        }
        if (tracker) tracker.PageChanged += OnPagina;
        if (seguimiento) seguimiento.MovimientoIniciado += OnMovProyeccion;
    }

    void Start()
    {
        if (proyectarContenido && (onboarding || (vuelo && incluirVuelo))) StartCoroutine(Contenido());
        Ping();
    }

    void OnDisable()
    {
        if (estado != Estado.Libre) Enviar("parar");
        if (receiver)
        {
            receiver.RawMarkerReceived -= OnRaw;
            receiver.MarkerDetected -= OnConfirmado;
            receiver.MessageReceived -= OnMensaje;
        }
        if (tracker) tracker.PageChanged -= OnPagina;
        if (seguimiento) seguimiento.MovimientoIniciado -= OnMovProyeccion;
        StopAllCoroutines();
        foreach (var kv in desactivados) if (kv.Key) kv.Key.enabled = kv.Value;
        desactivados.Clear();
        foreach (var e in eventosApagados) e.ev.SetPersistentListenerState(e.i, e.antes);
        eventosApagados.Clear();
        try { udp?.Close(); } catch { }
        udp = null;
    }

    void Apagar(UnityEventBase ev)
    {
        if (ev == null) return;
        for (int i = 0; i < ev.GetPersistentEventCount(); i++)
        {
            eventosApagados.Add((ev, i, ev.GetPersistentListenerState(i)));
            ev.SetPersistentListenerState(i, UnityEventCallState.Off);
        }
    }

    /// Proyecta contenido real en bucle: onboarding completo y luego el vuelo del Principito.
    IEnumerator Contenido()
    {
        yield return new WaitForSeconds(0.5f);
        while (true)
        {
            while (seguimiento && (seguimiento.BloqueaOnboarding || seguimiento.Calibrando)) yield return null;
            if (onboarding)
            {
                onboarding.IniciarOnboarding();
                yield return null;
                while (onboarding.IsRunning) yield return null;
            }
            if (vuelo && incluirVuelo)
            {
                vuelo.Play();
                yield return new WaitForSeconds(6f);
            }
            yield return new WaitForSeconds(pausaEntreRepeticiones);
        }
    }

    // ───────────── Teclas ─────────────
    void Update()
    {
        if (estado == Estado.Libre && usarGrabador && !grabadorListo && Ahora >= proximoPing) Ping();
        if (estado == Estado.Grabando) MuestrearProyeccion();
        if (estado == Estado.EsperandoGrabador && Ahora >= esperaHasta)
        {
            Avisar("El grabador no respondió: este video NO se está grabando. Abre python/grabar_deteccion.py o graba aparte. " +
                   "Los tiempos empiezan ahora.", 8f);
            Comenzar(esperaHasta - esperaGrabador);
        }

#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return;
        if (k.hKey.wasPressedThisFrame) mostrarPanel = !mostrarPanel;
        switch (estado)
        {
            case Estado.Libre:
                if (k.nKey.wasPressedThisFrame) { if (PuedeEmpezar(out var motivo)) IniciarVideo(); else Avisar(motivo, 6f); }
                else if (k.rightArrowKey.wasPressedThisFrame) idx = Mathf.Min(idx + 1, videos.Count - 1);
                else if (k.leftArrowKey.wasPressedThisFrame) idx = Mathf.Max(idx - 1, 0);
                break;
            case Estado.EsperandoGrabador:
                if (k.rKey.wasPressedThisFrame) Descartar();
                break;
            case Estado.Grabando:
                if (k.spaceKey.wasPressedThisFrame) PaginaALaVista();
                else if (k.pKey.wasPressedThisFrame) PasarHoja();
                else if (k.fKey.wasPressedThisFrame) Terminar();
                else if (k.rKey.wasPressedThisFrame) Descartar();
                break;
        }
#endif
    }

    /// Con el seguimiento activo, la proyección debe estar calibrada (si no, no sigue al libro y la prueba no mide el sistema real).
    bool PuedeEmpezar(out string motivo)
    {
        motivo = "";
        if (!seguimiento || !exigirCalibracion || !seguimiento.follow) return true;
        if (seguimiento.Calibrando) { motivo = "Termina la calibración de la proyección antes de grabar."; return false; }
        if (!seguimiento.Calibrado) { motivo = "La proyección no está calibrada: presiona K y calibra antes de grabar (o L para probar sin seguimiento)."; return false; }
        return true;
    }

    void IniciarVideo()
    {
        if (videos.Count == 0) return;
        actual = videos[idx];
        tramos.Clear();
        eventos.Clear();
        muestras.Clear();
        if (usarGrabador)
        {
            Enviar("grabar", actual.nombre);
            estado = Estado.EsperandoGrabador;
            esperaHasta = Ahora + esperaGrabador;
        }
        else Comenzar(Ahora);
    }

    void Comenzar(double inicio)
    {
        t0 = inicio;
        estado = Estado.Grabando;
        paginaVista = 1;
        paginaInicialSistema = tracker ? tracker.CurrentPage : 0;
        muestras.Clear();
        proximaMuestra = 0;
        tramos.Add(new Tramo { inicio = 0, pagina = 1 });
        Registrar("inicio_video", -1, 1);
        Registrar("pagina_sistema", -1, paginaInicialSistema);
    }

    /// Espacio: una nueva doble página quedó a la vista.
    void PaginaALaVista()
    {
        var abierto = tramos[tramos.Count - 1];
        if (abierto.pagina > 0 && abierto.fin < 0 && paginaVista >= paginas)
        {
            Avisar($"Ya van las {paginas} páginas: presiona F para terminar el video.");
            return;
        }
        if (paginaVista >= paginas) { Avisar("Ya se marcaron todas las páginas: presiona F."); return; }
        double t = T;
        abierto.fin = t;
        paginaVista++;
        tramos.Add(new Tramo { inicio = t, pagina = paginaVista });
        Registrar("tecla_pagina_a_la_vista", -1, paginaVista);
    }

    /// P: empieza a pasar la hoja.
    void PasarHoja()
    {
        var abierto = tramos[tramos.Count - 1];
        if (abierto.pagina == 0) return;             // ya se está pasando
        if (paginaVista >= paginas) { Avisar("Es la última página: presiona F para terminar."); return; }
        double t = T;
        abierto.fin = t;
        tramos.Add(new Tramo { inicio = t, pagina = 0, desde = paginaVista });
        Registrar("tecla_pasar_hoja", -1, paginaVista);
    }

    /// F: termina el video, guarda las etiquetas y el resumen.
    void Terminar()
    {
        double t = T;
        var abierto = tramos[tramos.Count - 1];
        if (abierto.fin < 0) abierto.fin = t;
        Registrar("fin_video", -1, paginaVista);
        if (usarGrabador) Enviar("parar");

        var et = new StringBuilder();
        foreach (var tr in tramos)
            et.Append(actual.nombre).Append(',').Append(F(tr.inicio)).Append(',').Append(F(tr.fin)).Append(',')
              .Append(tr.pagina > 0 ? tr.pagina.ToString(Inv) : "transicion").Append('\n');
        File.AppendAllText(fEtiquetas, et.ToString());

        int detectadas = 0, nPag = 0, falsas = 0, movPag = 0;
        var rs = new StringBuilder();
        int n = 0;
        foreach (var tr in tramos)
        {
            n++;
            var r = Resumir(tr);
            if (tr.pagina > 0) { nPag++; if (r.detectada) detectadas++; movPag += r.movimientos; }
            falsas += r.incorrectas;
            rs.Append(actual.nombre).Append(',').Append(actual.luz).Append(',').Append(actual.posicion).Append(',').Append(actual.rep).Append(',')
              .Append(n).Append(',').Append(tr.pagina > 0 ? tr.pagina.ToString(Inv) : "transicion").Append(',')
              .Append(F(tr.inicio)).Append(',').Append(F(tr.fin)).Append(',').Append(F(tr.fin - tr.inicio)).Append(',')
              .Append(tr.pagina > 0 ? (r.detectada ? "1" : "0") : "").Append(',')
              .Append(r.latencia >= 0 ? F(r.latencia) : "").Append(',')
              .Append(tr.pagina > 0 ? r.pctCorrecta.ToString("0.000", Inv) : "").Append(',')
              .Append(r.mensajesOk).Append(',').Append(r.mensajesOtros).Append(',').Append(r.incorrectas).Append(',').Append(r.cambios).Append(',')
              .Append(seguimiento ? r.movimientos.ToString(Inv) : "").Append(',')
              .Append(r.nMuestras > 0 ? F(r.recorrido) : "").Append(',')
              .Append(r.nMuestras > 0 ? F(r.desvioMax) : "").Append('\n');
        }
        File.AppendAllText(fResumen, rs.ToString());

        hechos.Add(actual.nombre);
        string mov = seguimiento ? $" · la proyección se movió {movPag} vez/veces con una página a la vista" : "";
        Avisar($"{actual.nombre}: {detectadas}/{nPag} páginas detectadas · {falsas} confirmaciones incorrectas{mov}. Guardado.", 10f);
        Debug.Log($"[Prueba detección] {actual.nombre}: {detectadas}/{nPag} páginas detectadas, {falsas} confirmaciones incorrectas{mov}.");
        estado = Estado.Libre;
        if (idx < videos.Count - 1) idx++;
        actual = null;
    }

    /// R: descarta el video en curso (no se guardan etiquetas ni resumen; los eventos quedan marcados como descartados).
    void Descartar()
    {
        if (usarGrabador) Enviar("descartar");
        if (estado == Estado.Grabando) Registrar("descartado", -1, paginaVista);
        Avisar($"{actual?.nombre} descartado: vuelve a la página 1 y presiona N para repetirlo.");
        estado = Estado.Libre;
        actual = null;
    }

    // ───────────── Resumen por tramo ─────────────
    struct Res
    {
        public bool detectada; public double latencia; public double pctCorrecta; public int mensajesOk, mensajesOtros, incorrectas, cambios;
        public int movimientos, nMuestras; public double recorrido, desvioMax;
    }

    Res Resumir(Tramo tr)
    {
        var r = new Res { latencia = -1 };
        int esperado = tr.pagina - 1;                                  // página N = marcador N-1
        int antes = tr.desde - 1, despues = tr.desde;                  // en una transición: la página que se deja y la siguiente
        foreach (var e in eventos)
        {
            if (e.t < tr.inicio || e.t > tr.fin) continue;
            if (e.tipo == "aruco")
            {
                if (tr.pagina > 0 ? e.marcador == esperado : (e.marcador == antes || e.marcador == despues)) r.mensajesOk++;
                else r.mensajesOtros++;
            }
            else if (e.tipo == "confirmado")
            {
                bool ok = tr.pagina > 0 ? e.marcador == esperado : (e.marcador == antes || e.marcador == despues);
                if (ok && tr.pagina > 0 && !r.detectada) { r.detectada = true; r.latencia = e.t - tr.inicio; }
                if (!ok) r.incorrectas++;
            }
            else if (e.tipo == "cambio_pagina") r.cambios++;
            else if (e.tipo == "mov_proyeccion") r.movimientos++;
        }
        EstabilidadProyeccion(tr, ref r);
        if (tr.pagina > 0 && tr.fin > tr.inicio)
        {
            // Página del sistema a lo largo del tramo
            int pag = paginaInicialSistema;
            double desde = tr.inicio, correcto = 0;
            foreach (var e in eventos)
            {
                if (e.tipo != "cambio_pagina") continue;
                if (e.t <= tr.inicio) { pag = e.pagina; continue; }
                if (e.t >= tr.fin) break;
                if (pag == tr.pagina) correcto += e.t - desde;
                pag = e.pagina;
                desde = e.t;
            }
            if (pag == tr.pagina) correcto += tr.fin - desde;
            r.pctCorrecta = correcto / (tr.fin - tr.inicio);
        }
        return r;
    }

    /// Recorrido de la proyección dentro del tramo y su mayor distancia a la posición mediana del tramo.
    /// Con el libro quieto (página a la vista, posición "centrado") ambos deberían ser casi 0: si no, la proyección rebota.
    void EstabilidadProyeccion(Tramo tr, ref Res r)
    {
        var xs = new List<float>(); var ys = new List<float>();
        Vector2? prev = null;
        foreach (var m in muestras)
        {
            if (m.t < tr.inicio || m.t > tr.fin) continue;
            if (prev.HasValue) r.recorrido += (m.pos - prev.Value).magnitude;
            prev = m.pos; xs.Add(m.pos.x); ys.Add(m.pos.y);
        }
        r.nMuestras = xs.Count;
        if (xs.Count == 0) return;
        var med = new Vector2(Mediana(xs), Mediana(ys));
        foreach (var m in muestras)
            if (m.t >= tr.inicio && m.t <= tr.fin) r.desvioMax = Math.Max(r.desvioMax, (m.pos - med).magnitude);
    }

    static float Mediana(List<float> v)
    {
        v.Sort();
        int k = v.Count / 2;
        return v.Count % 2 == 1 ? v[k] : 0.5f * (v[k - 1] + v[k]);
    }

    void MuestrearProyeccion()
    {
        if (!seguimiento || !seguimiento.TienePose || Ahora < proximaMuestra) return;
        proximaMuestra = Ahora + Mathf.Max(0.02f, muestreoProyeccion);
        double t = T;
        var m = new Muestra { t = t, pos = seguimiento.PosicionLibro, ang = seguimiento.AnguloLibro };
        muestras.Add(m);
        try
        {
            File.AppendAllText(fProyeccion, $"{actual?.nombre},{F(t)},{m.pos.x.ToString("0.0000", Inv)},{m.pos.y.ToString("0.0000", Inv)}," +
                                            $"{m.ang.ToString("0.00", Inv)},{(seguimiento.Moviendose ? 1 : 0)},{seguimiento.ArucoActivo}\n");
        }
        catch (Exception e) { Debug.LogWarning("[Prueba detección] No se pudo escribir la proyección: " + e.Message); }
    }

    // ───────────── Eventos del sistema ─────────────
    void OnRaw(int id, Vector2 pos)
    {
        ultimoMarcador = id;
        if (estado == Estado.Grabando) Registrar("aruco", id, id + 1);
    }

    void OnConfirmado(int id, Vector2 pos)
    {
        if (estado == Estado.Grabando) Registrar("confirmado", id, id + 1);
    }

    void OnPagina(int pagina)
    {
        if (estado == Estado.Grabando) Registrar("cambio_pagina", -1, pagina);
    }

    void OnMovProyeccion(int id, float desplazamiento, float giro)
    {
        if (estado == Estado.Grabando) Registrar("mov_proyeccion", id, id + 1);
    }

    void OnMensaje(string json)
    {
        AvisoGrabador a;
        try { a = JsonUtility.FromJson<AvisoGrabador>(json); } catch { return; }
        if (a == null || string.IsNullOrEmpty(a.tipo)) return;
        switch (a.tipo)
        {
            case "grabador_listo":
                grabadorListo = true;
                infoGrabador = $"listo ({a.ancho}×{a.alto})";
                break;
            case "grabacion_inicio":
                grabadorListo = true;
                if (estado == Estado.EsperandoGrabador && actual != null && a.video == actual.nombre)
                {
                    infoGrabador = $"grabando a {a.fps:0.#} fps";
                    Comenzar(Ahora);
                }
                break;
            case "grabacion_fin":
                infoGrabador = $"listo · último video: {a.cuadros} cuadros, {a.duracion_s:0.0} s";
                break;
            case "grabacion_error":
                Avisar("El grabador no pudo grabar " + a.video + ". Revisa la consola de Python.", 8f);
                break;
        }
    }

    // ───────────── Utilidades ─────────────
    void Registrar(string tipo, int marcador, int pagina)
    {
        double t = T;
        eventos.Add(new Ev { t = t, tipo = tipo, marcador = marcador, pagina = pagina });
        try
        {
            File.AppendAllText(fEventos, $"{actual?.nombre},{F(t)},{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()},{tipo}," +
                                         $"{(marcador >= 0 ? marcador.ToString(Inv) : "")},{(pagina > 0 ? pagina.ToString(Inv) : "")}\n");
        }
        catch (Exception e) { Debug.LogWarning("[Prueba detección] No se pudo escribir el registro: " + e.Message); }
    }

    void Enviar(string cmd, string video = null)
    {
        if (udp == null) return;
        // Al grabar también se envía la carpeta de resultados: los videos quedan junto a las etiquetas (Editor o build)
        string json = video == null ? $"{{\"cmd\":\"{cmd}\"}}"
            : $"{{\"cmd\":\"{cmd}\",\"video\":\"{video}\",\"carpeta\":\"{dir.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"}}";
        try { var b = Encoding.UTF8.GetBytes(json); udp.Send(b, b.Length, "127.0.0.1", puertoGrabador); }
        catch (Exception e) { Debug.LogWarning("[Prueba detección] No se pudo avisar al grabador: " + e.Message); }
    }

    void Ping()
    {
        proximoPing = Ahora + 3.0;
        if (usarGrabador) Enviar("ping");
    }

    void Avisar(string texto, float segundos = 5f)
    {
        aviso = texto;
        avisoHasta = Ahora + segundos;
    }

    static string F(double v) => v.ToString("0.000", Inv);

    // ───────────── Panel ─────────────
    GUIStyle estilo;

    void OnGUI()
    {
        if (!mostrarPanel) return;
        if (estilo == null) estilo = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true };
        var sb = new StringBuilder();
        sb.Append("<b>PRUEBA · DETECCIÓN DE PÁGINAS</b>   (H oculta este panel)\n");
        if (videos.Count > 0)
        {
            var v = actual ?? videos[idx];
            sb.Append($"Video {videos.IndexOf(v) + 1}/{videos.Count}: <b>{v.nombre}</b>{(hechos.Contains(v.nombre) && actual == null ? "  (ya hecho)" : "")}\n");
        }
        sb.Append($"Grabador: {(usarGrabador ? infoGrabador : "desactivado")}   ·   hechos: {hechos.Count}/{videos.Count}\n");
        sb.Append($"<size=12>Resultados: {dir}</size>\n");
        if (seguimiento)
            sb.Append("Proyección: " + (!seguimiento.follow ? "<color=#ffd166>seguimiento apagado (L)</color>"
                : seguimiento.Calibrando ? "calibrando…"
                : seguimiento.Calibrado ? $"calibrada · sigue al libro{(seguimiento.Moviendose ? " · <b>moviéndose</b>" : "")}"
                : "<color=#ff6666>SIN calibrar: presiona K antes de grabar</color>") + "\n");
        switch (estado)
        {
            case Estado.Libre:
                sb.Append("Abre el libro en la <b>página 1</b> y presiona <b>N</b> para empezar   (←/→ cambia de video)\n");
                break;
            case Estado.EsperandoGrabador:
                sb.Append("Esperando al grabador…   (R cancela)\n");
                break;
            case Estado.Grabando:
                var tr = tramos[tramos.Count - 1];
                sb.Append($"<color=#ff6666>● {T:0.0} s</color>   " +
                          (tr.pagina > 0 ? $"<b>página {tr.pagina}</b> a la vista" : $"pasando la hoja ({tr.desde}→{tr.desde + 1})") + "\n");
                sb.Append(paginaVista < paginas
                    ? "<b>P</b> empieza a pasar · <b>Espacio</b> nueva página a la vista · <b>R</b> repetir\n"
                    : "Última página: <b>F</b> termina el video · <b>R</b> repetir\n");
                break;
        }
        sb.Append($"Sistema: página {(tracker ? tracker.CurrentPage : 0)} · último ArUco {(ultimoMarcador >= 0 ? ultimoMarcador.ToString() : "—")}");
        if (Ahora < avisoHasta && !string.IsNullOrEmpty(aviso)) sb.Append("\n<color=#ffd166>").Append(aviso).Append("</color>");

        var rect = new Rect(12, 12, 660, 215);
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10, rect.y + 6, rect.width - 20, rect.height - 12), sb.ToString(), estilo);
    }
}
