"""Especificacion 'Atributo incompleto'.

Regla de completitud (V6.b): 'si falta la definicion o la responsabilidad, el atributo debe aparecer como
incompleto'. Segun la V11 (tension 6), C2 conserva la DEFINICION de que es incompleto y C6 lo calcula y
lo muestra. Aqui esta esa definicion; la ficha de consulta la usa para marcar lo faltante.

La responsabilidad es de C4: se recibe como dato ya consultado (el dominio no llama a otros contextos).

SOLID - OCP: si manana 'incompleto' tambien exige clasificacion (contrato 4.1), se agrega otra
especificacion y se combina con .o(), sin modificar las existentes.
"""

from dataclasses import dataclass

from c2_atributos.building_blocks.dominio import Especificacion
from c2_atributos.dominio.atributo import AtributoNegocio


class SinDefinicion(Especificacion[AtributoNegocio]):
    def se_satisface_con(self, candidato: AtributoNegocio) -> bool:
        return not candidato.definicion


class SinResponsable(Especificacion[AtributoNegocio]):
    def __init__(self, tiene_responsable: bool):
        self._tiene_responsable = tiene_responsable

    def se_satisface_con(self, candidato: AtributoNegocio) -> bool:
        return not self._tiene_responsable


@dataclass(frozen=True)
class Completitud:
    incompleto: bool
    faltantes: list[str]
    responsable_obligatorio: bool


def evaluar_completitud(atributo: AtributoNegocio, tiene_responsable: bool) -> Completitud:
    sin_definicion = SinDefinicion()
    sin_responsable = SinResponsable(tiene_responsable)
    incompleto = sin_definicion.o(sin_responsable).se_satisface_con(atributo)
    faltantes = []
    if sin_definicion.se_satisface_con(atributo):
        faltantes.append("definicion")
    if sin_responsable.se_satisface_con(atributo):
        faltantes.append("responsable")
    return Completitud(
        incompleto=incompleto,
        faltantes=faltantes,
        # V6.b: para atributos con datos personales la responsabilidad es obligatoria
        responsable_obligatorio=atributo.contiene_datos_personales,
    )