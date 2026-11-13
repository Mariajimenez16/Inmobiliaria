"""Punto de arranque del microservicio.

    uvicorn c2_atributos.main:crear_app_desde_entorno --factory --port 8012
"""

import logging

from fastapi import FastAPI

from c2_atributos.api.app import crear_app
from c2_atributos.config import Configuracion
from c2_atributos.contenedor import ContenedorC2


def crear_app_desde_entorno() -> FastAPI:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
    return crear_app(ContenedorC2(Configuracion.desde_entorno()))