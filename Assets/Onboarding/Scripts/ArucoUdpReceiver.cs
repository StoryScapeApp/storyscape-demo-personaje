using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Recibe por UDP los marcadores ArUco detectados por el script de Python (OpenCV)
/// y ejecuta la acción asociada a cada ID (por ejemplo, ID 0 → iniciar el onboarding).
///
/// Mensaje esperado (JSON): {"tipo": "aruco", "id": 0, "x": 0.5, "y": 0.5}
///   x, y = posición normalizada (0–1) del marcador en la imagen de la cámara (opcional).
///
/// Un marcador se considera detectado cuando llegan al menos "Min Hits" mensajes
/// en "Hit Window" segundos (evita falsos positivos de un solo cuadro).
/// Funciona igual en el Editor y en el ejecutable (.exe).
/// </summary>
public class ArucoUdpReceiver : MonoBehaviour
{
    [Serializable]
    public class MarkerAction
    {
        public string description = "Página";
        public int markerId;
        [Tooltip("Ejecutar solo la primera vez que se detecta en la sesión")]
        public bool onlyOnce = true;
        public UnityEvent onDetected = new UnityEvent();
        [NonSerialized] public bool fired;
    }

    [Header("Conexión")]
    [Tooltip("Debe coincidir con el puerto del script de Python")]
    public int port = 5005;

    [Header("Estabilidad de la detección")]
    [Tooltip("Mensajes mínimos del mismo ID para considerarlo detectado")]
    public int minHits = 2;
    [Tooltip("Ventana de tiempo (s) en la que deben llegar esos mensajes")]
    public float hitWindow = 1.5f;

    [Header("Acciones por marcador")]
    public List<MarkerAction> actions = new List<MarkerAction>();

    [Header("Depuración")]
    [Tooltip("Muestra en pantalla el último marcador recibido")]
    public bool showDebug = false;

    /// <summary>Se dispara con cada detección estable: (id, posición normalizada; -1 si no se envió).</summary>
    public event Action<int, Vector2> MarkerDetected;

    [Serializable] class Msg { public string tipo; public int id = -1; public float x = -1f; public float y = -1f; }

    struct Hit { public int id; public Vector2 pos; public float time; }

    readonly ConcurrentQueue<Msg> incoming = new ConcurrentQueue<Msg>();
    readonly Dictionary<int, List<float>> recentHits = new Dictionary<int, List<float>>();
    readonly Dictionary<int, Vector2> lastPositions = new Dictionary<int, Vector2>();
    UdpClient client;
    Thread thread;
    volatile bool running;
    string lastInfo = "esperando…";
    float lastTime = -999f;

    public bool TryGetLastPosition(int id, out Vector2 pos) => lastPositions.TryGetValue(id, out pos);

    void OnEnable()
    {
        try
        {
            client = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            running = true;
            thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "ArucoUdpReceiver" };
            thread.Start();
            Debug.Log($"[ArUco] Escuchando marcadores en el puerto UDP {port}.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ArUco] No se pudo abrir el puerto {port}: {e.Message}. " +
                           "¿Hay otra instancia abierta (Editor y .exe a la vez)?");
        }
    }

    void ReceiveLoop()
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                byte[] data = client.Receive(ref any);
                var msg = JsonUtility.FromJson<Msg>(Encoding.UTF8.GetString(data));
                if (msg != null && msg.id >= 0) incoming.Enqueue(msg);
            }
            catch (SocketException) { if (!running) break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception) { /* mensaje mal formado: se ignora */ }
        }
    }

    void Update()
    {
        float now = Time.unscaledTime;
        while (incoming.TryDequeue(out var msg))
        {
            if (msg.x >= 0f && msg.y >= 0f) lastPositions[msg.id] = new Vector2(msg.x, msg.y);
            lastInfo = $"ID {msg.id}" + (msg.x >= 0f ? $"  ({msg.x:0.00}, {msg.y:0.00})" : "");
            lastTime = now;

            if (!recentHits.TryGetValue(msg.id, out var list)) recentHits[msg.id] = list = new List<float>();
            list.Add(now);
            list.RemoveAll(t => now - t > hitWindow);

            if (list.Count >= minHits)
            {
                list.Clear();
                OnStableDetection(msg.id);
            }
        }
    }

    void OnStableDetection(int id)
    {
        lastPositions.TryGetValue(id, out var pos);
        MarkerDetected?.Invoke(id, lastPositions.ContainsKey(id) ? pos : new Vector2(-1f, -1f));

        bool any = false;
        foreach (var a in actions)
        {
            if (a.markerId != id) continue;
            any = true;
            if (a.onlyOnce && a.fired) continue;
            a.fired = true;
            Debug.Log($"[ArUco] ID {id} detectado → {a.description}");
            a.onDetected?.Invoke();
        }
        if (!any) Debug.Log($"[ArUco] ID {id} detectado (sin acción asignada).");
    }

    /// Permite volver a disparar las acciones "solo una vez" (p. ej. para un nuevo niño).
    [ContextMenu("Reiniciar acciones")]
    public void ResetActions()
    {
        foreach (var a in actions) a.fired = false;
    }

    void OnGUI()
    {
        if (!showDebug) return;
        bool recent = Time.unscaledTime - lastTime < 1f;
        GUI.color = recent ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 1f, 1f, 0.6f);
        GUI.Label(new Rect(16, Screen.height - 30, 500, 22), $"ArUco (UDP {port}): {lastInfo}");
        GUI.color = Color.white;
    }

    void OnDisable() => Stop();
    void OnApplicationQuit() => Stop();

    void Stop()
    {
        running = false;
        try { client?.Close(); } catch { }
        client = null;
        if (thread != null && thread.IsAlive) thread.Join(200);
        thread = null;
    }
}
