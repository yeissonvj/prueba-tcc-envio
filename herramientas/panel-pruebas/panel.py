"""Panel de pruebas manuales: carga, reconciliación y caídas controladas.

Sirve una página en http://127.0.0.1:8095 y ejecuta, al pulsar cada botón, las mismas pruebas que ya se
hicieron por consola (k6 + reconciliación, brokers caídos, PostgreSQL, Redis, proveedor de SMS, kill del
procesador) contra el entorno que esté levantado: local (3 brokers) o Aiven.

Seguridad: controla Docker en esta máquina, por eso escucha SOLO en 127.0.0.1 y nunca debe publicarse
(no está detrás del proxy de ngrok). Los secretos se leen del disco y no se envían al navegador ni al registro.

    python herramientas/panel-pruebas/panel.py
"""
from __future__ import annotations

import json
import os
import re
import subprocess
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

PUERTO = int(os.environ.get("PANEL_PUERTO", "8095"))
RAIZ = Path(__file__).resolve().parents[2]
PAGINA = Path(__file__).with_name("panel.html")
RESULTADOS = RAIZ / "pruebas-carga" / "resultados"
IMAGEN_K6 = "grafana/k6:2.3.0"
IMAGEN_PSQL = "postgres:17"
TOKEN_URL = "http://127.0.0.1:8081/realms/tcc/protocol/openid-connect/token"
ZONA_COLOMBIA = timezone(timedelta(hours=-5))

ENTORNOS = {
    "local": {
        "nombre": "Local (3 brokers)",
        "proyecto": "tcc-eventos",
        "compose": RAIZ / "infra" / "docker-compose.yml",
        "red": "tcc-eventos_default",
        "api": "http://127.0.0.1:8090",
        "componentes": ["api", "procesador", "notificador", "redis", "postgres", "kafka-1", "kafka-2", "kafka-3"],
        "carga_sugerida": {"tasa": 200, "duracion": 30},
        "espera_visible": 20,
    },
    "aiven": {
        "nombre": "Aiven (Kafka y PostgreSQL en la nube)",
        "proyecto": "tcc-eventos-aiven",
        "compose": RAIZ / "infra" / "aiven" / "docker-compose.yml",
        "red": "tcc-eventos-aiven_default",
        "api": "http://127.0.0.1:8091",
        "componentes": ["api", "procesador", "notificador", "redis"],
        # El procesador local contra la base remota drena ~5 ev/s (docs/demo-aiven.md): cargas pequeñas.
        "carga_sugerida": {"tasa": 20, "duracion": 30},
        "espera_visible": 60,
    },
}

# ---------------------------------------------------------------- registro en vivo y tarea en curso

class Registro:
    """Líneas que el navegador lee por sondeo (?desde=n). Una sola tarea a la vez."""

    def __init__(self) -> None:
        self._lineas: list[dict] = []
        self._candado = threading.Lock()
        self.tarea: str | None = None
        self.resultado: dict | None = None

    def escribir(self, texto: str, tipo: str = "info") -> None:
        with self._candado:
            self._lineas.append({"n": len(self._lineas), "hora": datetime.now().strftime("%H:%M:%S"), "tipo": tipo, "texto": texto})

    def desde(self, n: int) -> list[dict]:
        with self._candado:
            return self._lineas[n:]

    def total(self) -> int:
        with self._candado:
            return len(self._lineas)


registro = Registro()
candado_tarea = threading.Lock()


def miles(n: int) -> str:
    return f"{n:,}".replace(",", ".")


def ok(texto: str) -> None: registro.escribir(texto, "ok")
def mal(texto: str) -> None: registro.escribir(texto, "mal")
def info(texto: str) -> None: registro.escribir(texto, "info")
def titulo(texto: str) -> None: registro.escribir(texto, "titulo")
def nota(texto: str) -> None: registro.escribir(texto, "nota")

# ---------------------------------------------------------------- Docker

def docker(*args: str, entorno_extra: dict | None = None, timeout: int = 120, mostrar: bool = False) -> subprocess.CompletedProcess:
    env = {**os.environ, **(entorno_extra or {})}
    proceso = subprocess.run(["docker", *args], capture_output=True, text=True, encoding="utf-8", errors="replace",
                             env=env, timeout=timeout)
    if mostrar:
        for linea in (proceso.stdout + proceso.stderr).splitlines():
            if linea.strip():
                nota(linea)
    return proceso


def contenedor(entorno: dict, servicio: str) -> str:
    return f"{entorno['proyecto']}-{servicio}-1"


def estados_contenedores() -> dict[str, str]:
    salida = docker("ps", "-a", "--format", "{{.Names}}\t{{.State}}").stdout
    return dict(linea.split("\t", 1) for linea in salida.splitlines() if "\t" in linea)


def entorno_activo() -> str | None:
    estados = estados_contenedores()
    for clave, entorno in ENTORNOS.items():
        if estados.get(contenedor(entorno, "api")) == "running":
            return clave
    return None


def detener(entorno: dict, servicio: str) -> None:
    docker("stop", "-t", "5", contenedor(entorno, servicio))
    mal(f"■ {servicio} detenido")


def iniciar(entorno: dict, servicio: str) -> None:
    docker("start", contenedor(entorno, servicio))
    ok(f"▶ {servicio} iniciado")


def recrear_notificador(entorno: dict, sms_caido: bool) -> None:
    variables = {"SIMULAR_SMS_CAIDO": "true" if sms_caido else "false"}
    if sms_caido:  # escalera corta para verla en minutos, no en horas
        variables |= {"ESPERA_REINTENTO_1": "00:00:05", "ESPERA_REINTENTO_2": "00:00:10", "ESPERA_REINTENTO_3": "00:00:15"}
    r = docker("compose", "-f", str(entorno["compose"]), "up", "-d", "--no-deps", "--no-build", "notificador",
               entorno_extra=variables, timeout=180)
    if r.returncode != 0:
        raise RuntimeError(f"No se pudo recrear el notificador: {r.stderr.strip()[-300:]}")

# ---------------------------------------------------------------- PostgreSQL (sin exponer credenciales)

def leer_env_aiven() -> dict[str, str]:
    archivo = RAIZ / "infra" / "aiven" / ".env"
    valores = {}
    for linea in archivo.read_text(encoding="utf-8").splitlines():
        if "=" in linea and not linea.lstrip().startswith("#"):
            clave, valor = linea.split("=", 1)
            valores[clave.strip()] = valor.strip()
    return valores


def sql(clave_entorno: str, consulta: str) -> str:
    """Devuelve el resultado de una consulta de una sola columna (texto)."""
    entorno = ENTORNOS[clave_entorno]
    if clave_entorno == "local":
        r = docker("exec", contenedor(entorno, "postgres"), "psql", "-U", "tcc", "-d", "tcc_eventos", "-tAc", consulta, timeout=60)
    else:
        env = leer_env_aiven()
        cadena = (f"host={env['AIVEN_PG_HOST']} port={env['AIVEN_PG_PUERTO']} dbname={env.get('AIVEN_PG_BASE') or 'defaultdb'} "
                  f"user={env.get('AIVEN_PG_USUARIO') or 'avnadmin'} sslmode=verify-full sslrootcert=/c/ca.pem gssencmode=disable connect_timeout=15")
        r = docker("run", "--rm", "-e", "PGPASSWORD", "-v", f"{RAIZ / 'infra' / 'aiven' / 'certificados'}:/c:ro",
                   IMAGEN_PSQL, "psql", cadena, "-tAc", consulta,
                   entorno_extra={"PGPASSWORD": env["AIVEN_PG_CONTRASENA"]}, timeout=90)
    if r.returncode != 0:
        if "is not running" in r.stderr:
            raise RuntimeError("PostgreSQL está detenido (esperado si lo tumbaste)")
        detalle = r.stderr.strip().splitlines()[-1] if r.stderr.strip() else "sin detalle"
        raise RuntimeError(f"No se pudo consultar PostgreSQL: {detalle}")
    return r.stdout.strip()


def lag_procesador_local() -> int | None:
    """Lag del grupo del procesador, leído desde cualquier broker que siga arriba. None si no se puede medir."""
    estados = estados_contenedores()
    for broker in ("kafka-1", "kafka-2", "kafka-3"):
        nombre = contenedor(ENTORNOS["local"], broker)
        if estados.get(nombre) != "running":
            continue
        r = docker("exec", nombre, "/opt/kafka/bin/kafka-consumer-groups.sh", "--bootstrap-server", "localhost:19092",
                   "--describe", "--group", "procesador-estado", timeout=60)
        if "LOG-END-OFFSET" in r.stdout:
            return sum(int(c[5]) for c in (l.split() for l in r.stdout.splitlines()) if len(c) > 5 and c[5].isdigit())
    return None

# ---------------------------------------------------------------- HTTP hacia la API

def secretos_clientes() -> dict[str, str]:
    datos = json.loads((RAIZ / "infra" / "keycloak" / "tcc-realm.json").read_text(encoding="utf-8"))
    return {c["clientId"]: c.get("secret", "") for c in datos["clients"]}


def pedir(metodo: str, url: str, cuerpo: bytes | None = None, cabeceras: dict | None = None, timeout: float = 20) -> tuple[int, str, float]:
    inicio = time.perf_counter()
    solicitud = urllib.request.Request(url, data=cuerpo, method=metodo, headers=cabeceras or {})
    try:
        with urllib.request.urlopen(solicitud, timeout=timeout) as r:
            return r.status, r.read().decode("utf-8", "replace"), (time.perf_counter() - inicio) * 1000
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace"), (time.perf_counter() - inicio) * 1000
    except (urllib.error.URLError, TimeoutError) as e:
        return 0, str(e), (time.perf_counter() - inicio) * 1000


def token(cliente: str) -> str:
    cuerpo = urllib.parse.urlencode({"grant_type": "client_credentials", "client_id": cliente,
                                     "client_secret": secretos_clientes()[cliente]}).encode()
    estado, texto, _ = pedir("POST", TOKEN_URL, cuerpo, {"Content-Type": "application/x-www-form-urlencoded"})
    if estado != 200:
        raise RuntimeError(f"Keycloak no entregó token para {cliente} (HTTP {estado})")
    return json.loads(texto)["access_token"]


def salud_api(entorno: dict) -> str:
    estado, texto, _ = pedir("GET", entorno["api"] + "/salud/lista", timeout=5)
    if estado == 0:
        return "sin respuesta"
    return texto.strip() or str(estado)

# ---------------------------------------------------------------- acciones

def accion_fallas(clave: str, accion: str) -> None:
    entorno = ENTORNOS[clave]
    local = clave == "local"
    brokers = ["kafka-3", "kafka-2", "kafka-1"]

    if accion in ("broker-1", "broker-2", "kafka-completo", "postgres") and not local:
        raise RuntimeError("En Aiven, Kafka y PostgreSQL son administrados: no se pueden apagar. Usa el entorno local.")

    if accion == "broker-1":
        titulo("1 broker caído (de 3)")
        detener(entorno, "kafka-3")
        nota("Esperado: todo sigue normal. Quedan 2 réplicas sincronizadas = min.insync.replicas → 202 desde Kafka y /salud/lista sigue Healthy.")
    elif accion == "broker-2":
        titulo("2 brokers caídos (de 3)")
        for b in brokers[:2]:
            detener(entorno, b)
        nota("Esperado: Kafka pierde el quórum y no confirma escrituras (acks=all, ISR < 2). La API responde 202 igual, "
             "guardando en la contingencia de PostgreSQL; los primeros tardan hasta que se abre el circuito.")
    elif accion == "kafka-completo":
        titulo("Kafka completo caído")
        for b in brokers:
            detener(entorno, b)
        nota("Esperado: 202 desde la contingencia de PostgreSQL. Con el circuito abierto, en ~0,1 s.")
    elif accion == "recuperar-kafka":
        titulo("Recuperar Kafka")
        for b in reversed(brokers):
            iniciar(entorno, b)
        info("Esperando a que el relay reenvíe a Kafka lo guardado en la contingencia (hasta 3 min)…")
        inicio = time.time()
        while time.time() - inicio < 180:
            pendientes = int(sql(clave, "SELECT count(*) FROM contingencia_eventos") or 0)
            info(f"+{time.time() - inicio:.0f} s  eventos en contingencia: {miles(pendientes)}")
            if pendientes == 0:
                ok("✓ Contingencia vacía: todo lo recibido durante la caída ya está en Kafka")
                break
            time.sleep(5)
    elif accion == "postgres":
        titulo("PostgreSQL caído")
        detener(entorno, "postgres")
        nota("Esperado: la API sigue respondiendo 202 (Kafka guarda). El procesador reintenta sin saltarse eventos; "
             "el estado no avanza hasta que vuelva la base.")
    elif accion == "recuperar-postgres":
        titulo("Recuperar PostgreSQL")
        iniciar(entorno, "postgres")
        nota("El procesador retoma donde quedó y aplica los eventos en orden.")
    elif accion == "redis":
        titulo("Redis caído")
        detener(entorno, "redis")
        nota("Esperado: 202 igual. Redis es solo una optimización; un duplicado ya no se corta en la API (202) "
             "pero el inbox de PostgreSQL lo descarta: un solo efecto.")
    elif accion == "recuperar-redis":
        titulo("Recuperar Redis")
        iniciar(entorno, "redis")
    elif accion == "procesador":
        titulo("Procesador detenido")
        detener(entorno, "procesador")
        nota("Esperado: la API sigue aceptando (202) y los eventos se acumulan en Kafka (lag). Nada se pierde.")
    elif accion == "recuperar-procesador":
        titulo("Recuperar procesador")
        iniciar(entorno, "procesador")
        nota("Retoma desde el último offset confirmado y drena lo acumulado.")
    elif accion == "sms":
        titulo("Proveedor de SMS caído")
        recrear_notificador(entorno, sms_caido=True)
        mal("■ SMS simulado como caído (escalera de reintentos acortada a 5 s / 10 s / 15 s)")
        nota("Esperado: los avisos no se pierden ni frenan a las demás guías: reintentos y luego correo. "
             "Envía eventos de prueba y mira el canal en el resultado.")
    elif accion == "recuperar-sms":
        titulo("Recuperar proveedor de SMS")
        recrear_notificador(entorno, sms_caido=False)
        ok("▶ SMS normal (escalera 1 min / 10 min / 1 h)")
    elif accion == "recuperar-todo":
        titulo("Recuperar todo")
        estados = estados_contenedores()
        for servicio in entorno["componentes"]:
            if estados.get(contenedor(entorno, servicio)) not in (None, "running"):
                iniciar(entorno, servicio)
        recrear_notificador(entorno, sms_caido=False)
        ok("Todo arriba y el SMS en modo normal")
    else:
        raise RuntimeError(f"Acción desconocida: {accion}")
    info(f"Salud de la API: {salud_api(entorno)}")


def accion_eventos(clave: str, cantidad: int) -> None:
    entorno = ENTORNOS[clave]
    titulo(f"Enviar {cantidad} eventos de prueba ({entorno['nombre']})")
    info(f"Salud de la API: {salud_api(entorno)}")
    tms, portal = token("tms"), token("portal-consulta")
    prefijo = "PRB" + datetime.now().strftime("%d%H%M%S")
    guias, codigos = [], {}
    ultimo_cuerpo = None
    for i in range(cantidad):
        guia = f"{prefijo}{i:03d}1"  # nunca termina en 0: en el directorio simulado esas guías no tienen teléfono
        cuerpo = json.dumps({"idEvento": str(uuid.uuid4()), "numeroGuia": guia, "estado": "EN_REPARTO",
                             "ocurridoEn": datetime.now(ZONA_COLOMBIA).isoformat(timespec="seconds"), "origen": "TMS"}).encode()
        estado, _, ms = pedir("POST", entorno["api"] + "/api/v1/eventos-guia", cuerpo,
                              {"Content-Type": "application/json", "Authorization": f"Bearer {tms}"}, timeout=30)
        codigos[estado] = codigos.get(estado, 0) + 1
        (ok if estado == 202 else mal)(f"{guia} → {estado or 'sin respuesta'} en {ms:.0f} ms")
        if estado == 202:
            guias.append(guia)
            ultimo_cuerpo = cuerpo
    if ultimo_cuerpo:
        estado, _, ms = pedir("POST", entorno["api"] + "/api/v1/eventos-guia", ultimo_cuerpo,
                              {"Content-Type": "application/json", "Authorization": f"Bearer {tms}"}, timeout=30)
        info(f"Reenvío del último evento (mismo idEvento) → {estado} en {ms:.0f} ms "
             f"({'duplicado detectado en la API' if estado == 200 else 'la API lo aceptó; el inbox evitará el doble efecto'})")

    try:
        pendientes = sql(clave, "SELECT count(*) FROM contingencia_eventos")
        info(f"Eventos esperando en la contingencia de PostgreSQL: {pendientes}")
    except RuntimeError as e:
        mal(f"Contingencia no consultable: {e}")

    if not guias:
        mal("Ningún evento fue aceptado")
        registro.resultado = {"tipo": "eventos", "aceptados": 0, "codigos": codigos}
        return
    info(f"Esperando hasta {entorno['espera_visible']} s a que el estado sea visible por la consulta…")
    visibles, inicio = set(), time.time()
    while time.time() - inicio < entorno["espera_visible"] and len(visibles) < len(guias):
        for g in guias:
            if g not in visibles and pedir("GET", f"{entorno['api']}/api/v1/guias/{g}", None,
                                           {"Authorization": f"Bearer {portal}"}, timeout=10)[0] == 200:
                visibles.add(g)
        if len(visibles) < len(guias):
            time.sleep(2)
    segundos = time.time() - inicio
    (ok if len(visibles) == len(guias) else mal)(
        f"Estado visible: {len(visibles)} de {len(guias)} en {segundos:.1f} s"
        + ("" if len(visibles) == len(guias) else " (si Kafka o PostgreSQL están caídos es lo esperado: aparecerán al recuperarlos)"))

    # EN_REPARTO siempre se notifica: se espera un aviso por guía (por SMS o, si el SMS falla, por correo).
    lista = ",".join(f"'{g}'" for g in visibles)
    if lista:
        info("Esperando los avisos al cliente (hasta 60 s)…")
        inicio, canales = time.time(), ""
        try:
            while time.time() - inicio < 60:
                canales = sql(clave, f"SELECT canal || '=' || count(*) FROM notificaciones_enviadas "
                                     f"WHERE numero_guia IN ({lista}) GROUP BY canal ORDER BY canal")
                if sum(int(c.split("=")[1]) for c in canales.splitlines() if "=" in c) >= len(visibles):
                    break
                time.sleep(5)
            (ok if canales else mal)(f"Avisos enviados tras {time.time() - inicio:.0f} s: "
                                     + (canales.replace("\n", ", ") or "ninguno todavía"))
            if "CORREO" in canales:
                nota("CORREO es el canal alterno: el SMS falló, se agotó la escalera de reintentos y el aviso no se perdió.")
        except RuntimeError as e:
            mal(f"Notificaciones no consultables: {e}")
    registro.resultado = {"tipo": "eventos", "aceptados": len(guias), "visibles": len(visibles), "codigos": codigos}


def accion_carga(clave: str, tasa: int, duracion: int, matar_procesador: bool) -> None:
    entorno = ENTORNOS[clave]
    corrida = "PANEL" + datetime.now().strftime("%m%d%H%M%S")
    titulo(f"Prueba de carga {corrida}: {tasa} ev/s × {duracion} s ≈ {miles(tasa * duracion)} eventos ({entorno['nombre']})")
    if matar_procesador:
        nota("A mitad de la carga se hará 'kill' del procesador y se reiniciará 15 s después.")

    def matar_y_revivir() -> None:
        time.sleep(duracion / 2)
        docker("kill", contenedor(entorno, "procesador"))
        mal("✖ kill del procesador en plena carga")
        time.sleep(15)
        docker("start", contenedor(entorno, "procesador"))
        ok("▶ procesador reiniciado: retoma desde el último offset confirmado")

    if matar_procesador:
        threading.Thread(target=matar_y_revivir, daemon=True).start()

    comando = ["docker", "run", "--rm", "--network", entorno["red"], "-v", f"{RAIZ / 'pruebas-carga'}:/pruebas",
               "-e", f"CORRIDA={corrida}", "-e", f"ETAPAS={tasa}:{duracion}s", IMAGEN_K6, "run", "--quiet", "/pruebas/k6/ingesta.js"]
    info("k6 enviando eventos a la API…")
    proceso = subprocess.Popen(comando, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    for linea in proceso.stdout:
        linea = linea.rstrip()
        if re.search(r"(✓|✗|http_req_duration|http_req_failed|eventos_|checks|aceptados=|no_aceptados=|ERRO|WARN)", linea):
            nota(linea.strip())
    proceso.wait()

    archivo = RESULTADOS / f"{corrida}.txt"
    if not archivo.exists():
        raise RuntimeError("k6 no produjo resultados (¿la API o Keycloak están caídos?)")
    aceptados = int(re.search(r"aceptados=(\d+)", archivo.read_text()).group(1))
    no_aceptados = int(re.search(r"no_aceptados=(\d+)", archivo.read_text()).group(1))
    metricas = json.loads((RESULTADOS / f"{corrida}.json").read_text(encoding="utf-8"))["metrics"]
    latencia = metricas.get("http_req_duration{name:ingesta}", {}).get("values", {})
    info(f"k6 terminó: {miles(aceptados)} aceptados (202), {miles(no_aceptados)} no aceptados · mediana {latencia.get('med', 0):.0f} ms · "
         f"p95 {latencia.get('p(95)', 0):.0f} ms · p99 {latencia.get('p(99)', 0):.0f} ms")

    titulo("Reconciliación: ¿todo lo aceptado llegó a PostgreSQL?")
    espera_maxima = 900
    inicio, anterior, sin_cambio_desde = time.time(), -1, time.time()
    while True:
        en_base = int(sql(clave, f"SELECT count(*) FROM guias WHERE numero_guia LIKE '{corrida}%'") or 0)
        lag = lag_procesador_local() if clave == "local" else None
        transcurrido = time.time() - inicio
        info(f"+{transcurrido:.0f} s  en PostgreSQL: {miles(en_base)} de {miles(aceptados)}"
             + (f"  · lag del procesador: {miles(lag)}" if lag is not None else ""))
        if en_base != anterior:
            anterior, sin_cambio_desde = en_base, time.time()
        if en_base >= aceptados and (lag in (0, None)):
            break
        if time.time() - sin_cambio_desde > 120 or transcurrido > espera_maxima:
            break
        time.sleep(5 if clave == "local" else 10)

    diferencia = aceptados - en_base
    if diferencia == 0:
        ok(f"✓ Reconciliación exacta: {miles(aceptados)} aceptados = {miles(en_base)} en PostgreSQL. Cero eventos perdidos.")
    else:
        mal(f"✗ Diferencia de {miles(diferencia)}: revisar DLQ, contingencia y logs (¿algún componente sigue caído?)")
    registro.resultado = {"tipo": "carga", "corrida": corrida, "aceptados": aceptados, "no_aceptados": no_aceptados,
                          "en_base": en_base, "diferencia": diferencia, "p95": round(latencia.get("p(95)", 0)),
                          "p99": round(latencia.get("p(99)", 0)), "kill": matar_procesador}


def lanzar(nombre: str, funcion, *args) -> bool:
    if not candado_tarea.acquire(blocking=False):
        return False

    def correr() -> None:
        registro.tarea = nombre
        if nombre in ("eventos", "carga"):  # las fallas no borran el último resultado
            registro.resultado = None
        try:
            funcion(*args)
        except Exception as e:  # el panel nunca debe caerse por una prueba fallida
            mal(f"Error: {e}")
        finally:
            registro.tarea = None
            info("— listo —")
            candado_tarea.release()

    threading.Thread(target=correr, daemon=True).start()
    return True

# ---------------------------------------------------------------- servidor HTTP

class Manejador(BaseHTTPRequestHandler):
    def log_message(self, *_):  # silencio: el registro útil está en la página
        pass

    def _json(self, datos, estado: int = 200) -> None:
        cuerpo = json.dumps(datos).encode()
        self.send_response(estado)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(cuerpo)))
        self.end_headers()
        self.wfile.write(cuerpo)

    def do_GET(self) -> None:
        url = urllib.parse.urlparse(self.path)
        if url.path == "/":
            cuerpo = PAGINA.read_bytes()
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(cuerpo)))
            self.end_headers()
            self.wfile.write(cuerpo)
        elif url.path == "/api/estado":
            clave = entorno_activo()
            estados = estados_contenedores()
            datos = {"entorno": clave, "tarea": registro.tarea, "resultado": registro.resultado,
                     "entornos": {k: {"nombre": v["nombre"], "carga_sugerida": v["carga_sugerida"]} for k, v in ENTORNOS.items()}}
            if clave:
                entorno = ENTORNOS[clave]
                datos["componentes"] = {s: estados.get(contenedor(entorno, s), "no existe") for s in entorno["componentes"]}
                datos["salud"] = salud_api(entorno)
            self._json(datos)
        elif url.path == "/api/registro":
            desde = int(urllib.parse.parse_qs(url.query).get("desde", ["0"])[0])
            self._json({"lineas": registro.desde(desde), "total": registro.total(), "tarea": registro.tarea})
        else:
            self._json({"error": "no encontrado"}, 404)

    def do_POST(self) -> None:
        # Solo peticiones de la propia página: bloquea que otro sitio abierto en el navegador dispare acciones
        # (Origin) y los ataques de DNS rebinding (Host).
        permitidos = (f"127.0.0.1:{PUERTO}", f"localhost:{PUERTO}")
        if self.headers.get("Host", "") not in permitidos:
            return self._json({"error": "host no permitido"}, 403)
        origen = self.headers.get("Origin", "")
        if origen and origen not in (f"http://127.0.0.1:{PUERTO}", f"http://localhost:{PUERTO}"):
            return self._json({"error": "origen no permitido"}, 403)
        largo = int(self.headers.get("Content-Length", "0") or 0)
        datos = json.loads(self.rfile.read(largo) or b"{}") if largo else {}
        clave = entorno_activo()
        if not clave:
            return self._json({"error": "No hay ningún entorno levantado (local o Aiven)."}, 409)
        partes = urllib.parse.urlparse(self.path).path.strip("/").split("/")
        if partes[:2] == ["api", "falla"] and len(partes) == 3:
            lanzado = lanzar(f"falla:{partes[2]}", accion_fallas, clave, partes[2])
        elif partes == ["api", "eventos"]:
            lanzado = lanzar("eventos", accion_eventos, clave, max(1, min(int(datos.get("cantidad", 10)), 200)))
        elif partes == ["api", "carga"]:
            tasa = max(1, min(int(datos.get("tasa", 20)), 2000))
            duracion = max(5, min(int(datos.get("duracion", 30)), 600))
            lanzado = lanzar("carga", accion_carga, clave, tasa, duracion, bool(datos.get("matar_procesador")))
        else:
            return self._json({"error": "acción desconocida"}, 404)
        self._json({"lanzado": lanzado}, 202 if lanzado else 409)


if __name__ == "__main__":
    print(f"Panel de pruebas en http://127.0.0.1:{PUERTO}  (Ctrl+C para detener)")
    ThreadingHTTPServer(("127.0.0.1", PUERTO), Manejador).serve_forever()
