"""Simulador de C4 (Responsables, autoridad y decisiones) para lo que C2 necesita saber.

En el diseno completo, C4 es otro contexto con su propio agregado. Aqui solo se simula la respuesta
'este atributo tiene (o no) responsable asignado'. Se puede reemplazar por un cliente HTTP real.

SOLID - LSP/DIP: cumple el puerto `ConsultaResponsabilidad`; C2 no sabe que es un simulador.
"""

from c2_atributos.aplicacion.puertos import ConsultaResponsabilidad


class ResponsabilidadSimuladaC4(ConsultaResponsabilidad):
    def __init__(self) -> None:
        self._con_responsable: set[str] = set()

    def asignar(self, atributo_id: str) -> None:
        self._con_responsable.add(atributo_id)

    def tiene_responsable(self, atributo_id: str) -> bool:
        return atributo_id in self._con_responsable
    