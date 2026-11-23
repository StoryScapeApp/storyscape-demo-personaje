"""
StoryScape · Genera los audios de prueba con voces sintéticas (Azure o Google Cloud), NO con ElevenLabs.

Lee los planes que tienen columna "audio" (por defecto los 5 planes de voz) y crea en Pruebas/audios los
archivos que falten, en WAV 16 kHz mono. Usa la columna "texto" y la voz de la columna "voz" (v1, v2, v3, v4).
Para acercarse a la voz de un niño: tono y velocidad configurables abajo. Declara esto en el paper.
Los audios se reproducen después POR LOS PARLANTES (PruebasRunner en Unity) y el sistema los escucha con su micrófono.

Claves (NUNCA en el código ni en el repositorio; se leen de variables de entorno):
    Azure:  set AZURE_SPEECH_KEY=...   y   set AZURE_SPEECH_REGION=eastus   (o tu región)
    Google: set GOOGLE_TTS_API_KEY=...

Uso (desde Pruebas/scripts):
    python generar_audios.py --proveedor azure --listar      # cuántos faltan y cuántos caracteres (no gasta)
    python generar_audios.py --proveedor azure               # genera todo lo que falte
    python generar_audios.py --proveedor azure --planes alternativas.csv --espera 0.5   # con clave de pago (S0)
    python generar_audios.py --proveedor azure --planes transcripcion.csv --completar
        (--completar anota en el plan un audio y una voz para cada ensayo que no tenga, y luego los genera:
         así fragmentos y transcripción también se hacen con voces sintéticas en lugar de leer en vivo)
    python generar_audios.py --proveedor google --planes alternativas.csv
Solo usa la biblioteca estándar de Python.
"""
import argparse
import base64
import csv
import http.client
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from xml.sax.saxutils import escape

AQUI = Path(__file__).resolve().parent
PRUEBAS = AQUI.parent
PLANES = PRUEBAS / "planes"
AUDIOS = PRUEBAS / "audios"

# Voces en español latinoamericano (verifica los nombres disponibles en la consola de tu proveedor)
VOCES = {
    "azure": {"v1": "es-PE-CamilaNeural", "v2": "es-PE-AlexNeural", "v3": "es-MX-DaliaNeural", "v4": "es-US-PalomaNeural"},
    "google": {"v1": ("es-US", "es-US-Neural2-A"), "v2": ("es-US", "es-US-Neural2-B"), "v3": ("es-US", "es-US-Neural2-C"), "v4": ("es-US", "es-US-Neural2-A")},
}
TONO_SEMITONOS = 1        # voz más aguda, cercana a la de un niño
VELOCIDAD = 0.85          # 15 % más lenta
PAUSA = "1.5s"            # duración de cada «…» suelto del texto (versión con pausas)
PAUSA_CORTA = "0.3s"      # «ani…» pegado a una palabra (versión con repeticiones)
ESPERA_ENTRE = 3.5        # segundos entre peticiones (el plan gratuito F0 limita las peticiones por minuto)
REINTENTOS = 6


def con_reintentos(fn, *args):
    """Si el servicio responde 429 (demasiadas peticiones) o falla un momento, espera y vuelve a intentar."""
    for intento in range(1, REINTENTOS + 1):
        try:
            return fn(*args)
        except urllib.error.HTTPError as e:
            if e.code not in (429, 500, 502, 503, 504) or intento == REINTENTOS:
                raise
            espera = float(e.headers.get("Retry-After") or 0) or min(60, 10 * intento)
            print(f"    límite del servicio ({e.code}); espero {espera:.0f} s y reintento…")
            time.sleep(espera)
        except (urllib.error.URLError, http.client.HTTPException, ConnectionError, TimeoutError) as e:
            # Red inestable: la conexión se cortó a mitad de la descarga (IncompleteRead) o no respondió
            if intento == REINTENTOS:
                raise
            print(f"    se cortó la conexión ({type(e).__name__}); reintento en {5 * intento} s…")
            time.sleep(5 * intento)


def cuerpo_ssml(texto):
    """«…» suelto = pausa larga; «ani…» pegado a una palabra = repetición con una pausa corta."""
    texto = re.sub(r"(\w)…", r"\1⟨c⟩", texto)            # repetición: ani… animaciones
    texto = re.sub(r"\s*…\s*", "⟨l⟩", texto)              # pausa de lectura
    s = escape(texto)
    return s.replace("⟨c⟩", f'<break time="{PAUSA_CORTA}"/>').replace("⟨l⟩", f'<break time="{PAUSA}"/>')


def ssml_azure(texto, voz):
    cuerpo = cuerpo_ssml(texto)
    rate = f"{int((VELOCIDAD - 1) * 100):+d}%"
    return (f'<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="es-PE">'
            f'<voice name="{voz}"><prosody pitch="{TONO_SEMITONOS:+d}st" rate="{rate}">{cuerpo}</prosody></voice></speak>')


def sintetizar_azure(texto, voz):
    key, region = os.environ.get("AZURE_SPEECH_KEY"), os.environ.get("AZURE_SPEECH_REGION")
    if not key or not region:
        sys.exit("Falta AZURE_SPEECH_KEY o AZURE_SPEECH_REGION en las variables de entorno.")
    req = urllib.request.Request(
        f"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1",
        data=ssml_azure(texto, voz).encode("utf-8"),
        headers={"Ocp-Apim-Subscription-Key": key, "Content-Type": "application/ssml+xml",
                 "X-Microsoft-OutputFormat": "riff-16khz-16bit-mono-pcm", "User-Agent": "StoryScape-pruebas"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def sintetizar_google(texto, voz):
    key = os.environ.get("GOOGLE_TTS_API_KEY")
    if not key:
        sys.exit("Falta GOOGLE_TTS_API_KEY en las variables de entorno.")
    idioma, nombre = voz
    ssml = "<speak>" + cuerpo_ssml(texto) + "</speak>"
    body = {"input": {"ssml": ssml}, "voice": {"languageCode": idioma, "name": nombre},
            "audioConfig": {"audioEncoding": "LINEAR16", "sampleRateHertz": 16000,
                            "pitch": TONO_SEMITONOS, "speakingRate": VELOCIDAD}}
    req = urllib.request.Request(f"https://texttospeech.googleapis.com/v1/text:synthesize?key={key}",
                                 data=json.dumps(body).encode("utf-8"), headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return base64.b64decode(json.loads(r.read())["audioContent"])


def completar_plan(ruta):
    """Pone audio = <id>_<voz>.wav y voz (v1, v2, v3 rotando) en las filas que no tengan audio."""
    with open(ruta, encoding="utf-8-sig") as f:
        filas = list(csv.DictReader(f, delimiter=";"))
    if not filas:
        return
    columnas = list(filas[0].keys())
    for c in ("voz", "audio"):
        if c not in columnas:
            columnas.insert(columnas.index("id") + 1, c)
    cambios = 0
    for i, r in enumerate(filas):
        if (r.get("audio") or "").strip():
            continue
        voz = (r.get("voz") or "").strip() or f"v{i % 3 + 1}"
        r["voz"], r["audio"] = voz, f"{r['id']}_{voz}.wav"
        if "lector" in r:
            r["lector"] = f"sintética {voz}"   # en el análisis aparece por voz en lugar de por autor
        cambios += 1
    with open(ruta, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=columnas, delimiter=";", extrasaction="ignore")
        w.writeheader()
        w.writerows(filas)
    print(f"{ruta.name}: {cambios} ensayos ahora usan audio sintético")


def main():
    try:
        sys.stdout.reconfigure(errors="replace")   # consola de Windows sin UTF-8
    except Exception:
        pass
    ap = argparse.ArgumentParser()
    ap.add_argument("--proveedor", choices=["azure", "google"], required=True)
    ap.add_argument("--planes", nargs="+",
                    default=["fragmentos.csv", "latencia_aparicion.csv", "alternativas.csv", "adelantos.csv", "guardrails.csv"])
    ap.add_argument("--espera", type=float, default=ESPERA_ENTRE,
                    help="segundos entre peticiones (3.5 para el plan gratuito F0; 0.5 basta con S0)")
    ap.add_argument("--listar", action="store_true", help="solo muestra los audios que faltan")
    ap.add_argument("--rehacer", action="store_true", help="vuelve a generar aunque el archivo exista")
    ap.add_argument("--completar", action="store_true",
                    help="anota en el plan audio y voz (v1, v2, v3) para los ensayos que no tengan, y los genera")
    a = ap.parse_args()

    AUDIOS.mkdir(parents=True, exist_ok=True)
    pendientes = {}
    for nombre in a.planes:
        if not (PLANES / nombre).exists():
            print(f"  (no existe {nombre}; ejecuta generar_planes.py)")
            continue
        if a.completar:
            completar_plan(PLANES / nombre)
        with open(PLANES / nombre, encoding="utf-8-sig") as f:
            for r in csv.DictReader(f, delimiter=";"):
                texto = (r.get("texto") or r.get("referencia") or "").strip()
                audio, voz = r.get("audio", "").strip(), r.get("voz", "").strip() or "v1"
                if not audio or not texto:
                    continue
                if a.rehacer or not (AUDIOS / audio).exists():
                    pendientes[audio] = (texto, voz)

    print(f"Audios por generar: {len(pendientes)} (carpeta {AUDIOS})")
    caracteres = sum(len(t) for t, _ in pendientes.values())
    print(f"Caracteres aproximados: {caracteres} (más etiquetas SSML; el plan gratuito F0 de Azure incluye 500 000 al mes)")
    if pendientes and not a.listar:
        print(f"Tiempo estimado: {len(pendientes) * (a.espera + 1.2) / 60:.0f} min")
    if a.listar:
        for k, (t, v) in sorted(pendientes.items()):
            print(f"  {k}  [{v}]  {t}")
        return

    voces = VOCES[a.proveedor]
    for i, (archivo, (texto, voz)) in enumerate(sorted(pendientes.items()), 1):
        if voz not in voces:
            print(f"  ! {archivo}: voz '{voz}' no está en VOCES; se usa v1")
            voz = "v1"
        fn = sintetizar_azure if a.proveedor == "azure" else sintetizar_google
        wav = con_reintentos(fn, texto, voces[voz])
        (AUDIOS / archivo).write_bytes(wav)
        print(f"  [{i}/{len(pendientes)}] {archivo}")
        if i < len(pendientes):
            time.sleep(a.espera)
    print("Listo.")


if __name__ == "__main__":
    main()
