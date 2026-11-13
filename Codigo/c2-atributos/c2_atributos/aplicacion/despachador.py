"""Despachador del Outbox: lee los mensajes pendientes y los publica en el bus.

Por que Outbox: el agregado y el mensaje se guardan en la MISMA transaccion. Si el bus esta caido, el
mensaje queda pendiente y se publica despues; nunca se pierde un evento ni se publica uno que no se guardo.
Entrega 'al menos una vez': por eso los consumidores usan Inbox (idempotencia).

SOLID - SRP: solo mueve mensajes del outbox al bus. DIP: depende de puertos, no de SQLite.
"""

import logging

from c2_atributos.aplicacion.puertos import BusDeEventos, FabricaUnidadDeTrabajo

log = logging.getLogger("c2.outbox")


class DespachadorDeOutbox:
    def __init__(self, fabrica_uow: FabricaUnidadDeTrabajo, bus: BusDeEventos):
        self._fabrica_uow = fabrica_uow
        self._bus = bus

    def despachar(self) -> int:
        with self._fabrica_uow() as uow:
            pendientes = uow.outbox.pendientes()
            enviados = 0
            for mensaje in pendientes:
                self._bus.publicar(mensaje)
                uow.outbox.marcar_enviado(mensaje.message_id)
                uow.confirmar()  # se confirma mensaje a mensaje: un fallo no repite los ya enviados
                enviados += 1
        return enviados