"""Cache de lectura (cache-aside) con vencimiento (TTL).

Por que cache: C3, C4 y C5 llaman a `consultarAtributo(id)` muchas veces (servicio abierto de C2) y el
dato cambia poco. En contenedores/varias replicas se cambia esta clase por Redis; el puerto no cambia.

SOLID:
  - OCP: `LecturaAtributosConCache` es un DECORADOR (patron Decorator): agrega cache sin modificar la
    lectura real.
  - LSP: puede usarse en cualquier lugar donde se espera un `LecturaAtributos`.
"""

import threading
import time
from typing import Any, Callable

from c2_atributos.aplicacion.puertos import CacheClaveValor, LecturaAtributos
from c2_atributos.dominio.atributo import AtributoMemento


class CacheEnMemoria(CacheClaveValor):
    def __init__(self, ttl_segundos: int = 60, tiempo: Callable[[], float] = time.monotonic):
        self._ttl = ttl_segundos
        self._tiempo = tiempo
        self._datos: dict[str, tuple[float, Any]] = {}
        self._candado = threading.Lock()

    def obtener(self, clave: str) -> Any | None:
        with self._candado:
            entrada = self._datos.get(clave)
            if entrada is None:
                return None
            vence, valor = entrada
            if self._tiempo() >= vence:
                del self._datos[clave]
                return None
            return valor

    def poner(self, clave: str, valor: Any) -> None:
        with self._candado:
            self._datos[clave] = (self._tiempo() + self._ttl, valor)

    def invalidar(self, clave: str) -> None:
        with self._candado:
            self._datos.pop(clave, None)


class LecturaAtributosConCache(LecturaAtributos):
    def __init__(self, interna: LecturaAtributos, cache: CacheClaveValor):
        self._interna = interna
        self._cache = cache
        self.aciertos = 0  # para demostrar y probar el cache
        self.fallos = 0

    @staticmethod
    def _clave(atributo_id: str) -> str:
        return f"atributo:{atributo_id}"

    def obtener(self, atributo_id: str) -> AtributoMemento | None:
        guardado = self._cache.obtener(self._clave(atributo_id))
        if guardado is not None:
            self.aciertos += 1
            return guardado
        self.fallos += 1
        memento = self._interna.obtener(atributo_id)
        if memento is not None:  # lo que no existe no se guarda
            self._cache.poner(self._clave(atributo_id), memento)
        return memento

    def listar(self, fuente_id: str | None) -> list[AtributoMemento]:
        return self._interna.listar(fuente_id)

    def versiones(self, atributo_id: str) -> list[AtributoMemento]:
        return self._interna.versiones(atributo_id)

    def invalidar(self, atributo_id: str) -> None:
        self._cache.invalidar(self._clave(atributo_id))