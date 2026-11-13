"""Bus de eventos EN MEMORIA: simula el broker de mensajes (RabbitMQ/Kafka) dentro del proceso.

Patron Observer: quien se interesa en un tipo de mensaje se suscribe; el publicador no sabe quien escucha.
Un suscriptor que falla NO afecta al publicador (asi se comporta un broker real).

SOLID - DIP: la aplicacion usa el puerto `BusDeEventos`; en produccion se reemplaza por un broker real.
"""

import logging
from collections import defaultdict
from typing import Callable

from c2_atributos.aplicacion.puertos import BusDeEventos, MensajeDeIntegracion

log = logging.getLogger("c2.bus")


class BusEnMemoria(BusDeEventos):
    def __init__(self) -> None:
        self._suscriptores: dict[str, list[Callable[[MensajeDeIntegracion], None]]] = defaultdict(list)

    def suscribir(self, tipo: str, manejador: Callable[[MensajeDeIntegracion], None]) -> None:
        self._suscriptores[tipo].append(manejador)

    def publicar(self, mensaje: MensajeDeIntegracion) -> None:
        for manejador in list(self._suscriptores[mensaje.tipo]):
            try:
                manejador(mensaje)
            except Exception:  # noqa: BLE001
                log.exception("Un suscriptor fallo al procesar %s", mensaje.tipo)