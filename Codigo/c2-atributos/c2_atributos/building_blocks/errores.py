"""Errores de la aplicacion y su codigo HTTP (mapeo de las diapositivas 9b: 400/404/409/422).

SOLID - SRP: aqui solo se define QUE tipo de fallo es; el traductor a ProblemDetails vive en la capa API.
"""


class ErrorDeAplicacion(Exception):
    estado_http = 500
    titulo = "Error interno"

    def __init__(self, detalle: str):
        super().__init__(detalle)
        self.detalle = detalle


class ErrorDeValidacion(ErrorDeAplicacion):
    """La solicitud esta mal formada (campo vacio, tipo incorrecto). -> 400"""

    estado_http = 400
    titulo = "Solicitud invalida"


class NoEncontrado(ErrorDeAplicacion):
    """El recurso pedido no existe. -> 404"""

    estado_http = 404
    titulo = "Recurso no encontrado"


class Conflicto(ErrorDeAplicacion):
    """Choca con el estado actual (duplicado, modificacion concurrente). -> 409"""

    estado_http = 409
    titulo = "Conflicto con el estado actual"


class ErrorDeNegocio(ErrorDeAplicacion):
    """Se violo una regla o invariante del dominio. -> 422"""

    estado_http = 422
    titulo = "Regla de negocio violada"