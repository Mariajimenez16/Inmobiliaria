"""Contratos V1 de C2 (diapositiva 9b: proyecto Contracts versionado, records solo con primitivos).

Quien consume (C3, C4, C5, C6) se vuelve "conformista": adopta este modelo tal cual (V8, patron
Open Host Service + Published Language). El dominio de C2 NUNCA se expone directamente: los eventos
de dominio se traducen a estos contratos (ver aplicacion/traductores.py).

SOLID - SRP: estos objetos solo transportan datos. Cambiar el dominio no rompe a los consumidores.
"""

from dataclasses import dataclass
from typing import ClassVar


# ---------------------------------------------------------------- SALIDA: eventos de integracion (V6.b)
@dataclass(frozen=True)
class AtributoDeNegocioRegistradoV1:
    TIPO: ClassVar[str] = "AtributoDeNegocioRegistrado.v1"
    evento_id: str
    atributo_id: str
    nombre: str
    fuente_id: str
    contiene_datos_personales: bool
    ocurrio_en: str  # ISO 8601 (primitivo)


@dataclass(frozen=True)
class MetadatoDeNegocioActualizadoV1:
    TIPO: ClassVar[str] = "MetadatoDeNegocioActualizado.v1"
    evento_id: str
    atributo_id: str
    version: int
    ocurrio_en: str


@dataclass(frozen=True)
class PoliticaDeCalidadRegistradaV1:
    TIPO: ClassVar[str] = "PoliticaDeCalidadRegistrada.v1"
    evento_id: str
    atributo_id: str
    version: int
    ocurrio_en: str


@dataclass(frozen=True)
class ExpresionRegularValidadaV1:
    TIPO: ClassVar[str] = "ExpresionRegularValidada.v1"
    evento_id: str
    atributo_id: str
    patron: str
    version: int
    ocurrio_en: str


# ---------------------------------------------------------------- ENTRADA: evento que llega de C1
@dataclass(frozen=True)
class FuenteRegistradaV1:
    """Lo publica C1 (Published Language: la identidad de la fuente). C2 es conformista."""

    TIPO: ClassVar[str] = "FuenteRegistrada.v1"
    evento_id: str
    fuente_id: str
    nombre: str
    ocurrio_en: str


# ---------------------------------------------------------------- SALIDA: respuesta de consultarAtributo (contrato 4.1)
@dataclass(frozen=True)
class AtributoFichaV1:
    atributo_id: str
    nombre: str
    fuente_id: str
    contiene_datos_personales: bool
    version: int
    definicion: str | None
    politica_calidad: str | None
    expresion_regular: str | None
    incompleto: bool
    faltantes: list[str]
    responsable_obligatorio: bool
    creado_en: str
    actualizado_en: str


@dataclass(frozen=True)
class VersionAtributoV1:
    """Una version historica del atributo (contrato 4.1: obtenerVersiones)."""

    version: int
    nombre: str
    definicion: str | None
    politica_calidad: str | None
    expresion_regular: str | None
    actualizado_en: str



# ---------------------------------------------------------------- SALIDA: contrato 3.4 solicitarCompletar
@dataclass(frozen=True)
class SolicitudDeCompletarV1:
    """Respuesta de solicitarCompletar(atributo): que le falta al atributo."""

    atributo_id: str
    nombre: str
    incompleto: bool
    faltantes: list[str]
    mensaje: str


# ---------------------------------------------------------------- A4: carga de inventario (V4f)
@dataclass(frozen=True)
class ErrorDeFilaV1:
    fila: int  # 0 = error de toda la plantilla
    motivo: str


@dataclass(frozen=True)
class ResultadoValidacionPlantillaV1:
    valida: bool
    filas_leidas: int
    errores: list[ErrorDeFilaV1]


@dataclass(frozen=True)
class ResultadoCargaInventarioV1:
    atributos_creados: list[str]