"""Consultas de C2 (lado de lectura de CQRS). Una consulta NO produce eventos (Fase 4, decision 5).

`ConsultarAtributo` es la operacion `consultarAtributo(id)` del servicio abierto que usan C3, C4 y C5.
"""

from dataclasses import dataclass

from c2_atributos.aplicacion.comandos import _requerir_texto
from c2_atributos.building_blocks.errores import ErrorDeValidacion


@dataclass(frozen=True)
class ConsultarAtributo:
    atributo_id: str

    def validar(self) -> None:
        _requerir_texto(self.atributo_id, "atributo_id")


@dataclass(frozen=True)
class ListarAtributos:
    fuente_id: str | None = None


@dataclass(frozen=True)
class ObtenerVersiones:
    atributo_id: str

    def validar(self) -> None:
        _requerir_texto(self.atributo_id, "atributo_id")


@dataclass(frozen=True)
class SolicitarCompletar:
    """Operacion solicitarCompletar(atributo) del contrato 3.4: indica que falta. No cambia nada."""

    atributo_id: str

    def validar(self) -> None:
        _requerir_texto(self.atributo_id, "atributo_id")


@dataclass(frozen=True)
class ValidarPlantilla:
    """Operacion validarPlantilla(archivo) del adaptador A4: revisa sin guardar."""

    filas: list

    def validar(self) -> None:
        if not isinstance(self.filas, list):
            raise ErrorDeValidacion("El campo 'filas' es obligatorio y debe ser una lista.")