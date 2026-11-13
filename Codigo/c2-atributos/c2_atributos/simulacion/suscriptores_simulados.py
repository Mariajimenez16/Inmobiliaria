"""Suscriptores simulados de C4 y C6: reciben los eventos de integracion de C2 y los anotan.

Sirven para DEMOSTRAR la interoperabilidad (V8): quien se suscribe a que evento.
  - C6 (Diccionario y reporte) recibe 'Atributo de negocio registrado' y 'Metadato de negocio actualizado'.
  - C4 (Responsables y decisiones) recibe 'Metadato de negocio actualizado' (para la evidencia de decision).
Los otros dos eventos de C2 no tienen consumidor asignado en el diseno (pregunta abierta 1 de la V11).

SOLID - OCP: agregar otro contexto simulado = otra instancia y otra suscripcion, sin tocar a C2.
"""

from c2_atributos.aplicacion.puertos import BusDeEventos, MensajeDeIntegracion
from c2_atributos.contracts.v1 import AtributoDeNegocioRegistradoV1, MetadatoDeNegocioActualizadoV1


class SuscriptorSimulado:
    def __init__(self, nombre: str):
        self.nombre = nombre
        self.recibidos: list[dict] = []

    def recibir(self, mensaje: MensajeDeIntegracion) -> None:
        self.recibidos.append(
            {"tipo": mensaje.tipo, "atributo_id": mensaje.payload.get("atributo_id"), "mensaje_id": mensaje.message_id}
        )


def conectar_suscriptores_simulados(bus: BusDeEventos) -> dict[str, SuscriptorSimulado]:
    c4 = SuscriptorSimulado("C4")
    c6 = SuscriptorSimulado("C6")
    bus.suscribir(MetadatoDeNegocioActualizadoV1.TIPO, c4.recibir)
    bus.suscribir(AtributoDeNegocioRegistradoV1.TIPO, c6.recibir)
    bus.suscribir(MetadatoDeNegocioActualizadoV1.TIPO, c6.recibir)
    return {"C4": c4, "C6": c6}