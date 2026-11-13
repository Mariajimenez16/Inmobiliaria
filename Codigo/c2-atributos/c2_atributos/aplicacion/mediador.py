"""Mediador con pipeline de comportamientos (CQRS + Mediator, diapositivas 9b y 10).

Nadie llama a un manejador directamente: todo pasa por `Mediador.enviar(...)`, y asi no se salta
el pipeline (Registro -> Validacion -> Transaccion).

SOLID:
  - SRP: el mediador solo enruta; cada comportamiento hace una sola cosa transversal.
  - OCP: agregar un comportamiento nuevo no cambia ni al mediador ni a los manejadores.
  - DIP: los manejadores se registran por abstraccion (tienen un metodo `manejar`).
Patron Chain of Responsibility: cada comportamiento decide si pasa al siguiente.
"""

from abc import ABC, abstractmethod
from typing import Any, Callable, Protocol


class Contexto:
    """Datos que viajan por el pipeline (por ejemplo, la unidad de trabajo abierta)."""

    def __init__(self) -> None:
        self.uow: Any = None


class Manejador(Protocol):
    def manejar(self, peticion: Any, ctx: Contexto) -> Any: ...


Siguiente = Callable[[Any, Contexto], Any]


class Comportamiento(ABC):
    @abstractmethod
    def ejecutar(self, peticion: Any, ctx: Contexto, siguiente: Siguiente) -> Any: ...


class Mediador:
    def __init__(self, comportamientos: list[Comportamiento] | None = None):
        self._manejadores: dict[type, Manejador] = {}
        self._comportamientos = list(comportamientos or [])

    def registrar(self, tipo_peticion: type, manejador: Manejador) -> None:
        self._manejadores[tipo_peticion] = manejador

    def enviar(self, peticion: Any) -> Any:
        manejador = self._manejadores.get(type(peticion))
        if manejador is None:
            raise LookupError(f"No hay manejador para {type(peticion).__name__}")
        return self._construir(0, manejador)(peticion, Contexto())

    def _construir(self, posicion: int, manejador: Manejador) -> Siguiente:
        if posicion == len(self._comportamientos):
            return lambda p, c: manejador.manejar(p, c)
        comportamiento = self._comportamientos[posicion]
        siguiente = self._construir(posicion + 1, manejador)
        return lambda p, c: comportamiento.ejecutar(p, c, siguiente)