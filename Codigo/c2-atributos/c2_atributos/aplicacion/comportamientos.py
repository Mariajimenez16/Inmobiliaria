"""Comportamientos del pipeline: Registro -> Validacion -> Despacho -> Transaccion -> manejador.

(Diapositiva 9b: Registro -> Validacion -> Transaccion. 'Despacho' es el paso extra del Outbox:
corre cuando la transaccion ya se confirmo.)

SOLID - SRP: cada clase hace UNA cosa transversal; los manejadores quedan limpios.
"""

import logging
from typing import Any

from c2_atributos.aplicacion.despachador import DespachadorDeOutbox
from c2_atributos.aplicacion.mediador import Comportamiento, Contexto, Siguiente
from c2_atributos.aplicacion.puertos import FabricaUnidadDeTrabajo
from c2_atributos.building_blocks.errores import ErrorDeAplicacion

log = logging.getLogger("c2.pipeline")


class RegistroComportamiento(Comportamiento):
    """Deja constancia de cada peticion y de su resultado (nunca registra datos de personas)."""

    def ejecutar(self, peticion: Any, ctx: Contexto, siguiente: Siguiente) -> Any:
        nombre = type(peticion).__name__
        log.info("-> %s", nombre)
        try:
            resultado = siguiente(peticion, ctx)
        except ErrorDeAplicacion as exc:
            log.warning("<- %s rechazada (%s): %s", nombre, exc.estado_http, exc.detalle)
            raise
        log.info("<- %s ok", nombre)
        return resultado


class ValidacionComportamiento(Comportamiento):
    """Valida el FORMATO de la peticion (campos vacios, tipos). Falla con 400, antes de abrir transaccion."""

    def ejecutar(self, peticion: Any, ctx: Contexto, siguiente: Siguiente) -> Any:
        validar = getattr(peticion, "validar", None)
        if callable(validar):
            validar()
        return siguiente(peticion, ctx)


class TransaccionComportamiento(Comportamiento):
    """Abre la unidad de trabajo, ejecuta el manejador y confirma. Si algo falla, no se guarda nada."""

    def __init__(self, fabrica_uow: FabricaUnidadDeTrabajo):
        self._fabrica_uow = fabrica_uow

    def ejecutar(self, peticion: Any, ctx: Contexto, siguiente: Siguiente) -> Any:
        with self._fabrica_uow() as uow:
            ctx.uow = uow
            resultado = siguiente(peticion, ctx)
            uow.confirmar()
            return resultado


class DespachoComportamiento(Comportamiento):
    """Despues de confirmar, publica los mensajes pendientes del outbox. Un fallo aqui NO deshace el comando."""

    def __init__(self, despachador: DespachadorDeOutbox):
        self._despachador = despachador

    def ejecutar(self, peticion: Any, ctx: Contexto, siguiente: Siguiente) -> Any:
        resultado = siguiente(peticion, ctx)
        try:
            self._despachador.despachar()
        except Exception:  # noqa: BLE001 - el mensaje sigue en el outbox y se reintentara
            log.exception("No se pudo despachar el outbox; quedara pendiente")
        return resultado