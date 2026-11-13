"""IClock (diapositiva 9b): el reloj se inyecta para que las pruebas controlen el tiempo.

SOLID - DIP: el dominio y la aplicacion dependen de la abstraccion `Reloj`, no de datetime.now().
"""

from abc import ABC, abstractmethod
from datetime import datetime, timezone


class Reloj(ABC):
    @abstractmethod
    def ahora(self) -> datetime: ...


class RelojDelSistema(Reloj):
    def ahora(self) -> datetime:
        return datetime.now(timezone.utc)


class RelojFijo(Reloj):
    """Solo para pruebas."""

    def __init__(self, instante: datetime):
        self._instante = instante

    def ahora(self) -> datetime:
        return self._instante

    def avanzar(self, nuevo: datetime) -> None:
        self._instante = nuevo