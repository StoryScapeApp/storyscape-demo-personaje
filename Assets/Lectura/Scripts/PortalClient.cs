using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Envía los resultados del quiz al backend del portal docente (storyscape-platform).
///
///   1. Cuando el niño dice su nombre (SesionLector) se crea una sesión: el backend devuelve un código de alumno (ALUMNO-1234).
///   2. Cada pregunta de capítulo (id "capitulo_*") se envía con las letras que eligió en cada intento.
///   3. Al terminar el libro, o con "nuevo alumno", la sesión se cierra y el backend calcula los puntajes.
///
/// Configuración: StreamingAssets/portal.local.json  { "baseUrl": "http://localhost:3001", "apiKey": "…" }
/// (no se sube a git). Sin ese archivo, no hace nada. Si el backend no responde, los envíos esperan en una cola y se
/// reintentan: nunca frena la lectura. No envía audios ni imágenes: solo nombre (opcional), preguntas y letras elegidas.
/// Se crea solo al dar Play.
/// </summary>
public class PortalClient : MonoBehaviour
{
    public static PortalClient Instancia { get; private set; }

    [Tooltip("Solo se envían los quizzes cuyo id empieza así (el quiz de la página 1 del onboarding es de práctica)")]
    public string prefijoQuiz = "capitulo_";
    public string archivoConfig = "portal.local.json";
    public string libro = "el-principito";
    [Tooltip("Segundos entre reintentos si el backend no responde")]
    public float reintento = 6f;

    public bool Habilitado { get; private set; }
    public string SessionId { get; private set; }
    public string CodigoAlumno { get; private set; }

    [Serializable] class Config { public string baseUrl; public string apiKey; }
    [Serializable] class CrearBody { public string name; public string bookRefId; }
    [Serializable] class CrearResp { public string sessionId; public string studentId; public string studentCode; }
    [Serializable] class Intento { public string letter; }
    [Serializable]
    class RespBody
    {
        public string chapterRefId, questionType, questionText, correctLetter;
        public int questionNumber;
        public string[] options;
        public Intento[] attempts;
    }

    enum Tipo { Crear, Respuesta, Completar }
    class Envio { public Tipo tipo; public string json; public string sesion; public int fallos; }

    string baseUrl, apiKey;
    readonly Queue<Envio> cola = new Queue<Envio>();
    bool sesionAbierta;       // hay una sesión creada (o por crear) sin cerrar
    string nombrePendiente = "";
    bool trabajando;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Crear()
    {
        if (FindFirstObjectByType<PortalClient>() != null) return;
        if (FindFirstObjectByType<QuizSession>() == null) return;   // solo en la escena del libro
        new GameObject("PortalClient").AddComponent<PortalClient>();
    }

    void Awake()
    {
        if (Instancia != null && Instancia != this) { Destroy(gameObject); return; }
        Instancia = this;
        CargarConfig();
    }

    void CargarConfig()
    {
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, archivoConfig);
            if (!File.Exists(path)) { Debug.Log($"[Portal] Sin {archivoConfig}: los resultados solo se guardan en el CSV local."); return; }
            var c = JsonUtility.FromJson<Config>(File.ReadAllText(path, Encoding.UTF8));
            if (c == null || string.IsNullOrWhiteSpace(c.baseUrl) || string.IsNullOrWhiteSpace(c.apiKey)) { Debug.LogWarning($"[Portal] {archivoConfig} incompleto (baseUrl y apiKey)."); return; }
            baseUrl = c.baseUrl.Trim().TrimEnd('/');
            apiKey = c.apiKey.Trim();
            Habilitado = true;
            Debug.Log($"[Portal] Conectado a {baseUrl}: los resultados del quiz se envían al portal docente.");
        }
        catch (Exception e) { Debug.LogWarning("[Portal] No se pudo leer la configuración: " + e.Message); }
    }

    void OnEnable()
    {
        SesionLector.NombreCambiado += OnNombre;
        QuizSession.PreguntaTerminada += OnPregunta;
        QuizSession.QuizTerminado += OnQuizTerminado;
    }

    void OnDisable()
    {
        SesionLector.NombreCambiado -= OnNombre;
        QuizSession.PreguntaTerminada -= OnPregunta;
        QuizSession.QuizTerminado -= OnQuizTerminado;
    }

    void OnDestroy() { if (Instancia == this) Instancia = null; }

    // ───────────── Eventos del juego ─────────────
    void OnNombre(string nombre)
    {
        if (!Habilitado) return;
        if (string.IsNullOrEmpty(nombre)) { CerrarSesion(); return; }   // nuevo niño: se cierra la anterior
        nombrePendiente = nombre;
        // Un niño nuevo con nombre: cierra la sesión anterior si quedó abierta y abre la nueva
        if (sesionAbierta) CerrarSesion();
        AbrirSesion(nombre);
    }

    void OnPregunta(QuizSession.ResultadoPregunta r)
    {
        if (!Habilitado || r == null || string.IsNullOrEmpty(r.quizId) || !r.quizId.StartsWith(prefijoQuiz, StringComparison.OrdinalIgnoreCase)) return;
        if (!sesionAbierta) AbrirSesion(SesionLector.TieneNombre ? SesionLector.Nombre : "");   // sin nombre: sesión anónima

        var intentos = new List<Intento>();
        foreach (var l in r.intentos) intentos.Add(new Intento { letter = l });
        var body = new RespBody
        {
            chapterRefId = r.quizId, questionNumber = r.numero, questionType = r.tipo,
            questionText = r.texto, correctLetter = r.letraCorrecta, options = r.opciones, attempts = intentos.ToArray(),
        };
        Encolar(new Envio { tipo = Tipo.Respuesta, json = JsonUtility.ToJson(body) });
    }

    void OnQuizTerminado(string quizId, bool finDelLibro)
    {
        if (Habilitado && finDelLibro && !string.IsNullOrEmpty(quizId) && quizId.StartsWith(prefijoQuiz, StringComparison.OrdinalIgnoreCase)) CerrarSesion();
    }

    /// "Nuevo alumno": cierra la sesión en curso (el siguiente niño abre otra al decir su nombre).
    public static void NuevoAlumno() { if (Instancia != null) Instancia.CerrarSesion(); }

    void AbrirSesion(string nombre)
    {
        sesionAbierta = true;
        SessionId = null;
        CodigoAlumno = null;
        Encolar(new Envio { tipo = Tipo.Crear, json = JsonUtility.ToJson(new CrearBody { name = nombre ?? "", bookRefId = libro }) });
    }

    void CerrarSesion()
    {
        if (!Habilitado || !sesionAbierta) return;
        sesionAbierta = false;
        Encolar(new Envio { tipo = Tipo.Completar });
    }

    // ───────────── Cola de envíos (en orden, con reintentos) ─────────────
    void Encolar(Envio e)
    {
        if (cola.Count > 300) { Debug.LogWarning("[Portal] Demasiados envíos pendientes: se descarta el más antiguo."); cola.Dequeue(); }
        cola.Enqueue(e);
        if (!trabajando) StartCoroutine(Trabajar());
    }

    IEnumerator Trabajar()
    {
        trabajando = true;
        while (cola.Count > 0)
        {
            var e = cola.Peek();
            string url; string body = e.json;
            if (e.tipo == Tipo.Crear) url = baseUrl + "/unity/sessions";
            else
            {
                string sid = e.sesion ?? SessionId;
                if (string.IsNullOrEmpty(sid)) { cola.Dequeue(); continue; }   // no hay sesión (la creación falló definitivamente)
                e.sesion = sid;
                url = baseUrl + "/unity/sessions/" + sid + (e.tipo == Tipo.Respuesta ? "/responses" : "/complete");
                if (e.tipo == Tipo.Completar) body = "{}";
            }

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body ?? "{}"));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("x-api-key", apiKey);
                req.timeout = 10;
                yield return req.SendWebRequest();

                long code = req.responseCode;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    cola.Dequeue();
                    if (e.tipo == Tipo.Crear)
                    {
                        var r = JsonUtility.FromJson<CrearResp>(req.downloadHandler.text);
                        SessionId = r?.sessionId; CodigoAlumno = r?.studentCode;
                        Debug.Log($"[Portal] Sesión creada: {CodigoAlumno}.");
                    }
                    else if (e.tipo == Tipo.Respuesta) Debug.Log("[Portal] Respuesta enviada.");
                    else Debug.Log("[Portal] Sesión cerrada.");
                    continue;
                }

                string detalle = req.downloadHandler != null ? req.downloadHandler.text : "";
                if (code == 401 || code == 503)
                {
                    Debug.LogWarning($"[Portal] El backend rechazó el acceso ({code}). Revisa apiKey en {archivoConfig} y UNITY_INGEST_KEY / UNITY_PROFESSOR_EMAIL del backend. Se desactiva el envío. {detalle}");
                    Habilitado = false; cola.Clear(); break;
                }
                if (code >= 400 && code < 500)   // dato inválido o sesión inexistente: reintentar no sirve
                {
                    Debug.LogWarning($"[Portal] Se descarta un envío ({e.tipo}, {code}): {detalle}");
                    cola.Dequeue();
                    continue;
                }
                e.fallos++;
                if (e.fallos == 1) Debug.LogWarning($"[Portal] No se pudo contactar el backend ({req.error}). Se reintenta cada {reintento:0} s; la lectura sigue normal.");
            }
            yield return new WaitForSecondsRealtime(reintento);
        }
        trabajando = false;
    }
}
