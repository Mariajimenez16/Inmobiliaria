"""Adaptador A4 (V4d/V4f): carga del inventario manual de fuentes no estructuradas.

Contrato A4 (V4f): validarPlantilla(archivo) y cargarInventario(archivo).
  - "Carga manual guiada; no se cargan archivos con datos de personas."
  - "Plantilla invalida: se rechaza indicando las filas."
  - V8: A4 entra por C2 y rechaza las filas cuya fuente no existe en C1.

El formato de la plantilla NO esta definido en el diseno (V11, pregunta abierta 4). Decision nuestra:
una lista de filas con SOLO tres columnas: nombre, fuente_id, contiene_datos_personales. Cualquier otra
columna se rechaza, porque no hay forma de saber si trae datos de personas.

SOLID:
  - SRP: el traductor solo lee la forma de la plantilla; `revisar_plantilla` solo aplica las reglas.
  - DIP: `revisar_plantilla` usa la unidad de trabajo (puerto), no SQLite.
Patron: Capa anticorrupcion (el formato externo se traduce a filas propias antes de tocar el dominio).
"""

from dataclasses import dataclass, field
from typing import Any

from c2_atributos.aplicacion.puertos import UnidadDeTrabajo

COLUMNAS_PERMITIDAS = ("nombre", "fuente_id", "contiene_datos_personales")


@dataclass(frozen=True)
class FilaDeInventario:
    numero: int  # posicion en la plantilla, empezando en 1
    nombre: str
    fuente_id: str
    contiene_datos_personales: bool


@dataclass(frozen=True)
class ErrorDeFila:
    fila: int
    motivo: str


@dataclass
class RevisionDePlantilla:
    filas_leidas: int
    filas_validas: list[FilaDeInventario] = field(default_factory=list)
    errores: list[ErrorDeFila] = field(default_factory=list)

    @property
    def valida(self) -> bool:
        return self.filas_leidas > 0 and not self.errores


class TraductorDePlantillaA4:
    """Anticorrupcion: convierte las filas externas (diccionarios libres) en filas propias."""

    def traducir(self, filas: list[Any]) -> tuple[list[FilaDeInventario], list[ErrorDeFila]]:
        validas: list[FilaDeInventario] = []
        errores: list[ErrorDeFila] = []
        for numero, fila in enumerate(filas, start=1):
            if not isinstance(fila, dict):
                errores.append(ErrorDeFila(numero, "la fila no es un objeto con columnas."))
                continue
            problemas = self._problemas_de_forma(fila)
            if problemas:
                errores.extend(ErrorDeFila(numero, p) for p in problemas)
                continue
            validas.append(
                FilaDeInventario(
                    numero=numero,
                    nombre=fila["nombre"].strip(),
                    fuente_id=fila["fuente_id"].strip(),
                    contiene_datos_personales=fila.get("contiene_datos_personales", False),
                )
            )
        return validas, errores

    @staticmethod
    def _problemas_de_forma(fila: dict) -> list[str]:
        problemas = []
        for columna in fila:
            if columna not in COLUMNAS_PERMITIDAS:
                problemas.append(
                    f"columna no permitida '{columna}': la plantilla solo admite "
                    f"{', '.join(COLUMNAS_PERMITIDAS)} (no se cargan datos de personas)."
                )
        for columna in ("nombre", "fuente_id"):
            valor = fila.get(columna)
            if not isinstance(valor, str) or not valor.strip():
                problemas.append(f"la columna '{columna}' es obligatoria y debe ser texto.")
        if "contiene_datos_personales" in fila and not isinstance(fila["contiene_datos_personales"], bool):
            problemas.append("la columna 'contiene_datos_personales' debe ser verdadero o falso.")
        return problemas


def revisar_plantilla(filas: list[Any], uow: UnidadDeTrabajo) -> RevisionDePlantilla:
    """Aplica la forma (traductor) y las reglas con estado: fuente conocida y atributo no repetido."""
    validas_de_forma, errores = TraductorDePlantillaA4().traducir(filas)
    revision = RevisionDePlantilla(filas_leidas=len(filas), errores=list(errores))
    if not filas:
        revision.errores.append(ErrorDeFila(0, "la plantilla no trae filas."))
        return revision

    fuentes_conocidas: dict[str, bool] = {}
    vistos: set[tuple[str, str]] = set()
    for fila in validas_de_forma:
        if fila.fuente_id not in fuentes_conocidas:
            fuentes_conocidas[fila.fuente_id] = uow.fuentes.existe(fila.fuente_id)
        if not fuentes_conocidas[fila.fuente_id]:
            revision.errores.append(
                ErrorDeFila(fila.numero, f"la fuente '{fila.fuente_id}' no esta registrada en C1.")
            )
            continue
        clave = (fila.nombre, fila.fuente_id)
        if clave in vistos:
            revision.errores.append(ErrorDeFila(fila.numero, "atributo repetido dentro de la plantilla."))
            continue
        vistos.add(clave)
        if uow.atributos.existe_nombre_en_fuente(fila.nombre, fila.fuente_id):
            revision.errores.append(
                ErrorDeFila(fila.numero, "ya existe un atributo con ese nombre en esa fuente.")
            )
            continue
        revision.filas_validas.append(fila)
    revision.errores.sort(key=lambda e: e.fila)
    return revision