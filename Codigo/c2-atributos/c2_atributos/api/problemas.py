"""ProblemDetails (RFC 7807) y mapeo de errores a codigos HTTP (diapositiva 9b: 400/404/409/422).

  ErrorDeValidacion -> 400    NoEncontrado -> 404    Conflicto -> 409    ErrorDeNegocio -> 422

SOLID - SRP: la API decide COMO se ve un error; los casos de uso solo dicen QUE paso.
Un error inesperado devuelve 500 SIN detalles internos (no se filtra informacion).
"""

import logging

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from c2_atributos.building_blocks.errores import ErrorDeAplicacion

log = logging.getLogger("c2.api")


def _problema(request: Request, estado: int, titulo: str, detalle: str) -> JSONResponse:
    return JSONResponse(
        status_code=estado,
        media_type="application/problem+json",
        content={
            "type": "about:blank",
            "title": titulo,
            "status": estado,
            "detail": detalle,
            "instance": request.url.path,
        },
    )


def registrar_manejadores_de_error(app: FastAPI) -> None:
    @app.exception_handler(ErrorDeAplicacion)
    async def _error_de_aplicacion(request: Request, exc: ErrorDeAplicacion):
        return _problema(request, exc.estado_http, exc.titulo, exc.detalle)

    @app.exception_handler(RequestValidationError)
    async def _error_de_formato(request: Request, exc: RequestValidationError):
        campos = ", ".join(
            ".".join(str(parte) for parte in e["loc"] if parte != "body") or "cuerpo" for e in exc.errors()
        )
        return _problema(request, 400, "Solicitud invalida", f"Revise los campos: {campos}.")

    @app.exception_handler(Exception)
    async def _error_inesperado(request: Request, exc: Exception):
        log.exception("Error inesperado")
        return _problema(request, 500, "Error interno", "Ocurrio un error inesperado.")