"""Eventos de dominio de C2: exactamente los 4 de la vista V6.b."""

from dataclasses import dataclass

from c2_atributos.building_blocks.dominio import EventoDeDominio


@dataclass(frozen=True)
class AtributoDeNegocioRegistrado(EventoDeDominio):
    atributo_id: str
    nombre: str
    fuente_id: str
    contiene_datos_personales: bool


@dataclass(frozen=True)
class MetadatoDeNegocioActualizado(EventoDeDominio):
    atributo_id: str
    version: int


@dataclass(frozen=True)
class PoliticaDeCalidadRegistrada(EventoDeDominio):
    atributo_id: str
    version: int


@dataclass(frozen=True)
class ExpresionRegularValidada(EventoDeDominio):
    atributo_id: str
    patron: str
    version: int