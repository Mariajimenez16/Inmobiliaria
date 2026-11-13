"""Puertos (interfaces) que la aplicacion NECESITA y que la infraestructura implementa.

SOLID:
  - DIP: la aplicacion depende de estas abstracciones, nunca de SQLite, Fernet o del bus concreto.
  - ISP: interfaces pequenas y especificas; ningun consumidor depende de metodos que no usa.
"""

from abc import ABC, abstractmethod
from dataclasses import dataclass, field
from typing import Any, Callable

from c2_atributos.dominio.atributo import AtributoMemento, AtributoNegocio


# ---------------------------------------------------------------- mensajes de integracion
@dataclass(frozen=True)
class MensajeDeIntegracion:
    message_id: str
    tipo: str
    payload: dict[str, Any] = field(default_factory=dict)


# ---------------------------------------------------------------- escritura (agregado)
class RepositorioAtributos(ABC):
    @abstractmethod
    def obtener(self, atributo_id: str) -> AtributoNegocio | None: ...

    @abstractmethod
    def existe_nombre_en_fuente(self, nombre: str, fuente_id: str) -> bool: ...

    @abstractmethod
    def agregar(self, atributo: AtributoNegocio) -> None: ...

    @abstractmethod
    def actualizar(self, atributo: AtributoNegocio, version_anterior: int) -> None:
        """Concurrencia optimista: falla con Conflicto si otra operacion cambio la version."""


class PuertoOutbox(ABC):
    """Outbox: guarda el mensaje en la MISMA transaccion que el agregado."""

    @abstractmethod
    def agregar(self, mensaje: MensajeDeIntegracion) -> None: ...

    @abstractmethod
    def pendientes(self) -> list[MensajeDeIntegracion]: ...

    @abstractmethod
    def marcar_enviado(self, message_id: str) -> None: ...


class PuertoInbox(ABC):
    """Inbox: recuerda los mensajes ya procesados para que el consumidor sea idempotente."""

    @abstractmethod
    def ya_procesado(self, message_id: str) -> bool: ...

    @abstractmethod
    def registrar(self, message_id: str) -> None: ...


class PuertoFuentesConocidas(ABC):
    """Copia local de las identidades de fuente de C1 (supuesto de la V11, autonomia de C2)."""

    @abstractmethod
    def existe(self, fuente_id: str) -> bool: ...

    @abstractmethod
    def guardar(self, fuente_id: str, nombre: str) -> None: ...


class UnidadDeTrabajo(ABC):
    """Una transaccion: agregado + outbox + inbox se confirman juntos o no se confirma nada."""

    atributos: RepositorioAtributos
    outbox: PuertoOutbox
    inbox: PuertoInbox
    fuentes: PuertoFuentesConocidas

    @abstractmethod
    def __enter__(self) -> "UnidadDeTrabajo": ...

    @abstractmethod
    def __exit__(self, tipo, valor, traza) -> bool: ...

    @abstractmethod
    def confirmar(self) -> None: ...


FabricaUnidadDeTrabajo = Callable[[], UnidadDeTrabajo]


# ---------------------------------------------------------------- lectura (CQRS)
class LecturaAtributos(ABC):
    @abstractmethod
    def obtener(self, atributo_id: str) -> AtributoMemento | None: ...

    @abstractmethod
    def listar(self, fuente_id: str | None) -> list[AtributoMemento]: ...

    @abstractmethod
    def versiones(self, atributo_id: str) -> list[AtributoMemento]: ...


# ---------------------------------------------------------------- otros contextos (simulados)
class ConsultaResponsabilidad(ABC):
    """Lo que C2 necesita saber de C4 (responsabilidad sobre el dato). En esta entrega es un simulador."""

    @abstractmethod
    def tiene_responsable(self, atributo_id: str) -> bool: ...


# ---------------------------------------------------------------- servicios tecnicos
class Cifrador(ABC):
    @abstractmethod
    def cifrar(self, texto: str) -> str: ...

    @abstractmethod
    def descifrar(self, token: str) -> str: ...


class CacheClaveValor(ABC):
    @abstractmethod
    def obtener(self, clave: str) -> Any | None: ...

    @abstractmethod
    def poner(self, clave: str, valor: Any) -> None: ...

    @abstractmethod
    def invalidar(self, clave: str) -> None: ...


class BusDeEventos(ABC):
    @abstractmethod
    def publicar(self, mensaje: MensajeDeIntegracion) -> None: ...

    @abstractmethod
    def suscribir(self, tipo: str, manejador: Callable[[MensajeDeIntegracion], None]) -> None: ...