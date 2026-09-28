/* Reloj de la presentación: hora actual, tiempo total y tiempo en la sección actual.
 *
 * - El total se inicia y se pausa con ▶/⏸ y se conserva al cambiar de página (localStorage).
 * - El tiempo por sección se reinicia al pasar a otra diapositiva y se acumula en "Tiempos",
 *   para saber cuánto tomó cada explicación en un ensayo.
 * - Amarillo desde 3 min en la misma sección, rojo desde 5 min.
 */
(function () {
  'use strict';

  const CLAVE = 'tcc-presentacion-reloj';
  const AVISO_MS = 3 * 60 * 1000, LIMITE_MS = 5 * 60 * 1000;
  const vacio = () => ({ corriendo: false, acumulado: 0, desde: null, tiempos: {}, orden: 0 });

  function leer() {
    try { return Object.assign(vacio(), JSON.parse(localStorage.getItem(CLAVE) || '{}')); } catch { return vacio(); }
  }
  function guardar(e) { try { localStorage.setItem(CLAVE, JSON.stringify(e)); } catch { } }

  let estado = leer();
  let seccion = null;           // { clave, titulo }
  let seccionDesde = null;      // momento en que empezó a contar la sección actual (solo con el reloj corriendo)

  const pagina = () => (document.querySelector('nav .marca')?.textContent.trim() || document.title).replace(/\s+/g, ' ');

  function seccionActual() {
    const secciones = [...document.querySelectorAll('main section')];
    if (!secciones.length) return { clave: location.pathname.split('/').pop() || 'index.html', titulo: 'Inicio' };
    const y = scrollY + 90;
    let s = secciones[0];
    for (const x of secciones) if (x.offsetTop <= y) s = x;
    const encabezado = s.querySelector('h1, h2');
    const titulo = (encabezado ? encabezado.textContent : s.id).replace(/^\s*\d+\s*/, '').trim();
    return { clave: (location.pathname.split('/').pop() || 'index.html') + '#' + s.id, titulo };
  }

  // Suma al registro el tiempo de la sección que se deja.
  function cerrarSeccion() {
    if (!seccion || seccionDesde === null) return;
    const ms = Date.now() - seccionDesde;
    if (ms >= 1000) {
      const t = estado.tiempos[seccion.clave] || { titulo: seccion.titulo, pagina: pagina(), ms: 0, orden: ++estado.orden };
      t.ms += ms;
      estado.tiempos[seccion.clave] = t;
    }
    seccionDesde = estado.corriendo ? Date.now() : null;
    guardar(estado);
  }

  const total = () => estado.acumulado + (estado.corriendo && estado.desde ? Date.now() - estado.desde : 0);
  const mmss = (ms) => {
    const s = Math.floor(ms / 1000), h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), r = s % 60;
    return (h ? h + ':' + String(m).padStart(2, '0') : String(m).padStart(2, '0')) + ':' + String(r).padStart(2, '0');
  };

  function alternar() {
    if (estado.corriendo) {
      cerrarSeccion();
      estado.acumulado = total(); estado.corriendo = false; estado.desde = null; seccionDesde = null;
    } else {
      estado.corriendo = true; estado.desde = Date.now(); seccionDesde = Date.now();
    }
    guardar(estado); pintar();
  }

  function reiniciar() {
    if (!confirm('¿Reiniciar el reloj y borrar los tiempos por sección?')) return;
    estado = vacio(); seccionDesde = null; guardar(estado); pintar(); pintarLista();
  }

  // ---------------- interfaz ----------------
  const estilo = document.createElement('style');
  estilo.textContent = `
    .tcc-reloj { position: fixed; left: 50%; transform: translateX(-50%); z-index: 40; display: flex; align-items: center; gap: 10px;
      font: 13px system-ui, "Segoe UI", sans-serif; color: #dbe4ee; background: rgba(13,19,27,.92); border: 1px solid #2c3d50;
      border-radius: 999px; padding: 5px 6px 5px 14px; box-shadow: 0 10px 28px rgba(0,0,0,.45); backdrop-filter: blur(8px); user-select: none; }
    .tcc-reloj .d { font-family: ui-monospace, "Cascadia Code", Consolas, monospace; font-variant-numeric: tabular-nums; }
    .tcc-reloj .hora { font-size: 15px; font-weight: 700; letter-spacing: .02em; }
    .tcc-reloj .sep { width: 1px; height: 18px; background: #2c3d50; }
    .tcc-reloj .et { color: #8a9bb0; font-size: 11px; margin-right: 4px; }
    .tcc-reloj .sec { padding: 1px 8px; border-radius: 999px; transition: background .3s, color .3s; }
    .tcc-reloj .sec.aviso { background: #2b2710; color: #ffd166; }
    .tcc-reloj .sec.limite { background: #341616; color: #ff6b6b; animation: tcc-latido 1.2s ease-in-out infinite; }
    @keyframes tcc-latido { 50% { opacity: .55; } }
    .tcc-reloj .titulo { max-width: 180px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: #8a9bb0; font-size: 12px; }
    .tcc-reloj button { font: inherit; font-size: 12.5px; color: #dbe4ee; background: #17212c; border: 1px solid #2c3d50; border-radius: 999px;
      padding: 3px 10px; cursor: pointer; }
    .tcc-reloj button:hover { border-color: #4ea1ff; color: #fff; }
    .tcc-reloj button.play { color: #3ddc97; border-color: #1f6b4f; } .tcc-reloj button.pausa { color: #ffd166; border-color: #6b541f; }
    .tcc-reloj.detenido .tot { color: #8a9bb0; }
    .tcc-tiempos { position: fixed; left: 50%; transform: translateX(-50%); z-index: 41; width: min(460px, calc(100vw - 32px)); max-height: 60vh; overflow: auto;
      background: rgba(13,19,27,.97); border: 1px solid #2c3d50; border-radius: 14px; padding: 12px 14px; box-shadow: 0 18px 50px rgba(0,0,0,.55);
      font: 13px system-ui, "Segoe UI", sans-serif; color: #dbe4ee; display: none; }
    .tcc-tiempos.abierto { display: block; }
    .tcc-tiempos h4 { margin: 0 0 8px; font-size: 13px; color: #8a9bb0; font-weight: 600; display: flex; justify-content: space-between; }
    .tcc-tiempos .fila { display: grid; grid-template-columns: 1fr auto; gap: 10px; padding: 5px 0; border-bottom: 1px solid #1d2a38; }
    .tcc-tiempos .fila small { display: block; color: #6b7b8f; font-size: 11px; }
    .tcc-tiempos .fila .d { font-family: ui-monospace, Consolas, monospace; }
    .tcc-tiempos .fila .d.aviso { color: #ffd166; } .tcc-tiempos .fila .d.limite { color: #ff6b6b; }
    .tcc-tiempos .vacio { color: #6b7b8f; }
    @media (max-width: 760px) { .tcc-reloj .titulo, .tcc-reloj .et { display: none; } }
    @media print { .tcc-reloj, .tcc-tiempos { display: none !important; } }`;
  document.head.appendChild(estilo);

  const reloj = document.createElement('div');
  reloj.className = 'tcc-reloj';
  reloj.innerHTML = `
    <span class="d hora" title="Hora actual">--:--:--</span>
    <span class="sep"></span>
    <span title="Tiempo total de la presentación"><span class="et">Total</span><span class="d tot">00:00</span></span>
    <span title="Tiempo en esta sección"><span class="et">Sección</span><span class="d sec">00:00</span></span>
    <span class="titulo"></span>
    <button type="button" class="alternar play" title="Iniciar / pausar">▶&#xFE0E;</button>
    <button type="button" class="ver" title="Tiempo por sección">Tiempos</button>
    <button type="button" class="reiniciar" title="Reiniciar">⟲</button>`;
  const lista = document.createElement('div');
  lista.className = 'tcc-tiempos';

  function ubicar() {
    const nav = document.querySelector('nav');
    const arriba = nav ? nav.getBoundingClientRect().bottom + 8 : 14;
    reloj.style.top = arriba + 'px';
    lista.style.top = (arriba + reloj.offsetHeight + 8) + 'px';
  }

  function pintar() {
    const ahora = new Date();
    reloj.querySelector('.hora').textContent = ahora.toLocaleTimeString('es-CO', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
    reloj.querySelector('.tot').textContent = mmss(total());
    const enSeccion = seccionDesde === null ? 0 : Date.now() - seccionDesde;
    const sec = reloj.querySelector('.sec');
    sec.textContent = mmss(enSeccion);
    sec.className = 'd sec' + (enSeccion >= LIMITE_MS ? ' limite' : enSeccion >= AVISO_MS ? ' aviso' : '');
    reloj.querySelector('.titulo').textContent = seccion ? seccion.titulo : '';
    const boton = reloj.querySelector('.alternar');
    boton.textContent = estado.corriendo ? '❚❚' : '▶︎'; // ︎: símbolo de texto, no emoji
    boton.className = 'alternar ' + (estado.corriendo ? 'pausa' : 'play');
    reloj.classList.toggle('detenido', !estado.corriendo);
  }

  function pintarLista() {
    const filas = Object.values(estado.tiempos).sort((a, b) => a.orden - b.orden);
    const suma = filas.reduce((s, t) => s + t.ms, 0);
    const clase = (ms) => ms >= LIMITE_MS ? ' limite' : ms >= AVISO_MS ? ' aviso' : '';
    lista.innerHTML = `<h4><span>Tiempo por sección</span><span class="d">${mmss(suma)}</span></h4>` + (filas.length
      ? filas.map((t) => `<div class="fila"><span>${escapar(t.titulo)}<small>${escapar(t.pagina)}</small></span><span class="d${clase(t.ms)}">${mmss(t.ms)}</span></div>`).join('')
      : '<p class="vacio">Pulsa ▶ para empezar a medir. Cada sección que recorras aparecerá aquí con su tiempo.</p>');
  }
  const escapar = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

  function vigilarSeccion() {
    const nueva = seccionActual();
    if (!seccion || nueva.clave !== seccion.clave) {
      cerrarSeccion();
      seccion = nueva;
      seccionDesde = estado.corriendo ? Date.now() : null;
      if (lista.classList.contains('abierto')) pintarLista();
    }
  }

  function iniciar() {
    document.body.append(reloj, lista);
    reloj.querySelector('.alternar').addEventListener('click', alternar);
    reloj.querySelector('.reiniciar').addEventListener('click', reiniciar);
    reloj.querySelector('.ver').addEventListener('click', () => { cerrarSeccion(); pintarLista(); lista.classList.toggle('abierto'); });
    // Otra pestaña con la presentación puede haber pausado o iniciado el reloj.
    addEventListener('storage', (e) => { if (e.key === CLAVE) { estado = leer(); if (!estado.corriendo) seccionDesde = null; pintar(); } });
    addEventListener('pagehide', cerrarSeccion);
    addEventListener('resize', ubicar);
    ubicar();
    vigilarSeccion();
    if (estado.corriendo) seccionDesde = Date.now();
    pintar();
    setInterval(() => { vigilarSeccion(); pintar(); }, 250);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', iniciar); else iniciar();
})();
