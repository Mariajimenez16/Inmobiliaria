"""Repositorio SQLite del agregado AtributoNegocio (patron Repository).

- Cifra la definicion y la politica de calidad antes de escribir y las descifra al leer.
- Cada escritura guarda tambien una VERSION (un Memento serializado): historial del contrato 4.1.
- Concurrencia optimista: si la version cambio, lanza Conflicto (-> 409).

SOLID:
  - DIP/LSP: implementa el puerto RepositorioAtributos; la aplicacion no sabe que es SQLite.
  - SRP: solo traduce entre el agregado y las filas; no contiene reglas de negocio.
"""

import json
import sqlite3
from datetime import datetime

from c2_atributos.aplicacion.puertos import Cifrador, RepositorioAtributos
from c2_atributos.building_blocks.errores import Conflicto
from c2_atributos.dominio.atributo import AtributoMemento, AtributoNegocio


class RepositorioAtributosSqlite(RepositorioAtributos):
    def __init__(self, conexion: sqlite3.Connection, cifrador: Cifrador):
        self._c = conexion
        self._cifrador = cifrador

    # ------------------------------------------------------------------ puerto
    def obtener(self, atributo_id: str) -> AtributoNegocio | None:
        memento = self.obtener_memento(atributo_id)
        return None if memento is None else AtributoNegocio.desde_memento(memento)

    def existe_nombre_en_fuente(self, nombre: str, fuente_id: str) -> bool:
        fila = self._c.execute(
            "SELECT 1 FROM atributos WHERE nombre = ? AND fuente_id = ?", (nombre, fuente_id)
        ).fetchone()
        return fila is not None

    def agregar(self, atributo: AtributoNegocio) -> None:
        m = atributo.crear_memento()
        try:
            self._c.execute(
                "INSERT INTO atributos (id, nombre, fuente_id, contiene_datos_personales, definicion,"
                " politica_calidad, expresion_regular, version, creado_en, actualizado_en)"
                " VALUES (?,?,?,?,?,?,?,?,?,?)",
                self._valores_fila(m),
            )
        except sqlite3.IntegrityError as exc:
            raise Conflicto("Ya existe un atributo con ese nombre en la misma fuente.") from exc
        self._guardar_version(m)

    def actualizar(self, atributo: AtributoNegocio, version_anterior: int) -> None:
        m = atributo.crear_memento()
        cursor = self._c.execute(
            "UPDATE atributos SET nombre=?, fuente_id=?, contiene_datos_personales=?, definicion=?,"
            " politica_calidad=?, expresion_regular=?, version=?, actualizado_en=?"
            " WHERE id=? AND version=?",
            (
                m.nombre,
                m.fuente_id,
                int(m.contiene_datos_personales),
                self._cifrar(m.definicion),
                self._cifrar(m.politica_calidad),
                m.expresion_regular,
                m.version,
                m.actualizado_en.isoformat(),
                m.atributo_id,
                version_anterior,
            ),
        )
        if cursor.rowcount == 0:
            raise Conflicto("El atributo fue modificado por otra operacion; vuelva a consultarlo.")
        self._guardar_version(m)

    # ------------------------------------------------------------------ lecturas (usadas por LecturaAtributosSqlite)
    def obtener_memento(self, atributo_id: str) -> AtributoMemento | None:
        fila = self._c.execute("SELECT * FROM atributos WHERE id = ?", (atributo_id,)).fetchone()
        return None if fila is None else self._memento_desde_fila(fila)

    def listar_mementos(self, fuente_id: str | None) -> list[AtributoMemento]:
        if fuente_id:
            filas = self._c.execute(
                "SELECT * FROM atributos WHERE fuente_id = ? ORDER BY creado_en, nombre", (fuente_id,)
            ).fetchall()
        else:
            filas = self._c.execute("SELECT * FROM atributos ORDER BY creado_en, nombre").fetchall()
        return [self._memento_desde_fila(f) for f in filas]

    def versiones(self, atributo_id: str) -> list[AtributoMemento]:
        filas = self._c.execute(
            "SELECT estado FROM atributo_versiones WHERE atributo_id = ? ORDER BY version", (atributo_id,)
        ).fetchall()
        return [self._deserializar(f["estado"]) for f in filas]

    # ------------------------------------------------------------------ internos
    def _cifrar(self, valor: str | None) -> str | None:
        return None if valor is None else self._cifrador.cifrar(valor)

    def _descifrar(self, valor: str | None) -> str | None:
        return None if valor is None else self._cifrador.descifrar(valor)

    def _valores_fila(self, m: AtributoMemento) -> tuple:
        return (
            m.atributo_id,
            m.nombre,
            m.fuente_id,
            int(m.contiene_datos_personales),
            self._cifrar(m.definicion),
            self._cifrar(m.politica_calidad),
            m.expresion_regular,
            m.version,
            m.creado_en.isoformat(),
            m.actualizado_en.isoformat(),
        )

    def _memento_desde_fila(self, f: sqlite3.Row) -> AtributoMemento:
        return AtributoMemento(
            atributo_id=f["id"],
            nombre=f["nombre"],
            fuente_id=f["fuente_id"],
            contiene_datos_personales=bool(f["contiene_datos_personales"]),
            definicion=self._descifrar(f["definicion"]),
            politica_calidad=self._descifrar(f["politica_calidad"]),
            expresion_regular=f["expresion_regular"],
            version=f["version"],
            creado_en=datetime.fromisoformat(f["creado_en"]),
            actualizado_en=datetime.fromisoformat(f["actualizado_en"]),
        )

    def _guardar_version(self, m: AtributoMemento) -> None:
        self._c.execute(
            "INSERT INTO atributo_versiones (atributo_id, version, estado) VALUES (?,?,?)",
            (m.atributo_id, m.version, self._serializar(m)),
        )

    def _serializar(self, m: AtributoMemento) -> str:
        return json.dumps(
            {
                "atributo_id": m.atributo_id,
                "nombre": m.nombre,
                "fuente_id": m.fuente_id,
                "contiene_datos_personales": m.contiene_datos_personales,
                "definicion": self._cifrar(m.definicion),
                "politica_calidad": self._cifrar(m.politica_calidad),
                "expresion_regular": m.expresion_regular,
                "version": m.version,
                "creado_en": m.creado_en.isoformat(),
                "actualizado_en": m.actualizado_en.isoformat(),
            }
        )

    def _deserializar(self, texto: str) -> AtributoMemento:
        d = json.loads(texto)
        return AtributoMemento(
            atributo_id=d["atributo_id"],
            nombre=d["nombre"],
            fuente_id=d["fuente_id"],
            contiene_datos_personales=d["contiene_datos_personales"],
            definicion=self._descifrar(d["definicion"]),
            politica_calidad=self._descifrar(d["politica_calidad"]),
            expresion_regular=d["expresion_regular"],
            version=d["version"],
            creado_en=datetime.fromisoformat(d["creado_en"]),
            actualizado_en=datetime.fromisoformat(d["actualizado_en"]),
        )