// Prueba de carga de la API de ingesta con tasa de llegada constante (eventos/s, no usuarios).
// Cada evento crea una guía nueva con el prefijo de la corrida: así la reconciliación puede contar
// exactamente cuántas guías llegaron a PostgreSQL y compararlo con los 202 que recibió k6.
//
//   docker run --rm --network tcc-eventos_default -v "$PWD/pruebas-carga:/pruebas" \
//     -e CORRIDA=C1 -e ETAPAS=200:45s,500:45s,1000:45s grafana/k6:2.3.0 run /pruebas/k6/ingesta.js
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';
import { uuidv4 } from './utilidades.js';

const API = __ENV.API || 'http://api:8080';
const TOKEN_URL = __ENV.TOKEN_URL || 'http://keycloak:8081/realms/tcc/protocol/openid-connect/token';
const CORRIDA = __ENV.CORRIDA || `C${Date.now()}`;

// "200:45s,500:45s,1000:45s" → escalones de tasa constante
const ETAPAS = (__ENV.ETAPAS || '200:30s').split(',').map((e) => {
  const [tasa, duracion] = e.split(':');
  return { tasa: Number(tasa), duracion };
});

const aceptados = new Counter('eventos_aceptados');   // 202: durables, deben aparecer en la base
const rechazados = new Counter('eventos_no_aceptados');

export const options = {
  scenarios: Object.fromEntries(ETAPAS.map((etapa, i) => [`etapa_${etapa.tasa}_por_s`, {
    executor: 'constant-arrival-rate',
    rate: etapa.tasa,
    timeUnit: '1s',
    duration: etapa.duracion,
    preAllocatedVUs: Math.max(50, etapa.tasa / 4),
    maxVUs: Math.max(50, etapa.tasa * 2), // nunca menor que preAllocatedVUs (tasas bajas)
    startTime: ETAPAS.slice(0, i).reduce((acc, e) => acc + segundos(e.duracion), 0) + 's',
    tags: { etapa: String(etapa.tasa) },
  }])),
  // Umbrales = SLO de la Fase 0. Si no se cumplen, k6 termina con error.
  thresholds: {
    'http_req_duration{name:ingesta}': ['p(95)<100', 'p(99)<200'],
    'http_req_failed{name:ingesta}': ['rate<0.001'],
    checks: ['rate>0.999'],
    // Por etapa: además de evaluar el SLO, hace que el resumen traiga los percentiles de cada tasa.
    ...Object.fromEntries(ETAPAS.map((e) => [`http_req_duration{etapa:${e.tasa}}`, ['p(99)<200']])),
    ...Object.fromEntries(ETAPAS.map((e) => [`http_req_failed{etapa:${e.tasa}}`, ['rate<0.001']])),
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function segundos(duracion) {
  const n = parseInt(duracion, 10);
  return duracion.endsWith('m') ? n * 60 : n;
}

function token(cliente, secreto) {
  const r = http.post(TOKEN_URL, { grant_type: 'client_credentials', client_id: cliente, client_secret: secreto });
  if (r.status !== 200) throw new Error(`No se obtuvo token para ${cliente}: ${r.status}`);
  return r.json('access_token');
}

// Dos sistemas emisores, cada uno con su token y su origen (el API rechaza que uno se haga pasar por otro).
export function setup() {
  return {
    emisores: [
      { origen: 'TMS', token: token('tms', __ENV.SECRETO_TMS || 'tms-secreto-solo-local') },
      { origen: 'TRANSPORTE', token: token('transporte', __ENV.SECRETO_TRANSPORTE || 'transporte-secreto-solo-local') },
    ],
  };
}

export default function (datos) {
  const emisor = datos.emisores[(__VU + __ITER) % 2];
  const guia = `${CORRIDA}${String(__VU).padStart(4, '0')}${String(__ITER).padStart(6, '0')}`;
  const cuerpo = JSON.stringify({
    idEvento: uuidv4(),
    numeroGuia: guia,
    estado: 'CREADA',
    ocurridoEn: new Date(Date.now() - 60_000).toISOString(),
    origen: emisor.origen,
  });

  const r = http.post(`${API}/api/v1/eventos-guia`, cuerpo, {
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${emisor.token}` },
    tags: { name: 'ingesta' },
  });

  const ok = check(r, { 'aceptado (202)': (x) => x.status === 202 });
  (ok ? aceptados : rechazados).add(1);
}

// Resumen legible + un archivo simple para la reconciliación.
export function handleSummary(datos) {
  delete datos.setup_data; // contiene los tokens de acceso: nunca deben quedar en un archivo
  const valor = (m) => (datos.metrics[m] ? datos.metrics[m].values.count : 0);
  const resultado = `corrida=${CORRIDA}\naceptados=${valor('eventos_aceptados')}\nno_aceptados=${valor('eventos_no_aceptados')}\n`;
  return {
    [`/pruebas/resultados/${CORRIDA}.txt`]: resultado,
    [`/pruebas/resultados/${CORRIDA}.json`]: JSON.stringify(datos, null, 2),
    stdout: resultado,
  };
}
