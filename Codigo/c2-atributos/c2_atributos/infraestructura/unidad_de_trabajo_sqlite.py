"""Unidad de trabajo SQLite (patron Unit of Work): una conexion = una transaccion.

Todo lo que ocurre dentro del `with` (agregado + outbox + inbox) se confirma junto o se revierte junto.

SOLID - LSP: puede sustituir a cualquier `UnidadDeTrabajo`.
"""

import sqlite3

from c2_atributos.aplicacion.puertos import Cifrador, LecturaAtributos, UnidadDeTrabajo
from c2_atributos.building_blocks.reloj import Reloj
from c2_atributos.dominio.atributo import AtributoMemento
from c2_atributos.infraestructura.mensajeria_sqlite import FuentesConocidasSqlite, InboxSqlite, OutboxSqlite
from c2_atributos.infraestructura.repositorio_sqlite import RepositorioAtributosSqlite


def _abrir(ruta_bd: str) -> sqlite3.Connection:
    conexion = sqlite3.connect(ruta_bd, timeout=10)
    conexion.row_factory = sqlite3.Row
    return conexion


class UnidadDeTrabajoSqlite(UnidadDeTrabajo):
    def __init__(self, ruta_bd: str, cifrador: Cifrador, reloj: Reloj):
        self._ruta_bd = ruta_bd
        self._cifrador = cifrador
        self._reloj = reloj
        self._conexion: sqlite3.Connection | None = None

    def __enter__(self) -> "UnidadDeTrabajoSqlite":
        self._conexion = _abrir(self._ruta_bd)
        self.atributos = RepositorioAtributosSqlite(self._conexion, self._cifrador)
        self.outbox = OutboxSqlite(self._conexion, self._reloj)
        self.inbox = InboxSqlite(self._conexion, self._reloj)
        self.fuentes = FuentesConocidasSqlite(self._conexion)
        return self

    def confirmar(self) -> None:
        self._conexion.commit()

    def __exit__(self, tipo, valor, traza) -> bool:
        if tipo is not None:
            self._conexion.rollback()
        self._conexion.close()
        return False  # nunca traga la excepcion


class LecturaAtributosSqlite(LecturaAtributos):
    """Lado de lectura (CQRS): abre una conexion corta por consulta; no necesita transaccion de escritura."""

    def __init__(self, ruta_bd: str, cifrador: Cifrador):
        self._ruta_bd = ruta_bd
        self._cifrador = cifrador

    def _con_repositorio(self, funcion):
        conexion = _abrir(self._ruta_bd)
        try:
            return funcion(RepositorioAtributosSqlite(conexion, self._cifrador))
        finally:
            conexion.close()

    def obtener(self, atributo_id: str) -> AtributoMemento | None:
        return self._con_repositorio(lambda r: r.obtener_memento(atributo_id))

    def listar(self, fuente_id: str | None) -> list[AtributoMemento]:
        return self._con_repositorio(lambda r: r.listar_mementos(fuente_id))

    def versiones(self, atributo_id: str) -> list[AtributoMemento]:
        return self._con_repositorio(lambda r: r.versiones(atributo_id))