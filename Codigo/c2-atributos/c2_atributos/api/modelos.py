"""Cuerpos de las solicitudes HTTP. Solo describen la FORMA del JSON; las reglas viven en el dominio."""

from typing import Any

from pydantic import BaseModel


class RegistrarAtributoIn(BaseModel):
    nombre: str
    fuente_id: str
    contiene_datos_personales: bool = False


class MetadatoNegocioIn(BaseModel):
    definicion: str


class PoliticaCalidadIn(BaseModel):
    descripcion: str


class ExpresionRegularIn(BaseModel):
    patron: str


class FuenteRegistradaIn(BaseModel):
    """Forma del evento 'Fuente registrada' que publica C1 (contrato FuenteRegistradaV1)."""

    evento_id: str
    fuente_id: str
    nombre: str
    ocurrio_en: str | None = None


class PlantillaInventarioIn(BaseModel):
    """Plantilla del adaptador A4. Cada fila solo admite: nombre, fuente_id, contiene_datos_personales."""

    filas: list[Any]