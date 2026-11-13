"""Outbox, Inbox y copia local de fuentes, todos sobre la MISMA conexion/transaccion que el agregado.

SOLID:
  - ISP: tres clases pequenas, cada una implementa un puerto de pocos metodos.
  - SRP: cada tabla tiene su clase.
"""

import json
import sqlite3

from c2_atributos.aplicacion.puertos import (
    MensajeDeIntegracion,
    PuertoFuentesConocidas,
    PuertoInbox,
    PuertoOutbox,
)
from c2_atributos.building_blocks.reloj import Reloj


class OutboxSqlite(PuertoOutbox):
    def __init__(self, conexion: sqlite3.Connection, reloj: Reloj):
        self._c = conexion
        self._reloj = reloj

    def agregar(self, mensaje: MensajeDeIntegracion) -> None:
        self._c.execute(
            "INSERT INTO outbox (message_id, tipo, payload, creado_en) VALUES (?,?,?,?)",
            (mensaje.message_id, mensaje.tipo, json.dumps(mensaje.payload), self._reloj.ahora().isoformat()),
        )

    def pendientes(self) -> list[MensajeDeIntegracion]:
        filas = self._c.execute(
            "SELECT message_id, tipo, payload FROM outbox WHERE enviado_en IS NULL ORDER BY rowid"
        ).fetchall()
        return [MensajeDeIntegracion(f["message_id"], f["tipo"], json.loads(f["payload"])) for f in filas]

    def marcar_enviado(self, message_id: str) -> None:
        self._c.execute(
            "UPDATE outbox SET enviado_en = ? WHERE message_id = ?",
            (self._reloj.ahora().isoformat(), message_id),
        )


class InboxSqlite(PuertoInbox):
    def __init__(self, conexion: sqlite3.Connection, reloj: Reloj):
        self._c = conexion
        self._reloj = reloj

    def ya_procesado(self, message_id: str) -> bool:
        return self._c.execute("SELECT 1 FROM inbox WHERE message_id = ?", (message_id,)).fetchone() is not None

    def registrar(self, message_id: str) -> None:
        self._c.execute(
            "INSERT INTO inbox (message_id, recibido_en) VALUES (?,?)",
            (message_id, self._reloj.ahora().isoformat()),
        )


class FuentesConocidasSqlite(PuertoFuentesConocidas):
    def __init__(self, conexion: sqlite3.Connection):
        self._c = conexion

    def existe(self, fuente_id: str) -> bool:
        return self._c.execute("SELECT 1 FROM fuentes_locales WHERE fuente_id = ?", (fuente_id,)).fetchone() is not None

    def guardar(self, fuente_id: str, nombre: str) -> None:
        self._c.execute(
            "INSERT INTO fuentes_locales (fuente_id, nombre) VALUES (?,?)"
            " ON CONFLICT(fuente_id) DO UPDATE SET nombre = excluded.nombre",
            (fuente_id, nombre),
        )