"""Agregado AtributoDeNegocio (RAIZ), vista V6.b.

Contiene: metadato de negocio (definicion), politica de calidad y expresion regular.
Son REFERENCIAS externas (no entran al agregado): la fuente (C1) y la responsabilidad (C4).

Invariantes que protege (V6.b / V11):
  - identidad estable, nombre y fuente obligatorios;
  - definicion y responsabilidad pueden completarse despues (por eso nacen vacias);
  - la regex solo se acepta si su sintaxis es valida y NO se ejecuta contra datos;
  - cada cambio crea una version (contrato 4.1).

SOLID:
  - SRP: protege las reglas del atributo; no sabe de bases de datos ni de HTTP.
  - DIP: recibe la hora ya resuelta (no llama a datetime.now()).
Patron Memento: el agregado es el Originador (IOriginador); AtributoMemento es la "foto".
"""

import uuid
from dataclasses import dataclass
from datetime import datetime

from c2_atributos.building_blocks.dominio import RaizDeAgregado, verificar_regla
from c2_atributos.building_blocks.guard import Guard
from c2_atributos.building_blocks.memento import IOriginador
from c2_atributos.dominio.eventos import (
    AtributoDeNegocioRegistrado,
    ExpresionRegularValidada,
    MetadatoDeNegocioActualizado,
    PoliticaDeCalidadRegistrada,
)
from c2_atributos.dominio.reglas import SintaxisDeExpresionRegularValida


@dataclass(frozen=True)
class AtributoMemento:
    """Foto inmutable del estado del atributo (solo datos)."""

    atributo_id: str
    nombre: str
    fuente_id: str
    contiene_datos_personales: bool
    definicion: str | None
    politica_calidad: str | None
    expresion_regular: str | None
    version: int
    creado_en: datetime
    actualizado_en: datetime


def _nuevo_id() -> str:
    return str(uuid.uuid4())


class AtributoNegocio(RaizDeAgregado, IOriginador[AtributoMemento]):
    def __init__(self, memento: AtributoMemento):
        super().__init__(memento.atributo_id)
        self._cargar(memento)

    # ------------------------------------------------------------------ fabricas
    @classmethod
    def registrar(
        cls,
        atributo_id: str,
        nombre: str,
        fuente_id: str,
        contiene_datos_personales: bool,
        ahora: datetime,
    ) -> "AtributoNegocio":
        """Comando 'Registrar atributo de negocio'. Minimo: nombre y fuente."""
        atributo_id = Guard.no_vacio(atributo_id, "La identidad del atributo")
        nombre = Guard.no_vacio(nombre, "El nombre del atributo", 200)
        fuente_id = Guard.no_vacio(fuente_id, "La fuente del atributo", 100)
        atributo = cls(
            AtributoMemento(
                atributo_id=atributo_id,
                nombre=nombre,
                fuente_id=fuente_id,
                contiene_datos_personales=bool(contiene_datos_personales),
                definicion=None,
                politica_calidad=None,
                expresion_regular=None,
                version=1,
                creado_en=ahora,
                actualizado_en=ahora,
            )
        )
        atributo._registrar_evento(
            AtributoDeNegocioRegistrado(
                evento_id=_nuevo_id(),
                ocurrio_en=ahora,
                atributo_id=atributo_id,
                nombre=nombre,
                fuente_id=fuente_id,
                contiene_datos_personales=bool(contiene_datos_personales),
            )
        )
        return atributo

    @classmethod
    def desde_memento(cls, memento: AtributoMemento) -> "AtributoNegocio":
        """Reconstruye el agregado desde la persistencia; NO produce eventos."""
        return cls(memento)

    # ------------------------------------------------------------------ comportamiento
    def actualizar_metadato_de_negocio(self, definicion: str, ahora: datetime) -> None:
        """Comando 'Actualizar metadato de negocio' (la definicion es obligatoria, contrato 4.4)."""
        self.definicion = Guard.no_vacio(definicion, "La definicion", 2000)
        self._nueva_version(ahora)
        self._registrar_evento(
            MetadatoDeNegocioActualizado(
                evento_id=_nuevo_id(), ocurrio_en=ahora, atributo_id=self.id, version=self.version
            )
        )

    def registrar_politica_de_calidad(self, descripcion: str, ahora: datetime) -> None:
        """Comando 'Registrar politica de calidad'. Se documenta; no se ejecuta contra datos."""
        self.politica_calidad = Guard.no_vacio(descripcion, "La politica de calidad", 1000)
        self._nueva_version(ahora)
        self._registrar_evento(
            PoliticaDeCalidadRegistrada(
                evento_id=_nuevo_id(), ocurrio_en=ahora, atributo_id=self.id, version=self.version
            )
        )

    def validar_expresion_regular(self, patron: str, ahora: datetime) -> None:
        """Comando 'Validar sintaxis de expresion regular'. El evento solo existe si la sintaxis es valida."""
        patron = Guard.no_vacio(patron, "La expresion regular")
        verificar_regla(SintaxisDeExpresionRegularValida(patron))
        self.expresion_regular = patron
        self._nueva_version(ahora)
        self._registrar_evento(
            ExpresionRegularValidada(
                evento_id=_nuevo_id(),
                ocurrio_en=ahora,
                atributo_id=self.id,
                patron=patron,
                version=self.version,
            )
        )

    # ------------------------------------------------------------------ Memento
    def crear_memento(self) -> AtributoMemento:
        return AtributoMemento(
            atributo_id=self.id,
            nombre=self.nombre,
            fuente_id=self.fuente_id,
            contiene_datos_personales=self.contiene_datos_personales,
            definicion=self.definicion,
            politica_calidad=self.politica_calidad,
            expresion_regular=self.expresion_regular,
            version=self.version,
            creado_en=self.creado_en,
            actualizado_en=self.actualizado_en,
        )

    def restaurar(self, memento: AtributoMemento) -> None:
        if memento.atributo_id != self.id:
            raise ValueError("El memento pertenece a otro atributo.")
        self._cargar(memento)
        self._eventos.clear()  # lo deshecho no debe publicarse

    # ------------------------------------------------------------------ internos
    def _cargar(self, m: AtributoMemento) -> None:
        self.nombre = m.nombre
        self.fuente_id = m.fuente_id
        self.contiene_datos_personales = m.contiene_datos_personales
        self.definicion = m.definicion
        self.politica_calidad = m.politica_calidad
        self.expresion_regular = m.expresion_regular
        self.version = m.version
        self.creado_en = m.creado_en
        self.actualizado_en = m.actualizado_en

    def _nueva_version(self, ahora: datetime) -> None:
        self.version += 1
        self.actualizado_en = ahora