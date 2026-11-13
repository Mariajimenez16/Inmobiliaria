"""Piezas base de DDD tactico (Entity, AggregateRoot, DomainEvent, IBusinessRule, Specification).

SOLID:
  - SRP: cada clase tiene una sola razon de cambio.
  - OCP: se extiende creando nuevas reglas/especificaciones, sin modificar estas bases.
"""

from abc import ABC, abstractmethod
from dataclasses import dataclass
from datetime import datetime
from typing import Generic, TypeVar

from c2_atributos.building_blocks.errores import ErrorDeNegocio

T = TypeVar("T")


@dataclass(frozen=True)
class EventoDeDominio:
    """Hecho que YA ocurrio (nombre en pasado). Es inmutable."""

    evento_id: str
    ocurrio_en: datetime


class Entidad:
    """Objeto con identidad estable: sigue siendo el mismo aunque cambien sus datos."""

    def __init__(self, id_: str):
        self.id = id_


class RaizDeAgregado(Entidad):
    """Punto de entrada del agregado; acumula los eventos de dominio que se producen."""

    def __init__(self, id_: str):
        super().__init__(id_)
        self._eventos: list[EventoDeDominio] = []

    def _registrar_evento(self, evento: EventoDeDominio) -> None:
        self._eventos.append(evento)

    def extraer_eventos(self) -> list[EventoDeDominio]:
        """Entrega los eventos pendientes y limpia la lista (asi no se publican dos veces)."""
        eventos, self._eventos = self._eventos, []
        return eventos


class IReglaDeNegocio(ABC):
    """Regla de negocio con nombre propio (diapositiva 9b)."""

    @property
    @abstractmethod
    def mensaje(self) -> str: ...

    @abstractmethod
    def se_cumple(self) -> bool: ...


def verificar_regla(regla: IReglaDeNegocio) -> None:
    if not regla.se_cumple():
        raise ErrorDeNegocio(regla.mensaje)


class Especificacion(ABC, Generic[T]):
    """Patron Specification: una condicion reutilizable que se puede combinar."""

    @abstractmethod
    def se_satisface_con(self, candidato: T) -> bool: ...

    def o(self, otra: "Especificacion[T]") -> "Especificacion[T]":
        return _O(self, otra)

    def y(self, otra: "Especificacion[T]") -> "Especificacion[T]":
        return _Y(self, otra)


class _O(Especificacion[T]):
    def __init__(self, a: Especificacion[T], b: Especificacion[T]):
        self._a, self._b = a, b

    def se_satisface_con(self, candidato: T) -> bool:
        return self._a.se_satisface_con(candidato) or self._b.se_satisface_con(candidato)


class _Y(Especificacion[T]):
    def __init__(self, a: Especificacion[T], b: Especificacion[T]):
        self._a, self._b = a, b

    def se_satisface_con(self, candidato: T) -> bool:
        return self._a.se_satisface_con(candidato) and self._b.se_satisface_con(candidato)