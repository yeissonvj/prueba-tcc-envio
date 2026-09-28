"""Genera docs/presentacion/acceso.js con las cuentas del login de la presentación.

Pide usuario y contraseña del administrador y del evaluador en la terminal (la contraseña no se muestra) y guarda
SOLO un hash PBKDF2-SHA256 con sal aleatoria por cuenta. Las contraseñas no quedan en ningún archivo.

    python herramientas/credenciales-presentacion.py

Después: commit y push de docs/presentacion/acceso.js. Para cambiar una contraseña, volver a ejecutarlo.
"""
from __future__ import annotations

import argparse
import getpass
import hashlib
import json
import os
import secrets
from pathlib import Path

ITERACIONES = 210_000  # recomendación OWASP para PBKDF2-HMAC-SHA256
LARGO_MINIMO = 10
DESTINO = Path(__file__).resolve().parents[1] / "docs" / "presentacion" / "acceso.js"


def pedir_cuenta(rol: str, usuario_por_defecto: str) -> dict:
    etiqueta = "administrador" if rol == "admin" else "evaluador"
    usuario = (input(f"Usuario del {etiqueta} [{usuario_por_defecto}]: ").strip() or usuario_por_defecto).lower()
    variable = f"TCC_CLAVE_{rol.upper()}"  # solo para automatizar pruebas; lo normal es escribirla
    clave = os.environ.get(variable)
    while not clave:
        primera = getpass.getpass(f"Contraseña del {etiqueta} (mín. {LARGO_MINIMO} caracteres): ")
        if len(primera) < LARGO_MINIMO:
            print("  Muy corta.")
            continue
        if getpass.getpass("Repítela: ") != primera:
            print("  No coinciden.")
            continue
        clave = primera
    sal = secrets.token_bytes(16)
    hash_ = hashlib.pbkdf2_hmac("sha256", clave.encode("utf-8"), sal, ITERACIONES, dklen=32)
    return {"usuario": usuario, "rol": rol, "sal": sal.hex(), "hash": hash_.hex()}


def main() -> None:
    argumentos = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    argumentos.add_argument("--salida", type=Path, default=DESTINO, help="archivo a escribir (por defecto, el de la presentación)")
    salida = argumentos.parse_args().salida

    cuentas = [pedir_cuenta("admin", "admin"), pedir_cuenta("evaluador", "evaluador")]
    if cuentas[0]["usuario"] == cuentas[1]["usuario"]:
        raise SystemExit("Los dos usuarios deben ser distintos.")
    contenido = (
        "// Generado por herramientas/credenciales-presentacion.py. No editar a mano.\n"
        "// Solo contiene hashes PBKDF2-SHA256 con sal: las contraseñas no están en este archivo.\n"
        f"window.TCC_ACCESO = {json.dumps({'iteraciones': ITERACIONES, 'cuentas': cuentas}, indent=2)};\n"
    )
    salida.write_text(contenido, encoding="utf-8")
    print(f"Listo: {salida} ({', '.join(c['usuario'] + ' = ' + c['rol'] for c in cuentas)})")


if __name__ == "__main__":
    main()
