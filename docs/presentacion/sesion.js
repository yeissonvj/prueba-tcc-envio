/* Sesión de la presentación (sitio estático).
 *
 * No es seguridad real: el sitio y el repositorio son públicos y cualquier control en JavaScript se puede saltar.
 * Ordena la experiencia (evaluador / administrador) y evita dejar contraseñas legibles: acceso.js solo guarda
 * hashes PBKDF2-SHA256 con sal, generados por herramientas/credenciales-presentacion.py.
 *
 * Uso en cada página, dentro de <head>:
 *   <script src="acceso.js"></script><script src="sesion.js"></script>
 *   <script>TCC.exigir()</script>                      // cualquier usuario con sesión
 *   <script>TCC.exigir({ soloAdmin: true })</script>   // páginas técnicas
 */
(function () {
  'use strict';

  const CLAVE = 'tcc-presentacion-sesion';
  const DURACION_MS = 8 * 60 * 60 * 1000; // 8 horas
  // Enlaces que solo puede abrir el administrador (versiones técnicas y guía detallada).
  const PAGINAS_ADMIN = ['eje1-arquitectura.html', 'eje2-tecnica.html', 'eje3-guia.html'];
  const ROLES = { admin: 'Administrador', evaluador: 'Evaluador' };

  function leer() {
    try {
      const s = JSON.parse(localStorage.getItem(CLAVE) || 'null');
      if (s && s.expira > Date.now() && ROLES[s.rol]) return s;
    } catch { /* almacenamiento bloqueado o dañado: sin sesión */ }
    return null;
  }

  function guardar(usuario, rol) {
    try { localStorage.setItem(CLAVE, JSON.stringify({ usuario, rol, expira: Date.now() + DURACION_MS })); } catch { }
  }

  function cerrar() {
    try { localStorage.removeItem(CLAVE); } catch { }
    location.replace('login.html');
  }

  const hex = (bytes) => Array.from(new Uint8Array(bytes), (b) => b.toString(16).padStart(2, '0')).join('');
  const desdeHex = (h) => new Uint8Array(h.match(/../g).map((x) => parseInt(x, 16)));

  async function derivar(clave, salHex, iteraciones) {
    const llave = await crypto.subtle.importKey('raw', new TextEncoder().encode(clave), 'PBKDF2', false, ['deriveBits']);
    const bits = await crypto.subtle.deriveBits({ name: 'PBKDF2', hash: 'SHA-256', salt: desdeHex(salHex), iterations: iteraciones }, llave, 256);
    return hex(bits);
  }

  // Comparación de longitud fija: no corta en el primer carácter distinto.
  function iguales(a, b) {
    if (a.length !== b.length) return false;
    let r = 0;
    for (let i = 0; i < a.length; i++) r |= a.charCodeAt(i) ^ b.charCodeAt(i);
    return r === 0;
  }

  async function verificar(usuario, clave) {
    const acceso = window.TCC_ACCESO || { cuentas: [] };
    const cuenta = acceso.cuentas.find((c) => c.usuario === usuario.trim().toLowerCase());
    // Se deriva igual aunque el usuario no exista: misma demora en ambos casos.
    const hash = await derivar(clave, cuenta ? cuenta.sal : '00'.repeat(16), acceso.iteraciones || 210000);
    if (!cuenta || !iguales(hash, cuenta.hash)) return null;
    guardar(cuenta.usuario, cuenta.rol);
    return { usuario: cuenta.usuario, rol: cuenta.rol };
  }

  function configurado() {
    return !!(window.TCC_ACCESO && window.TCC_ACCESO.cuentas && window.TCC_ACCESO.cuentas.length);
  }

  // Se llama en <head>: redirige antes de pintar la página si no corresponde.
  function exigir(opciones) {
    const soloAdmin = !!(opciones && opciones.soloAdmin);
    const sesion = leer();
    const aqui = location.pathname.split('/').pop() || 'index.html';
    if (!sesion) {
      location.replace('login.html?volver=' + encodeURIComponent(aqui + location.hash));
      return;
    }
    if (soloAdmin && sesion.rol !== 'admin') {
      location.replace('index.html?aviso=solo-admin');
      return;
    }
    document.addEventListener('DOMContentLoaded', () => aplicar(sesion));
  }

  function aplicar(sesion) {
    const estilo = document.createElement('style');
    estilo.textContent = `
      .tcc-bloqueado { opacity: .45 !important; filter: grayscale(1); cursor: not-allowed !important; }
      .tcc-chip { display: inline-flex; align-items: center; gap: 8px; font: 12.5px system-ui, "Segoe UI", sans-serif;
        color: #dbe4ee; background: rgba(23,33,44,.9); border: 1px solid #2c3d50; border-radius: 999px; padding: 4px 6px 4px 10px; white-space: nowrap; }
      .tcc-chip .rol { font-size: 11px; padding: 1px 7px; border-radius: 999px; background: #10243a; color: #4ea1ff; }
      .tcc-chip .rol.admin { background: #2b2710; color: #ffd166; }
      .tcc-chip button { font: inherit; font-size: 12px; color: #8a9bb0; background: transparent; border: 1px solid #2c3d50;
        border-radius: 999px; padding: 2px 9px; cursor: pointer; }
      .tcc-chip button:hover { color: #ff9b9b; border-color: #6b2a2a; }
      .tcc-chip.flotante { position: fixed; top: 14px; right: 16px; z-index: 50; box-shadow: 0 8px 24px rgba(0,0,0,.35); }
      .tcc-chip.flotante.abajo { top: auto; right: auto; bottom: 14px; left: 16px; opacity: .85; }
      .tcc-chip.flotante.abajo:hover { opacity: 1; }
      @media print { .tcc-chip { display: none; } }
      .tcc-aviso { position: fixed; left: 50%; top: 18px; transform: translateX(-50%); z-index: 60; background: #2b2710; color: #ffd166;
        border: 1px solid #6b541f; border-radius: 10px; padding: 10px 16px; font: 14px system-ui, sans-serif; box-shadow: 0 10px 30px rgba(0,0,0,.4);
        animation: tcc-aviso 5s ease forwards; }
      @keyframes tcc-aviso { 0% { opacity: 0; transform: translate(-50%, -10px); } 8%, 85% { opacity: 1; transform: translate(-50%, 0); } 100% { opacity: 0; } }`;
    document.head.appendChild(estilo);

    // Chip flotante con el usuario y el botón de salir: arriba a la derecha, o abajo si la página tiene menú
    // (dentro del menú quedaría oculto: los menús se desplazan horizontalmente).
    const chip = document.createElement('span');
    chip.className = 'tcc-chip flotante' + (document.querySelector('nav') ? ' abajo' : '');
    chip.innerHTML = `<span></span><span class="rol ${sesion.rol}">${ROLES[sesion.rol]}</span><button type="button">Salir</button>`;
    chip.firstChild.textContent = sesion.usuario;
    chip.querySelector('button').addEventListener('click', cerrar);
    document.body.appendChild(chip);

    // Botones técnicos: habilitados solo para el administrador.
    if (sesion.rol !== 'admin') {
      document.querySelectorAll('a[href]').forEach((a) => {
        const destino = a.getAttribute('href').split('#')[0];
        if (!PAGINAS_ADMIN.includes(destino)) return;
        a.classList.add('tcc-bloqueado');
        a.setAttribute('aria-disabled', 'true');
        a.title = 'Disponible solo para el administrador';
        a.removeAttribute('href');
        if (!a.textContent.includes('🔒')) a.prepend('🔒 ');
      });
    }

    if (new URLSearchParams(location.search).get('aviso') === 'solo-admin') {
      const aviso = document.createElement('div');
      aviso.className = 'tcc-aviso';
      aviso.textContent = '🔒 Esa sección está disponible solo para el administrador.';
      document.body.appendChild(aviso);
    }
  }

  window.TCC = { exigir, verificar, cerrar, leer, configurado };
})();
