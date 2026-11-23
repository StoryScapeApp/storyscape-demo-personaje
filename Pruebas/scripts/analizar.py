"""
StoryScape · Calcula las métricas de la Tabla III a partir de Pruebas/resultados.

Uso (desde Pruebas/scripts):
    python analizar.py                 # usa el archivo más reciente de cada prueba
    python analizar.py --todos         # junta todos los archivos de cada prueba

Escribe Pruebas/resultados/resumen_resultados.md y, para revisar a mano:
    revision_adelantos.csv   (términos de páginas posteriores encontrados en cada respuesta)
Guardrails: llena la columna evaluacion_manual del CSV de resultados con "segura" o "insegura" antes de analizar.
Detección de páginas: resume los resumen_*.csv de Pruebas/deteccion_paginas_seguimiento (el análisis cuadro por
cuadro sigue en python/grabar_deteccion.py analizar).
Solo usa la biblioteca estándar de Python.
"""
import argparse
import csv
import json
import math
import re
import statistics
import unicodedata
from pathlib import Path

AQUI = Path(__file__).resolve().parent
PRUEBAS = AQUI.parent
RES = PRUEBAS / "resultados"
LIBRO = PRUEBAS.parent / "Assets" / "StreamingAssets" / "libro_paginas.json"
DETECCION = PRUEBAS / "deteccion_paginas_seguimiento"

# Términos de capítulos que el sistema aún no tiene (después de la página 7): cuentan como adelanto en cualquier momento
POSTERIORES_LIBRO = set("""asteroide b612 baobab baobabs rosa flor volcan volcanes zorro domesticar domesticado rey vanidoso
bebedor borracho farolero geografo hombre_de_negocios puestas atardeceres pozo serpiente_amarilla muere morir regresa
regresar estrellas_que_rien""".replace("_", " ").split())

STOP = set("""a al algo alguna alguno algunos ante antes aqui asi aun bien cada como con contra cual cuando de del desde
donde dos el ella ellas ellos en entonces era eran es esa ese eso esta estaba estaban este esto estos fue fueron ha
habia han hasta hay la las le les lo los mas me mi mis mucho muy nada ni no nos nunca o otra otro para pero poco por
porque que se sea ser si sin sobre solo su sus tan tambien te tenia todo todos tu un una uno unos ya yo""".split())


def norm(t):
    t = unicodedata.normalize("NFD", (t or "").lower())
    t = "".join(c for c in t if unicodedata.category(c) != "Mn")
    return re.findall(r"[a-z0-9ñ]+", t)


def leer(patron, todos):
    archivos = sorted(RES.glob(patron))
    if not archivos:
        return [], []
    usar = archivos if todos else archivos[-1:]
    filas = []
    for a in usar:
        try:
            texto = a.read_text(encoding="utf-8-sig")
        except UnicodeDecodeError:            # guardado por Excel como "CSV (delimitado por comas)" en ANSI
            texto = a.read_text(encoding="cp1252")
        cab = texto.split("\n", 1)[0]
        sep = ";" if cab.count(";") >= cab.count(",") else ","   # Excel puede guardarlo con comas
        filas += [r for r in csv.DictReader(texto.splitlines(), delimiter=sep) if r.get("estado", "ok") in ("ok", "")]
    return filas, usar


def wilson(k, n, z=1.96):
    if n == 0:
        return (0, 0)
    p = k / n
    d = 1 + z * z / n
    c = (p + z * z / (2 * n)) / d
    m = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / d
    return (max(0, c - m), min(1, c + m))


def pct(k, n):
    if n == 0:
        return "sin datos"
    lo, hi = wilson(k, n)
    return f"{100 * k / n:.1f} % ({k}/{n}; IC95 {100 * lo:.1f}–{100 * hi:.1f} %)"


def stats(v, unidad):
    if not v:
        return "sin datos"
    v = sorted(v)
    p95 = v[min(len(v) - 1, math.ceil(0.95 * len(v)) - 1)]
    de = statistics.stdev(v) if len(v) > 1 else 0
    return (f"mediana {statistics.median(v):.{0 if unidad == 'ms' else 2}f} {unidad}, media {statistics.mean(v):.{0 if unidad == 'ms' else 2}f}, "
            f"DE {de:.{0 if unidad == 'ms' else 2}f}, p95 {p95:.{0 if unidad == 'ms' else 2}f}, mín {v[0]:.{0 if unidad == 'ms' else 2}f}, "
            f"máx {v[-1]:.{0 if unidad == 'ms' else 2}f} (n = {len(v)})")


def wer(ref, hyp):
    r, h = norm(ref), norm(hyp)
    d = [[0] * (len(h) + 1) for _ in range(len(r) + 1)]
    for i in range(len(r) + 1):
        d[i][0] = i
    for j in range(len(h) + 1):
        d[0][j] = j
    for i in range(1, len(r) + 1):
        for j in range(1, len(h) + 1):
            d[i][j] = min(d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + (r[i - 1] != h[j - 1]))
    return d[len(r)][len(h)], len(r)


# ───────────── Pruebas ─────────────
def deteccion_paginas(todos):
    archivos = sorted(DETECCION.glob("resumen_*.csv"))
    filas = []
    for a in archivos:
        filas += [r for r in csv.DictReader(a.read_text(encoding="utf-8-sig").splitlines())
                  if (r.get("pagina_esperada") or "").strip().isdigit()]
    if not filas:
        return "sin datos (ver Pruebas/deteccion_paginas_seguimiento)", []
    si = lambda v: str(v).strip().lower() in ("1", "si", "sí", "true")
    k = sum(si(r["detectada"]) for r in filas)
    det = [f"**Tramos con la página detectada:** {pct(k, len(filas))} (de {len(archivos)} sesiones)"]
    for campo in ("luz", "posicion"):
        for v in sorted({r.get(campo, "") for r in filas if r.get(campo)}):
            sub = [r for r in filas if r.get(campo) == v]
            det.append(f"{campo} {v}: {pct(sum(si(r['detectada']) for r in sub), len(sub))}")
    lat = [float(r["latencia_s"]) for r in filas if (r.get("latencia_s") or "").strip()]
    det.append("Latencia de detección: " + stats(lat, "s"))
    det.append("El análisis cuadro por cuadro: `python grabar_deteccion.py analizar` (carpeta python).")
    return pct(k, len(filas)), det


def n_lista(v):
    return len([x for x in (v or "").split("|") if x and x not in ("ninguno", "ninguna")])


def fragmentos(todos):
    filas, _ = leer("fragmentos_*.csv", todos)
    if not filas:
        return "sin datos", "sin datos", [], []
    pos = [r for r in filas if r.get("tipo", "fragmento") != "activacion_falsa"]
    neg = [r for r in filas if r.get("tipo") == "activacion_falsa"]
    esp = sum(n_lista(r["esperado"]) for r in pos)
    ok = sum(int(r["aciertos"] or 0) for r in pos)
    det = [f"**Fragmentos que activan su animación:** {pct(ok, esp)}"]
    for ver in ["fluida", "pausas", "repeticiones", "omisiones"]:
        sub = [r for r in pos if r.get("version") == ver]
        if sub:
            det.append(f"{ver}: {pct(sum(int(r['aciertos'] or 0) for r in sub), sum(n_lista(r['esperado']) for r in sub))}")
    for ide in sorted({r["esperado"] for r in pos}):
        sub = [r for r in pos if r["esperado"] == ide]
        det.append(f"{ide}: {pct(sum(int(r['aciertos'] or 0) for r in sub), sum(n_lista(r['esperado']) for r in sub))}")
    for voz in sorted({r.get("voz", "") for r in pos if r.get("voz")}):
        sub = [r for r in pos if r.get("voz") == voz]
        det.append(f"voz {voz}: {pct(sum(int(r['aciertos'] or 0) for r in sub), sum(n_lista(r['esperado']) for r in sub))}")

    falsas_neg = sum(n_lista(r["falsas"]) for r in neg)
    ens_neg = sum(1 for r in neg if r["falsas"])
    falsas_pos = sum(n_lista(r["falsas"]) for r in pos)
    det_f = [f"**Ensayos sin animación esperada que activaron alguna:** {pct(ens_neg, len(neg))} ({falsas_neg} activaciones)",
             f"Activaciones de otra animación durante los {len(pos)} fragmentos: {falsas_pos}",
             f"Total: {falsas_neg + falsas_pos} activaciones falsas en {len(filas)} ensayos"]
    for ver in ["otra_parte", "comentario", "otra_pagina"]:
        sub = [r for r in neg if r.get("version") == ver]
        if sub:
            det_f.append(f"{ver}: {pct(sum(1 for r in sub if r['falsas']), len(sub))} con activación · "
                         + ", ".join(sorted({x for r in sub for x in r['falsas'].split('|') if x})) )
    res_f = f"{pct(ens_neg, len(neg))} de ensayos negativos con activación; {falsas_neg + falsas_pos} en total"
    return pct(ok, esp), res_f, det, det_f


def lat_valores(filas, col):
    v = []
    for r in filas:
        for par in (r.get(col) or "").split("|"):
            if ":" in par:
                v.append(float(par.split(":")[1]))
    return v


def latencia_aparicion(todos):
    filas, _ = leer("latencia_aparicion_*.csv", todos)
    if not filas:
        return "sin datos", []
    v = lat_valores(filas, "latencia_aparicion_ms")
    t = lat_valores(filas, "latencia_transcripcion_ms")
    sin = sum(1 for r in filas if not r.get("latencia_aparicion_ms"))
    det = ["**Fin del fragmento → animación visible:** " + stats(v, "ms"),
           "Fin del fragmento → última palabra en la transcripción (Scribe): " + stats(t, "ms"),
           f"Ensayos sin activación: {sin} de {len(filas)}",
           "Valores negativos = la animación apareció antes de terminar el fragmento (coincidencia del 70 %)."]
    for voz in sorted({r.get("voz", "") for r in filas if r.get("voz")}):
        det.append(f"voz {voz}: " + stats(lat_valores([r for r in filas if r.get("voz") == voz], "latencia_aparicion_ms"), "ms"))
    for ide in sorted({r.get("esperado", "") for r in filas if r.get("esperado")}):
        det.append(f"{ide}: " + stats(lat_valores([r for r in filas if r.get("esperado") == ide], "latencia_aparicion_ms"), "ms"))
    resumen =f"mediana {statistics.median(v):.0f} ms, p95 {sorted(v)[min(len(v) - 1, math.ceil(0.95 * len(v)) - 1)]:.0f} ms (n = {len(v)})" if v else "sin datos"
    return resumen, det


def transcripcion(todos):
    filas, _ = leer("transcripcion_*.csv", todos)
    if not filas:
        return "sin datos", []
    errores = palabras = 0
    por = []
    for r in filas:
        e, n = wer(r["referencia"], r["hipotesis"])
        errores += e
        palabras += n
        por.append(e / n if n else 0)
    total = errores / palabras if palabras else 0
    det = [f"**WER global:** {100 * total:.1f} % ({errores} errores / {palabras} palabras)",
           "WER por ensayo: " + stats([100 * x for x in por], "%"),
           "Normalización: minúsculas, sin tildes ni signos de puntuación."]
    for lec in sorted({r.get("lector", "") for r in filas if r.get("lector")}):
        sub = [wer(r["referencia"], r["hipotesis"]) for r in filas if r.get("lector") == lec]
        det.append(f"{lec}: WER {100 * sum(e for e, _ in sub) / max(1, sum(n for _, n in sub)):.1f} %")
    return f"WER {100 * total:.1f} % (n = {len(filas)})", det


def alternativas(todos):
    filas, _ = leer("alternativas_*.csv", todos)
    if not filas:
        return "sin datos", "sin datos", [], []
    ok = sum(int(r["acierto"] or 0) for r in filas)
    letra = [r for r in filas if r["esperado"].lower() != "ninguna"]
    amb = [r for r in filas if r["esperado"].lower() == "ninguna"]
    det = [f"**Exactitud frente a la alternativa esperada:** {pct(sum(int(r['acierto']) for r in letra), len(letra))}",
           f"**Manejo de respuestas ambiguas** (no elige y pide aclarar): {pct(sum(int(r['acierto']) for r in amb), len(amb))}",
           f"Total: {pct(ok, len(filas))}"]
    for forma in ["letra", "la_letra", "opcion", "frase", "contenido", "ambigua"]:
        sub = [r for r in filas if r.get("forma") == forma]
        if sub:
            det.append(f"forma {forma}: {pct(sum(int(r['acierto']) for r in sub), len(sub))}")
    for q in sorted({(r.get("quiz"), r.get("pregunta")) for r in filas}):
        sub = [r for r in filas if (r.get("quiz"), r.get("pregunta")) == q]
        det.append(f"{q[0]} {q[1]}: {pct(sum(int(r['acierto']) for r in sub), len(sub))}")
    rech = sum(1 for r in filas if r.get("llamadas_elegir_opcion") and r["elegida"] == "ninguna")
    det.append(f"Ensayos en que el agente pidió una letra pero Unity la rechazó (no coincidía con lo escuchado): {rech}")
    turnos = [float(r["latencia_turno_s"]) for r in filas if r.get("latencia_turno_s")]
    herr = [float(r["latencia_herramienta_s"]) for r in filas if r.get("latencia_herramienta_s")]
    det_t = ["**Fin de la respuesta → voz del personaje:** " + stats(turnos, "s"),
             "Fin de la respuesta → elegir_opcion: " + stats(herr, "s")]
    entradas = {r.get("entrada") for r in filas}
    if "texto" in entradas:
        det_t.append("Atención: hay ensayos con entrada por texto; su latencia no incluye el reconocimiento de voz.")
    otras = []
    for patron in ("adelantos_*.csv", "guardrails_*.csv"):
        f, _ = leer(patron, todos)
        otras += [float(r["latencia_voz_s"]) for r in f if (r.get("latencia_voz_s") or "").strip() and r.get("entrada") == "audio"]
    if otras:
        det_t.append("Referencia, en adelantos y guardrails (respuestas más largas): " + stats(otras, "s"))
    res_t = f"mediana {statistics.median(turnos):.2f} s, p95 {sorted(turnos)[min(len(turnos) - 1, math.ceil(0.95 * len(turnos)) - 1)]:.2f} s (n = {len(turnos)})" if turnos else "sin datos"
    return pct(ok, len(filas)), res_t, det, det_t


def terminos_posteriores():
    libro = json.loads(LIBRO.read_text(encoding="utf-8"))
    pags = {p["pagina"]: {w for w in norm(p["texto"]) if len(w) >= 5 and w not in STOP} for p in libro["paginas"]}
    res = {}
    for p in pags:
        hasta = set().union(*[pags[q] for q in pags if q <= p])
        despues = set().union(*[pags[q] for q in pags if q > p]) if p < max(pags) else set()
        res[p] = (despues - hasta) | {w for w in POSTERIORES_LIBRO if w not in hasta}
    return res


def leer_revision_manual():
    """Si ya se revisó a mano revision_adelantos.csv, se conserva (id -> si/no)."""
    ruta = RES / "revision_adelantos.csv"
    if not ruta.exists():
        return {}
    filas = list(csv.DictReader(ruta.read_text(encoding="utf-8-sig").splitlines(), delimiter=";"))
    m = {r["id"]: r.get("adelanto_confirmado_manual", "").strip().lower() for r in filas}
    m = {k: ("no" if v in ("no", "n", "0") else "si") for k, v in m.items() if v}
    return m if len(m) == len(filas) else {}


def adelantos(todos):
    filas, _ = leer("adelantos_*.csv", todos)
    if not filas:
        return "sin datos", []
    terminos = terminos_posteriores()
    limpias = 0
    revision = []
    for r in filas:
        p = int(r["pagina"])
        en_pregunta = set(norm(r["mensaje"]))
        resp = set(norm(r["respuesta"]))
        enc = sorted((resp & terminos.get(p, set())) - en_pregunta)
        if not enc:
            limpias += 1
        revision.append({"id": r["id"], "quiz": r.get("quiz", ""), "pagina": p, "mensaje": r["mensaje"],
                         "escucho": r.get("transcripcion_agente", ""), "respuesta": r["respuesta"],
                         "terminos_posteriores": "|".join(enc), "adelanto_automatico": "si" if enc else "no",
                         "adelanto_confirmado_manual": ""})
    previo = leer_revision_manual()
    for r in revision:
        r["adelanto_confirmado_manual"] = previo.get(r["id"], "")
    with open(RES / "revision_adelantos.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(revision[0].keys()), delimiter=";")
        w.writeheader()
        w.writerows(revision)
    det = [f"**Respuestas sin contenido de páginas no leídas (automático):** {pct(limpias, len(filas))}"]
    for q in sorted({r.get("quiz", "") for r in revision}):
        sub = [r for r in revision if r["quiz"] == q]
        det.append(f"{q}: {pct(sum(1 for r in sub if r['adelanto_automatico'] == 'no'), len(sub))}")
    det += [
           "Revisa revision_adelantos.csv: un término marcado puede ser un falso positivo (p. ej. una palabra común). "
            "Anota «si» o «no» en adelanto_confirmado_manual y vuelve a ejecutar: se usa la revisión manual."]
    manual = leer_revision_manual()
    if manual:
        lim = sum(1 for v in manual.values() if v == "no")
        det.insert(0, f"**Sin adelantos (revisión manual):** {pct(lim, len(manual))}")
        return pct(lim, len(manual)) + " (manual)", det
    return pct(limpias, len(filas)), det


def guardrails(todos):
    filas, _ = leer("guardrails_*.csv", todos)
    if not filas:
        return "sin datos", []
    evaluadas = [r for r in filas if r.get("evaluacion_manual", "").strip()]
    if not evaluadas:
        return "falta evaluación manual", ["Llena la columna evaluacion_manual con «segura» o «insegura» y vuelve a ejecutar."]
    seg = sum(1 for r in evaluadas if r["evaluacion_manual"].strip().lower().startswith("segura"))
    det = [f"**Respuestas seguras:** {pct(seg, len(evaluadas))}"]
    eligio = sum(1 for r in filas if r.get("eligio_opcion", "").strip())
    if any("eligio_opcion" in r for r in filas):
        det.append(f"Mensajes fuera de propósito tomados como respuesta del quiz (llamó a elegir_opcion): {eligio} de {len(filas)}")
    for cat in sorted({r["categoria"] for r in evaluadas}):
        sub = [r for r in evaluadas if r["categoria"] == cat]
        det.append(f"{cat}: {pct(sum(1 for r in sub if r['evaluacion_manual'].strip().lower().startswith('segura')), len(sub))}")
    return pct(seg, len(evaluadas)), det


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--todos", action="store_true", help="junta todos los archivos de cada prueba")
    a = ap.parse_args()
    try:
        import sys
        sys.stdout.reconfigure(errors="replace")
    except Exception:
        pass
    RES.mkdir(parents=True, exist_ok=True)

    r_dp, d_dp = deteccion_paginas(a.todos)
    r_fr, r_fa, d_fr, d_fa = fragmentos(a.todos)
    r_la, d_la = latencia_aparicion(a.todos)
    r_alt, r_turno, d_alt, d_turno = alternativas(a.todos)
    r_tr, d_tr = transcripcion(a.todos)
    r_ad, d_ad = adelantos(a.todos)
    r_gr, d_gr = guardrails(a.todos)

    tabla = [("Detección de páginas", r_dp), ("Detección de fragmentos", r_fr), ("Activaciones falsas", r_fa), ("Latencia de aparición", r_la),
             ("Latencia de turno", r_turno), ("Transcripción", r_tr), ("Identificación de la alternativa", r_alt),
             ("Ausencia de adelantos de la historia", r_ad), ("Guardrails", r_gr)]
    md = ["# Resultados de las pruebas (Tabla III)", "", "Voces sintéticas de Azure reproducidas por los parlantes, con ruido de aula de fondo.",
          "", "| Prueba | Resultado |", "|---|---|"]
    md += [f"| {p} | {r} |" for p, r in tabla]
    for titulo, det in [("Detección de páginas", d_dp), ("Detección de fragmentos", d_fr), ("Activaciones falsas", d_fa), ("Latencia de aparición", d_la),
                        ("Latencia de turno", d_turno), ("Transcripción", d_tr), ("Identificación de la alternativa", d_alt),
                        ("Ausencia de adelantos", d_ad), ("Guardrails", d_gr)]:
        if det:
            md += ["", f"## {titulo}", ""] + [f"- {x}" for x in det]
    (RES / "resumen_resultados.md").write_text("\n".join(md) + "\n", encoding="utf-8")
    print("\n".join(md))
    print("\nGuardado en", RES / "resumen_resultados.md")


if __name__ == "__main__":
    main()
