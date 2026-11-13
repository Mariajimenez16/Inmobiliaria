"""Patron Memento (diapositiva 9b, IOriginador).

El originador sabe sacar una "foto" inmutable de su estado y volver a ella. El memento no expone
logica, solo datos. SOLID - ISP: interfaz de dos metodos, nada mas.
"""

from abc import ABC, abstractmethod
from typing import Generic, TypeVar

M = TypeVar("M")


class IOriginador(ABC, Generic[M]):
    @abstractmethod
    def crear_memento(self) -> M: ...

    @abstractmethod
    def restaurar(self, memento: M) -> None: ...