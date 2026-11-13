"""Traductores: evento de DOMINIO -> contrato de INTEGRACION V1 (diapositiva 9b).

El dominio no se publica hacia afuera tal cual; se traduce al contrato publico y versionado.

SOLID:
  - SRP: solo traduce.
  - OCP: un evento nuevo = una funcion nueva registrada; no se modifica el traductor.
"""

from dataclasses import asdict
from typing import Any, Callable

from c2_atributos.aplicacion.puertos import MensajeDeIntegracion
from c2_atributos.building_blocks.dominio import EventoDeDominio
from c2_atributos.contracts.v1 import (
    AtributoDeNegocioRegistradoV1,
    ExpresionRegularValidadaV1,
    MetadatoDeNegocioActualizadoV1,
    PoliticaDeCalidadRegistradaV1,
)
from c2_atributos.dominio.eventos import (
    AtributoDeNegocioRegistrado,
    ExpresionRegularValidada,
    MetadatoDeNegocioActualizado,
    PoliticaDeCalidadRegistrada,
)


class TraductorDeEventos:
    def __init__(self) -> None:
        self._funciones: dict[type, Callable[[Any], Any]] = {}

    def registrar(self, tipo_evento: type, funcion: Callable[[Any], Any]) -> None:
        self._funciones[tipo_evento] = funcion

    def traducir(self, evento: EventoDeDominio) -> MensajeDeIntegracion:
        funcion = self._funciones.get(type(evento))
        if funcion is None:
            raise LookupError(f"No hay traductor para {type(evento).__name__}")
        contrato = funcion(evento)
        return MensajeDeIntegracion(
            message_id=evento.evento_id, tipo=contrato.TIPO, payload=asdict(contrato)
        )


def crear_traductor_por_defecto() -> TraductorDeEventos:
    t = TraductorDeEventos()
    t.registrar(
        AtributoDeNegocioRegistrado,
        lambda e: AtributoDeNegocioRegistradoV1(
            evento_id=e.evento_id,
            atributo_id=e.atributo_id,
            nombre=e.nombre,
            fuente_id=e.fuente_id,
            contiene_datos_personales=e.contiene_datos_personales,
            ocurrio_en=e.ocurrio_en.isoformat(),
        ),
    )
    t.registrar(
        MetadatoDeNegocioActualizado,
        lambda e: MetadatoDeNegocioActualizadoV1(
            evento_id=e.evento_id,
            atributo_id=e.atributo_id,
            version=e.version,
            ocurrio_en=e.ocurrio_en.isoformat(),
        ),
    )
    t.registrar(
        PoliticaDeCalidadRegistrada,
        lambda e: PoliticaDeCalidadRegistradaV1(
            evento_id=e.evento_id,
            atributo_id=e.atributo_id,
            version=e.version,
            ocurrio_en=e.ocurrio_en.isoformat(),
        ),
    )
    t.registrar(
        ExpresionRegularValidada,
        lambda e: ExpresionRegularValidadaV1(
            evento_id=e.evento_id,
            atributo_id=e.atributo_id,
            patron=e.patron,
            version=e.version,
            ocurrio_en=e.ocurrio_en.isoformat(),
        ),
    )
    return t