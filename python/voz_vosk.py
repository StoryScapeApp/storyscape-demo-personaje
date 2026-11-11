"""
StoryScape · Reconocimiento de voz local (Vosk) para la lectura en vivo.

Escucha el micrófono y envía a Unity, por UDP (mismo puerto del ArUco), el texto que el niño va leyendo:
  - texto PARCIAL mientras habla (cada ~0.25 s), así Unity activa la animación apenas termina la frase;
  - texto FINAL cuando hace una pausa.
Mensaje: {"tipo": "voz", "texto": "...", "final": false}

Funciona sin internet y sin costo. Ejecútalo junto con el script del ArUco (son independientes).

Instalación (una sola vez):
  pip install vosk sounddevice
  Descarga el modelo de español y descomprímelo junto a este archivo:
  https://alphacephei.com/vosk/models/vosk-model-small-es-0.42.zip
"""
import json
import os
import queue
import socket
import sys

import sounddevice as sd
from vosk import KaldiRecognizer, Model, SetLogLevel

# ───────────── Configuración ─────────────
MODELO = os.path.join(os.path.dirname(os.path.abspath(__file__)), "vosk-model-small-es-0.42")
UNITY = ("127.0.0.1", 5005)          # mismo puerto que el ArUco
MICROFONO = "USB Audio Device"       # parte del nombre del micrófono; None = predeterminado de Windows
FRECUENCIA = 16000
BLOQUE = 4000                        # 0.25 s de audio por bloque (texto parcial cada ~0.25 s)


def buscar_microfono(nombre):
    """Devuelve el índice del primer micrófono cuyo nombre contiene 'nombre' (o None)."""
    if not nombre:
        return None
    for i, d in enumerate(sd.query_devices()):
        if d["max_input_channels"] > 0 and nombre.lower() in d["name"].lower():
            return i
    print(f"[Voz] No encontré el micrófono '{nombre}'. Uso el predeterminado de Windows.")
    return None


def main():
    if not os.path.isdir(MODELO):
        print(f"[Voz] Falta el modelo de Vosk en:\n  {MODELO}\n"
              "Descárgalo de https://alphacephei.com/vosk/models/vosk-model-small-es-0.42.zip "
              "y descomprímelo junto a este archivo.")
        sys.exit(1)

    SetLogLevel(-1)
    print("[Voz] Cargando el modelo de español…")
    rec = KaldiRecognizer(Model(MODELO), FRECUENCIA)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    def enviar(texto, final):
        msg = json.dumps({"tipo": "voz", "texto": texto, "final": final}, ensure_ascii=False)
        sock.sendto(msg.encode("utf-8"), UNITY)

    audio = queue.Queue()

    def al_recibir(indata, frames, tiempo, status):
        audio.put(bytes(indata))

    dispositivo = buscar_microfono(MICROFONO)
    nombre = sd.query_devices(dispositivo, "input")["name"] if dispositivo is not None else "predeterminado de Windows"
    print(f"[Voz] Escuchando con: {nombre}. Enviando a Unity {UNITY[0]}:{UNITY[1]}. Ctrl+C para salir.")

    ultimo = ""
    with sd.RawInputStream(samplerate=FRECUENCIA, blocksize=BLOQUE, device=dispositivo,
                           dtype="int16", channels=1, callback=al_recibir):
        while True:
            datos = audio.get()
            if rec.AcceptWaveform(datos):
                texto = json.loads(rec.Result()).get("text", "")
                if texto:
                    enviar(texto, True)
                    print(f"  ✔ {texto}")
                ultimo = ""
            else:
                parcial = json.loads(rec.PartialResult()).get("partial", "")
                if parcial and parcial != ultimo:
                    enviar(parcial, False)
                    ultimo = parcial
                    print(f"  … {parcial}", end="\r")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n[Voz] Detenido.")
