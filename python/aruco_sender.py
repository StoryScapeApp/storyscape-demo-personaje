"""
Demo OpenCV + ArUco -> Unity (por UDP)

Requisitos:  pip install opencv-contrib-python numpy   (OpenCV >= 4.7)

Uso:
  python aruco_sender.py --generar 0 1 2      # crea marker_0.png, marker_1.png, marker_2.png
  python aruco_sender.py                      # detecta con la webcam y envía a Unity
  python aruco_sender.py --cam 1 --port 5065  # otra cámara / puerto
"""
import argparse
import json
import socket
import time

import cv2
import numpy as np

UNITY = ("127.0.0.1", 5005)          # Unity en la misma laptop, puerto 5005
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
ultimo_envio = {}
ENVIO_MIN_S = 0.1   # segundos entre mensajes del mismo marcador (antes 0.5). Más bajo = la proyección sigue al libro con más fluidez

def enviar_marcador(marker_id, centro=None, borde=None):
    """Envía a Unity el ID detectado (máx. 1 vez cada ENVIO_MIN_S por marcador)."""
    ahora = time.time()
    if ahora - ultimo_envio.get(marker_id, 0) < ENVIO_MIN_S:
        return
    ultimo_envio[marker_id] = ahora
    msg = {"tipo": "aruco", "id": int(marker_id)}
    if centro:
        msg["x"], msg["y"] = centro   # posición normalizada 0–1: la proyección sigue al libro
    if borde:
        msg["ax"], msg["ay"] = borde  # punto medio del borde derecho (0–1): Unity calcula el giro del libro
    sock.sendto(json.dumps(msg).encode("utf-8"), UNITY)

DICCIONARIO = cv2.aruco.DICT_4X4_50
LADO_MARCADOR = 0.05  # metros: mide tu marcador impreso y cambia este valor


def generar_marcadores(ids, size=400):
    d = cv2.aruco.getPredefinedDictionary(DICCIONARIO)
    for i in ids:
        img = cv2.aruco.generateImageMarker(d, i, size)
        # borde blanco: sin él la detección falla mucho
        img = cv2.copyMakeBorder(img, 40, 40, 40, 40, cv2.BORDER_CONSTANT, value=255)
        cv2.imwrite(f"marker_{i}.png", img)
        print(f"Generado marker_{i}.png")


def rot_a_quat(R):
    """Matriz de rotación 3x3 -> cuaternión (x, y, z, w)."""
    t = np.trace(R)
    if t > 0:
        s = np.sqrt(t + 1.0) * 2
        w, x = 0.25 * s, (R[2, 1] - R[1, 2]) / s
        y, z = (R[0, 2] - R[2, 0]) / s, (R[1, 0] - R[0, 1]) / s
    elif R[0, 0] > R[1, 1] and R[0, 0] > R[2, 2]:
        s = np.sqrt(1.0 + R[0, 0] - R[1, 1] - R[2, 2]) * 2
        w, x = (R[2, 1] - R[1, 2]) / s, 0.25 * s
        y, z = (R[0, 1] + R[1, 0]) / s, (R[0, 2] + R[2, 0]) / s
    elif R[1, 1] > R[2, 2]:
        s = np.sqrt(1.0 + R[1, 1] - R[0, 0] - R[2, 2]) * 2
        w, x = (R[0, 2] - R[2, 0]) / s, (R[0, 1] + R[1, 0]) / s
        y, z = 0.25 * s, (R[1, 2] + R[2, 1]) / s
    else:
        s = np.sqrt(1.0 + R[2, 2] - R[0, 0] - R[1, 1]) * 2
        w, x = (R[1, 0] - R[0, 1]) / s, (R[0, 2] + R[2, 0]) / s
        y, z = (R[1, 2] + R[2, 1]) / s, 0.25 * s
    return x, y, z, w


def main(args):
    cap = cv2.VideoCapture(args.cam)
    ok, frame = cap.read()
    if not ok:
        raise SystemExit("No se pudo abrir la cámara")
    h, w = frame.shape[:2]

    # Intrínsecos aproximados (suficiente para una demo).
    # Para precisión real, calibra la cámara y reemplaza K y dist.
    K = np.array([[w, 0, w / 2], [0, w, h / 2], [0, 0, 1]], dtype=np.float64)
    dist = np.zeros(5)
    fov_v = np.degrees(2 * np.arctan((h / 2) / K[1, 1]))
    print(f"Resolución {w}x{h} -> pon el Field of View vertical de la cámara de Unity en {fov_v:.1f}°")

    detector = cv2.aruco.ArucoDetector(
        cv2.aruco.getPredefinedDictionary(DICCIONARIO), cv2.aruco.DetectorParameters()
    )
    m = LADO_MARCADOR / 2
    # Orden de esquinas que devuelve ArUco: sup-izq, sup-der, inf-der, inf-izq
    obj_pts = np.array([[-m, m, 0], [m, m, 0], [m, -m, 0], [-m, -m, 0]], dtype=np.float32)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    destino = (args.host, args.port)
    destino_video = (args.host, args.port + 1)

    while True:
        ok, frame = cap.read()
        if not ok:
            break
        ok_jpg, jpg = cv2.imencode(".jpg", frame, [cv2.IMWRITE_JPEG_QUALITY, 70])
        if ok_jpg and len(jpg) < 65000:
            sock.sendto(jpg.tobytes(), destino_video)
        gris = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        corners, ids, _ = detector.detectMarkers(gris)

        marcadores = []
        if ids is not None:
            cv2.aruco.drawDetectedMarkers(frame, corners, ids)
            for c, i in zip(corners, ids.flatten()):
                cx, cy = c[0].mean(axis=0)
                ax, ay = (c[0][1] + c[0][2]) / 2   # entre sup-der e inf-der
                enviar_marcador(i, (float(cx / w), float(cy / h)), (float(ax / w), float(ay / h)))

                ok, rvec, tvec = cv2.solvePnP(obj_pts, c[0], K, dist, flags=cv2.SOLVEPNP_IPPE_SQUARE)
                if not ok:
                    continue
                cv2.drawFrameAxes(frame, K, dist, rvec, tvec, LADO_MARCADOR * 0.5)
                R, _ = cv2.Rodrigues(rvec)
                qx, qy, qz, qw = rot_a_quat(R)
                tx, ty, tz = tvec.flatten()
                # OpenCV (Y hacia abajo, dextrógiro) -> Unity (Y hacia arriba, levógiro): se invierte Y
                marcadores.append({
                    "id": int(i),
                    "pos": [float(tx), float(-ty), float(tz)],
                    "rot": [float(-qx), float(qy), float(-qz), float(qw)],
                })

        sock.sendto(json.dumps({"markers": marcadores}).encode(), destino)
        cv2.imshow("ArUco -> Unity (q para salir)", frame)
        if cv2.waitKey(1) & 0xFF == ord("q"):
            break

    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--generar", nargs="+", type=int, help="IDs de marcadores a generar")
    p.add_argument("--cam", type=int, default=0)
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--port", type=int, default=5065)
    a = p.parse_args()
    generar_marcadores(a.generar) if a.generar else main(a)
