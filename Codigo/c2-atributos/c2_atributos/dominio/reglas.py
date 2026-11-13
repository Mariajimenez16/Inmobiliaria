"""Reglas de negocio con nombre propio (invariantes de V6.b y contrato 4.4).

SOLID - OCP: una regla nueva es una clase nueva; no se toca el agregado existente.
"""

import re

from c2_atributos.building_blocks.dominio import IReglaDeNegocio

LONGITUD_MAXIMA_PATRON = 500


class SintaxisDeExpresionRegularValida(IReglaDeNegocio):
    """Invariante V6.b: 'Una expresion regular debe tener sintaxis valida antes de aceptarse como regla activa'.

    Solo se COMPILA el patron. Nunca se ejecuta contra datos (contrato 4.4, restriccion C1).
    """

    def __init__(self, patron: str):
        self._patron = patron
        self._error: str | None = None

    @property
    def mensaje(self) -> str:
        return f"La expresion regular no es valida: {self._error}"

    def se_cumple(self) -> bool:
        if len(self._patron) > LONGITUD_MAXIMA_PATRON:
            self._error = f"supera {LONGITUD_MAXIMA_PATRON} caracteres"
            return False
        try:
            re.compile(self._patron)
        except re.error as exc:
            self._error = str(exc)
            return False
        return True