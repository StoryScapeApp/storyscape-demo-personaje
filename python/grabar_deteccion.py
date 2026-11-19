"""
StoryScape · Grabador y análisis para la prueba "Detección de páginas".

GRABAR (lo controla Unity con el objeto PruebaDeteccion):
    python grabar_deteccion.py grabar --cam 0
    python grabar_deteccion.py grabar --cam 1 --ancho 1280 --alto 720 --fps 30   # si el sistema usa otra resolución

  Hace lo mismo que el script de ArUco del sistema (detecta con OpenCV y envía cada marcador a Unity por UDP,
  máx. 1 mensaje cada 0,5 s por marcador) y, además, graba el video limpio de la cámara cuando Unity se lo pide.
  Así el video es exactamente lo que vio el sistema y Unity registra en vivo qué página confirmó.
  Cierra antes aruco_sender.py: usan la misma cámara.

ANALIZAR (después de grabar, cuadro por cuadro):
    python grabar_deteccion.py analizar                      # usa el etiquetas_*.csv más reciente
    python grabar_deteccion.py analizar --etiquetas ruta.csv

Archivos (en Pruebas/deteccion_paginas):
  videos/<video>.mp4          video de la cámara, sin marcas
  videos/<video>.cuadros.csv  tiempo real de cada cuadro (t_s desde el primer cuadro = t_s de Unity)
  analisis_<sesion>.csv       por tramo: % de cuadros con el marcador correcto, detección con el criterio del sistema

Requisitos: pip install opencv-contrib-python
"""
import argparse
import csv
import json
import socket
import time
from collections import defaultdict, deque
from pathlib import Path

import cv2

DICCIONARIO = cv2.aruco.DICT_4X4_50
THROTTLE_S = 0.5      # el script del sistema envía como máx. 1 mensaje cada 0,5 s por marcador
MIN_HITS = 2          # ArucoUdpReceiver.minHits
VENTANA_S = 1.5       # ArucoUdpReceiver.hitWindow
RAIZ = Path(__file__).resolve().parent.parent
CARPETA = RAIZ / "Pruebas" / "deteccion_paginas"


def nuevo_detector():
    return cv2.aruco.ArucoDetector(cv2.aruco.getPredefinedDictionary(DICCIONARIO), cv2.aruco.DetectorParameters())


# ───────────────────────────── Grabar ─────────────────────────────

def abrir_camara(a):
    api = {"auto": cv2.CAP_ANY, "dshow": cv2.CAP_DSHOW, "msmf": cv2.CAP_MSMF}[a.backend]
    cap = cv2.VideoCapture(a.cam, api)
    if a.ancho:
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, a.ancho)
    if a.alto:
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, a.alto)
    if a.fps:
        cap.set(cv2.CAP_PROP_FPS, a.fps)
    for _ in range(30):                       # algunas cámaras tardan en entregar el primer cuadro
        ok, frame = cap.read()
        if ok:
            return cap, frame
        time.sleep(0.1)
    cap.release()
    raise SystemExit(f"No se pudo abrir la cámara {a.cam}. Cierra aruco_sender.py u otra app que la use, "
                     "o prueba --cam 1 o --backend dshow.")


def grabar(a):
    cap, frame = abrir_camara(a)
    h, w = frame.shape[:2]
    print(f"Cámara {a.cam}: {w}x{h}, {cap.get(cv2.CAP_PROP_FPS):.0f} fps declarados. "
          "Debe ser la misma resolución que usa el sistema.")
    videos = (Path(a.carpeta) if a.carpeta else CARPETA) / "videos"
    videos.mkdir(parents=True, exist_ok=True)
    print(f"Los videos se guardan en {videos}")

    unity = ("127.0.0.1", a.unity_port)
    tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        rx.bind(("127.0.0.1", a.control_port))
    except OSError:
        raise SystemExit(f"El puerto {a.control_port} está ocupado: ¿hay otro grabador abierto?")
    rx.setblocking(False)

    def avisar(**d):
        tx.sendto(json.dumps(d).encode("utf-8"), unity)

    detector = nuevo_detector()
    ultimo_envio = {}
    tiempos = deque(maxlen=90)
    grab = None
    fallos = 0
    avisar(tipo="grabador_listo", ancho=w, alto=h)
    print("Listo. En Unity: activa PruebaDeteccion, da Play y presiona N. (q en esta ventana para salir)")
    ventana = "Grabador - deteccion de paginas (q para salir)"

    def cerrar(g, descartar=False):
        g["writer"].release()
        g["csv_file"].close()
        dur = g["ultimo"] - g["t0"] if g["cuadros"] > 1 else 0.0
        if descartar:
            g["ruta"].unlink(missing_ok=True)
            g["ruta_csv"].unlink(missing_ok=True)
            print(f"  Descartado {g['ruta'].name}.")
            return
        fps_real = (g["cuadros"] - 1) / dur if dur > 0 else 0
        print(f"  Guardado {g['ruta'].name}: {g['cuadros']} cuadros, {dur:.1f} s ({fps_real:.1f} fps reales).")
        if fps_real and abs(fps_real - g["fps"]) / g["fps"] > 0.1:
            print(f"  ! El video se escribió a {g['fps']:.0f} fps pero la cámara entregó {fps_real:.1f}: "
                  f"usa los tiempos de {g['ruta_csv'].name} (el análisis ya lo hace).")
        avisar(tipo="grabacion_fin", video=g["ruta"].name, cuadros=g["cuadros"], duracion_s=round(dur, 3))

    while True:
        ok, frame = cap.read()
        ahora = time.time()
        if not ok:
            fallos += 1
            if fallos > 60:
                print("! La cámara dejó de entregar imágenes.")
                break
            continue
        fallos = 0
        tiempos.append(ahora)

        # 1) Órdenes de Unity
        while True:
            try:
                data, _ = rx.recvfrom(4096)
            except (BlockingIOError, ConnectionResetError):
                break
            try:
                orden = json.loads(data.decode("utf-8"))
            except ValueError:
                continue
            cmd = orden.get("cmd")
            if cmd == "ping":
                avisar(tipo="grabador_listo", ancho=w, alto=h)
            elif cmd == "grabar":
                if grab:
                    cerrar(grab)
                nombre = Path(orden.get("video") or f"video_{int(ahora)}.mp4").name
                ruta = videos / nombre
                # fps medidos en la vista previa: así el video dura lo mismo que la realidad
                medido = (len(tiempos) - 1) / (tiempos[-1] - tiempos[0]) if len(tiempos) > 10 else 0
                fps = round(medido) if 5 <= medido <= 120 else (cap.get(cv2.CAP_PROP_FPS) or 30)
                writer = cv2.VideoWriter(str(ruta), cv2.VideoWriter_fourcc(*"mp4v"), fps, (w, h))
                if not writer.isOpened():
                    print(f"! No se pudo crear {ruta}")
                    avisar(tipo="grabacion_error", video=nombre)
                    continue
                ruta_csv = ruta.with_suffix(".cuadros.csv")
                f = open(ruta_csv, "w", newline="", encoding="utf-8")
                cw = csv.writer(f)
                cw.writerow(["cuadro", "t_s", "unix_ms"])
                grab = {"writer": writer, "ruta": ruta, "ruta_csv": ruta_csv, "csv_file": f, "csv": cw,
                        "fps": fps, "cuadros": 0, "t0": None, "ultimo": None}
                print(f"\nGrabando {nombre} a {fps:.0f} fps…")
            elif cmd in ("parar", "descartar") and grab:
                cerrar(grab, descartar=(cmd == "descartar"))
                grab = None

        # 2) Detección y envío a Unity (igual que el sistema)
        corners, ids, _ = detector.detectMarkers(cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY))
        if ids is not None:
            for c, i in zip(corners, ids.flatten()):
                i = int(i)
                if ahora - ultimo_envio.get(i, 0) < THROTTLE_S:
                    continue
                ultimo_envio[i] = ahora
                cx, cy = c[0].mean(axis=0)
                ax, ay = (c[0][1] + c[0][2]) / 2   # punto medio del borde derecho: Unity calcula el giro del libro
                tx.sendto(json.dumps({"tipo": "aruco", "id": i, "x": float(cx / w), "y": float(cy / h),
                                      "ax": float(ax / w), "ay": float(ay / h)}).encode("utf-8"), unity)

        # 3) Grabación (cuadro limpio, sin marcas)
        if grab:
            if grab["cuadros"] == 0:
                grab["t0"] = ahora
                avisar(tipo="grabacion_inicio", video=grab["ruta"].name, fps=grab["fps"])
            grab["writer"].write(frame)
            grab["csv"].writerow([grab["cuadros"], f"{ahora - grab['t0']:.4f}", int(ahora * 1000)])
            grab["cuadros"] += 1
            grab["ultimo"] = ahora

        # 4) Vista previa
        vista = frame.copy()
        if ids is not None:
            cv2.aruco.drawDetectedMarkers(vista, corners, ids)
            texto = "Ve pagina " + ", ".join(str(int(i) + 1) for i in sorted(set(ids.flatten())))
        else:
            texto = "No ve ningun marcador"
        cv2.putText(vista, texto, (10, h - 20), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 200, 0) if ids is not None else (0, 165, 255), 2)
        if grab:
            cv2.putText(vista, f"GRABANDO {grab['ruta'].name}  {ahora - grab['t0']:.0f}s", (10, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)
        cv2.imshow(ventana, vista)
        if cv2.waitKey(1) & 0xFF in (ord("q"), ord("Q"), 27):
            break

    if grab:
        cerrar(grab)
    cap.release()
    cv2.destroyAllWindows()


# ───────────────────────────── Analizar ─────────────────────────────

def leer_tiempos(ruta_video, n_fps):
    ruta_csv = ruta_video.with_suffix(".cuadros.csv")
    if ruta_csv.exists():
        with open(ruta_csv, encoding="utf-8") as f:
            return [float(r["t_s"]) for r in csv.DictReader(f)]
    return None


def analizar_video(ruta, detector):
    """Devuelve [(t, ids)] por cuadro y las confirmaciones simuladas [(t, id)] con el criterio del sistema."""
    cap = cv2.VideoCapture(str(ruta))
    fps = cap.get(cv2.CAP_PROP_FPS) or 30
    tiempos = leer_tiempos(ruta, fps)
    cuadros, confirmaciones = [], []
    ultimo_envio, hits = {}, defaultdict(list)
    n = 0
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        t = tiempos[n] if tiempos and n < len(tiempos) else n / fps
        n += 1
        _, ids, _ = detector.detectMarkers(cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY))
        ids = set() if ids is None else {int(i) for i in ids.flatten()}
        cuadros.append((t, ids))
        for i in ids:
            if t - ultimo_envio.get(i, -1e9) < THROTTLE_S:
                continue
            ultimo_envio[i] = t
            lista = [x for x in hits[i] if t - x <= VENTANA_S] + [t]
            if len(lista) >= MIN_HITS:
                confirmaciones.append((t, i))
                lista = []
            hits[i] = lista
    cap.release()
    return cuadros, confirmaciones


def analizar(a):
    base = Path(a.carpeta) if a.carpeta else CARPETA
    if a.etiquetas:
        ruta_et = Path(a.etiquetas)
    else:
        candidatos = sorted(base.glob("etiquetas_*.csv"))
        if not candidatos:
            raise SystemExit(f"No hay etiquetas_*.csv en {base}")
        ruta_et = candidatos[-1]
    with open(ruta_et, encoding="utf-8-sig") as f:
        filas = list(csv.DictReader(f))
    por_video = defaultdict(list)
    for r in filas:
        por_video[r["video"]].append(r)
    print(f"Etiquetas: {ruta_et.name} · {len(por_video)} videos")

    detector = nuevo_detector()
    salida = base / ruta_et.name.replace("etiquetas_", "analisis_")
    resultados = []
    for video, tramos in por_video.items():
        ruta = base / "videos" / video
        if not ruta.exists():
            print(f"  falta {ruta.name}")
            continue
        cuadros, conf = analizar_video(ruta, detector)
        partes = Path(video).stem.split("_")
        luz, pos, rep = (partes + ["", "", ""])[:3]
        anterior = None
        for k, tr in enumerate(tramos):
            ini, fin = float(tr["inicio_s"]), float(tr["fin_s"])
            es_pag = tr["pagina_esperada"] != "transicion"
            if es_pag:
                validos = {int(tr["pagina_esperada"]) - 1}
                anterior = int(tr["pagina_esperada"])
            else:
                siguiente = next((int(x["pagina_esperada"]) for x in tramos[k + 1:] if x["pagina_esperada"] != "transicion"), None)
                validos = {p - 1 for p in (anterior, siguiente) if p}
            en = [ids for t, ids in cuadros if ini <= t < fin]
            n = len(en)
            correctos = sum(1 for ids in en if ids & validos)
            otros = sum(1 for ids in en if ids - validos)
            c_ok = [t for t, i in conf if ini <= t <= fin and i in validos]
            c_mal = [t for t, i in conf if ini <= t <= fin and i not in validos]
            resultados.append({
                "video": video, "luz": luz, "posicion": pos, "rep": rep.replace("rep", ""),
                "pagina_esperada": tr["pagina_esperada"], "inicio_s": f"{ini:.3f}", "fin_s": f"{fin:.3f}", "cuadros": n,
                "tasa_cuadros_correctos": f"{(correctos / n if n else 0):.3f}",
                "cuadros_con_otro_marcador": otros,
                "detectada": (1 if c_ok else 0) if es_pag else "",
                "latencia_s": f"{c_ok[0] - ini:.3f}" if es_pag and c_ok else "",
                "confirmaciones_incorrectas": len(c_mal),
            })
        pags = [r for r in resultados if r["video"] == video and r["pagina_esperada"] != "transicion"]
        print(f"  {video}: {sum(r['detectada'] for r in pags)}/{len(pags)} páginas detectadas")

    if not resultados:
        raise SystemExit("No se encontró ningún video.")
    with open(salida, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(resultados[0].keys()))
        w.writeheader()
        w.writerows(resultados)

    # Resumen por condición
    print("\nTasa de detección por condición (páginas):")
    grupos = defaultdict(list)
    for r in resultados:
        if r["pagina_esperada"] != "transicion":
            grupos[(r["luz"], r["posicion"])].append(r["detectada"])
    for (luz, pos), v in sorted(grupos.items()):
        print(f"  {luz:10s} {pos:10s} {sum(v)}/{len(v)}  ({100 * sum(v) / len(v):.0f} %)")
    falsas = sum(r["confirmaciones_incorrectas"] for r in resultados if r["pagina_esperada"] == "transicion")
    print(f"Confirmaciones incorrectas durante las transiciones: {falsas}")
    print("Resultados en", salida)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    g = sub.add_parser("grabar")
    g.add_argument("--cam", type=int, default=0)
    g.add_argument("--backend", choices=["auto", "dshow", "msmf"], default="auto",
                   help="auto = igual que aruco_sender.py; prueba dshow si la cámara no abre")
    g.add_argument("--ancho", type=int, default=0, help="déjalo en 0 para usar la resolución por defecto, como el sistema")
    g.add_argument("--alto", type=int, default=0)
    g.add_argument("--fps", type=int, default=0)
    g.add_argument("--unity-port", type=int, default=5005)
    g.add_argument("--control-port", type=int, default=5007)
    g.add_argument("--carpeta", default="", help="por defecto Pruebas/deteccion_paginas del proyecto")
    an = sub.add_parser("analizar")
    an.add_argument("--etiquetas", default="", help="etiquetas_<sesion>.csv (por defecto, el más reciente)")
    an.add_argument("--carpeta", default="")
    a = ap.parse_args()
    grabar(a) if a.cmd == "grabar" else analizar(a)


if __name__ == "__main__":
    main()
