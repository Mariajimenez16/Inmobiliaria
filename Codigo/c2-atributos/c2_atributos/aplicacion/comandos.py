"""Comandos de C2: los 4 de la vista V6.b (+ uno interno de integracion).

Un comando es una SOLICITUD: puede ser rechazada. `validar()` revisa solo el formato (-> 400);
las reglas del negocio las revisa el dominio (-> 422).

SOLID - SRP: un comando solo transporta la intencion y valida su propio formato.
"""

from dataclasses import dataclass

from c2_atributos.building_blocks.errores import ErrorDeValidacion


def _requerir_texto(valor: object, campo: str) -> None:
    if not isinstance(valor, str) or not valor.strip():
        raise ErrorDeValidacion(f"El campo '{campo}' es obligatorio y debe ser texto.")


@dataclass(frozen=True)
class RegistrarAtributoDeNegocio:
    nombre: str
    fuente_id: str
    contiene_datos_personales: bool = False

    def validar(self) -> None:
        _requerir_texto(self.nombre, "nombre")
        _requerir_texto(self.fuente_id, "fuente_id")
        if not isinstance(self.contiene_datos_personales, bool):
            raise ErrorDeValidacion("El campo 'contiene_datos_personales' debe ser verdadero o falso.")


@dataclass(frozen=True)
class ActualizarMetadatoDeNegocio:
    atributo_id: str
    definicion: str

    def validar(self) -> None:
        _requerir_texto(self.atributo_id, "atributo_id")
        _requerir_texto(self.definicion, "definicion")


@dataclass(frozen=True)
class RegistrarPoliticaDeCalidad:
    atributo_id: str
    descripcion: str

    def validar(self) -> None:
        _requerir_texto(self.atributo_id, "atributo_id")
        _requerir_texto(self.descripcion, "descripcion")


@dataclass(frozen=True)
class ValidarSintaxisDeExpresionRegular:
    atributo_id: str
    patron: str

    def validar(self) -> None:
        _requerir_texto(self.atributo_id, "atributo_id")
        _requerir_texto(self.patron, "patron")


@dataclass(frozen=True)
class SincronizarFuenteConocida:
    """NO es un comando del dominio: es la reaccion de C2 al evento 'Fuente registrada' de C1
    (copia local de identidades de fuente, supuesto de la V11)."""

    mensaje_id: str
    fuente_id: str
    nombre: str

    def validar(self) -> None:
        _requerir_texto(self.mensaje_id, "evento_id")
        _requerir_texto(self.fuente_id, "fuente_id")
        _requerir_texto(self.nombre, "nombre")



@dataclass(frozen=True)
class CargarInventario:
    """Operacion cargarInventario(archivo) del adaptador A4. Todo o nada: si una fila falla, no se guarda ninguna."""

    filas: list

    def validar(self) -> None:
        if not isinstance(self.filas, list):
            raise ErrorDeValidacion("El campo 'filas' es obligatorio y debe ser una lista.")