"""Manejadores (casos de uso) de C2. Orquestan: cargan el agregado, le piden la operacion, guardan y
dejan los eventos en el outbox. La REGLA de negocio vive en el dominio, no aqui.

SOLID:
  - SRP: un manejador por caso de uso.
  - OCP: ManejadorDeModificacion (Template Method) permite otra modificacion nueva sin tocar las demas.
  - DIP: dependen de puertos (UnidadDeTrabajo, Reloj, ConsultaResponsabilidad), no de SQLite ni de HTTP.
Patrones: Template Method, Memento (se deshace el cambio en memoria si algo falla).
"""

import uuid
from abc import ABC, abstractmethod
from typing import Callable

from c2_atributos.aplicacion.comandos import (
    ActualizarMetadatoDeNegocio,
    CargarInventario,
    RegistrarAtributoDeNegocio,
    RegistrarPoliticaDeCalidad,
    SincronizarFuenteConocida,
    ValidarSintaxisDeExpresionRegular,
)
from c2_atributos.aplicacion.consultas import (
    ConsultarAtributo,
    ListarAtributos,
    ObtenerVersiones,
    SolicitarCompletar,
    ValidarPlantilla,
)
from c2_atributos.aplicacion.inventario import revisar_plantilla
from c2_atributos.aplicacion.mediador import Contexto
from c2_atributos.aplicacion.puertos import ConsultaResponsabilidad, FabricaUnidadDeTrabajo, LecturaAtributos
from c2_atributos.aplicacion.traductores import TraductorDeEventos
from c2_atributos.building_blocks.errores import Conflicto, ErrorDeNegocio, NoEncontrado
from c2_atributos.building_blocks.reloj import Reloj
from c2_atributos.contracts.v1 import (
    AtributoFichaV1,
    ErrorDeFilaV1,
    ResultadoCargaInventarioV1,
    ResultadoValidacionPlantillaV1,
    SolicitudDeCompletarV1,
    VersionAtributoV1,
)
from c2_atributos.dominio.atributo import AtributoMemento, AtributoNegocio
from c2_atributos.dominio.especificaciones import evaluar_completitud


# ====================================================================== ESCRITURA
class ManejadorRegistrarAtributo:
    """Comando 'Registrar atributo de negocio' -> evento 'Atributo de negocio registrado'."""

    def __init__(
        self,
        reloj: Reloj,
        traductor: TraductorDeEventos,
        generar_id: Callable[[], str] = lambda: str(uuid.uuid4()),
    ):
        self._reloj = reloj
        self._traductor = traductor
        self._generar_id = generar_id

    def manejar(self, comando: RegistrarAtributoDeNegocio, ctx: Contexto) -> str:
        uow = ctx.uow
        # V11: 'Un atributo solo se carga si su fuente existe en C1' (copia local de identidades)
        if not uow.fuentes.existe(comando.fuente_id.strip()):
            raise ErrorDeNegocio(
                f"La fuente '{comando.fuente_id}' no esta registrada en C1; registrela primero."
            )
        # V11: 'Un atributo se documenta una sola vez'
        if uow.atributos.existe_nombre_en_fuente(comando.nombre.strip(), comando.fuente_id.strip()):
            raise Conflicto("Ya existe un atributo con ese nombre en la misma fuente.")
        atributo = AtributoNegocio.registrar(
            atributo_id=self._generar_id(),
            nombre=comando.nombre,
            fuente_id=comando.fuente_id,
            contiene_datos_personales=comando.contiene_datos_personales,
            ahora=self._reloj.ahora(),
        )
        uow.atributos.agregar(atributo)
        for evento in atributo.extraer_eventos():
            uow.outbox.agregar(self._traductor.traducir(evento))
        return atributo.id


class ManejadorDeModificacion(ABC):
    """Template Method: el esqueleto (cargar -> foto -> aplicar -> guardar -> publicar) es siempre igual.
    Cada subclase solo implementa `aplicar`."""

    def __init__(self, reloj: Reloj, traductor: TraductorDeEventos):
        self._reloj = reloj
        self._traductor = traductor

    @abstractmethod
    def aplicar(self, atributo: AtributoNegocio, comando) -> None: ...

    def manejar(self, comando, ctx: Contexto) -> str:
        uow = ctx.uow
        atributo = uow.atributos.obtener(comando.atributo_id.strip())
        if atributo is None:
            raise NoEncontrado(f"No existe el atributo '{comando.atributo_id}'.")
        version_anterior = atributo.version
        foto = atributo.crear_memento()  # Memento: punto de retorno
        try:
            self.aplicar(atributo, comando)
            uow.atributos.actualizar(atributo, version_anterior)
            for evento in atributo.extraer_eventos():
                uow.outbox.agregar(self._traductor.traducir(evento))
        except Exception:
            atributo.restaurar(foto)  # si algo falla, el objeto en memoria vuelve a su estado anterior
            raise
        return atributo.id


class ManejadorActualizarMetadato(ManejadorDeModificacion):
    """Comando 'Actualizar metadato de negocio' -> 'Metadato de negocio actualizado'."""

    def aplicar(self, atributo: AtributoNegocio, comando: ActualizarMetadatoDeNegocio) -> None:
        atributo.actualizar_metadato_de_negocio(comando.definicion, self._reloj.ahora())


class ManejadorRegistrarPolitica(ManejadorDeModificacion):
    """Comando 'Registrar politica de calidad' -> 'Politica de calidad registrada'."""

    def aplicar(self, atributo: AtributoNegocio, comando: RegistrarPoliticaDeCalidad) -> None:
        atributo.registrar_politica_de_calidad(comando.descripcion, self._reloj.ahora())


class ManejadorValidarExpresion(ManejadorDeModificacion):
    """Comando 'Validar sintaxis de expresion regular' -> 'Expresion regular validada' (solo si es valida)."""

    def aplicar(self, atributo: AtributoNegocio, comando: ValidarSintaxisDeExpresionRegular) -> None:
        atributo.validar_expresion_regular(comando.patron, self._reloj.ahora())


class ManejadorSincronizarFuente:
    """Consumidor idempotente (Inbox) del evento 'Fuente registrada' de C1."""

    def manejar(self, comando: SincronizarFuenteConocida, ctx: Contexto) -> bool:
        uow = ctx.uow
        if uow.inbox.ya_procesado(comando.mensaje_id):
            return False  # duplicado: se ignora sin efectos
        uow.fuentes.guardar(comando.fuente_id.strip(), comando.nombre.strip())
        uow.inbox.registrar(comando.mensaje_id)
        return True


# ====================================================================== LECTURA
def construir_ficha(memento: AtributoMemento, responsabilidad: ConsultaResponsabilidad) -> AtributoFichaV1:
    atributo = AtributoNegocio.desde_memento(memento)
    completitud = evaluar_completitud(atributo, responsabilidad.tiene_responsable(memento.atributo_id))
    return AtributoFichaV1(
        atributo_id=memento.atributo_id,
        nombre=memento.nombre,
        fuente_id=memento.fuente_id,
        contiene_datos_personales=memento.contiene_datos_personales,
        version=memento.version,
        definicion=memento.definicion,
        politica_calidad=memento.politica_calidad,
        expresion_regular=memento.expresion_regular,
        incompleto=completitud.incompleto,
        faltantes=completitud.faltantes,
        responsable_obligatorio=completitud.responsable_obligatorio,
        creado_en=memento.creado_en.isoformat(),
        actualizado_en=memento.actualizado_en.isoformat(),
    )


class ManejadorConsultarAtributo:
    def __init__(self, lectura: LecturaAtributos, responsabilidad: ConsultaResponsabilidad):
        self._lectura = lectura
        self._responsabilidad = responsabilidad

    def manejar(self, consulta: ConsultarAtributo, ctx: Contexto) -> AtributoFichaV1:
        memento = self._lectura.obtener(consulta.atributo_id.strip())
        if memento is None:
            raise NoEncontrado(f"No existe el atributo '{consulta.atributo_id}'.")
        return construir_ficha(memento, self._responsabilidad)


class ManejadorListarAtributos:
    def __init__(self, lectura: LecturaAtributos, responsabilidad: ConsultaResponsabilidad):
        self._lectura = lectura
        self._responsabilidad = responsabilidad

    def manejar(self, consulta: ListarAtributos, ctx: Contexto) -> list[AtributoFichaV1]:
        fuente = consulta.fuente_id.strip() if consulta.fuente_id else None
        return [construir_ficha(m, self._responsabilidad) for m in self._lectura.listar(fuente)]


class ManejadorObtenerVersiones:
    def __init__(self, lectura: LecturaAtributos):
        self._lectura = lectura

    def manejar(self, consulta: ObtenerVersiones, ctx: Contexto) -> list[VersionAtributoV1]:
        atributo_id = consulta.atributo_id.strip()
        if self._lectura.obtener(atributo_id) is None:
            raise NoEncontrado(f"No existe el atributo '{consulta.atributo_id}'.")
        return [
            VersionAtributoV1(
                version=m.version,
                nombre=m.nombre,
                definicion=m.definicion,
                politica_calidad=m.politica_calidad,
                expresion_regular=m.expresion_regular,
                actualizado_en=m.actualizado_en.isoformat(),
            )
            for m in self._lectura.versiones(atributo_id)
        ]


class ManejadorSolicitarCompletar:
    """Contrato 3.4 `solicitarCompletar(atributo)`: dice que le falta al atributo (definicion, responsable).
    Es una consulta: no cambia nada ni produce eventos (Fase 4, decision 5)."""

    def __init__(self, lectura: LecturaAtributos, responsabilidad: ConsultaResponsabilidad):
        self._lectura = lectura
        self._responsabilidad = responsabilidad

    def manejar(self, consulta: SolicitarCompletar, ctx: Contexto) -> SolicitudDeCompletarV1:
        memento = self._lectura.obtener(consulta.atributo_id.strip())
        if memento is None:
            raise NoEncontrado(f"No existe el atributo '{consulta.atributo_id}'.")
        ficha = construir_ficha(memento, self._responsabilidad)
        if ficha.incompleto:
            mensaje = f"El atributo '{ficha.nombre}' esta incompleto. Falta: {', '.join(ficha.faltantes)}."
        else:
            mensaje = f"El atributo '{ficha.nombre}' esta completo; no hay nada que completar."
        return SolicitudDeCompletarV1(
            atributo_id=ficha.atributo_id,
            nombre=ficha.nombre,
            incompleto=ficha.incompleto,
            faltantes=ficha.faltantes,
            mensaje=mensaje,
        )


# ====================================================================== ADAPTADOR A4 (inventario manual)
def _a_contrato(revision) -> ResultadoValidacionPlantillaV1:
    return ResultadoValidacionPlantillaV1(
        valida=revision.valida,
        filas_leidas=revision.filas_leidas,
        errores=[ErrorDeFilaV1(e.fila, e.motivo) for e in revision.errores],
    )


class ManejadorValidarPlantilla:
    """A4 `validarPlantilla`: revisa la plantilla y devuelve las filas con problemas. No guarda nada."""

    def __init__(self, fabrica_uow: FabricaUnidadDeTrabajo):
        self._fabrica_uow = fabrica_uow

    def manejar(self, consulta: ValidarPlantilla, ctx: Contexto) -> ResultadoValidacionPlantillaV1:
        with self._fabrica_uow() as uow:
            return _a_contrato(revisar_plantilla(consulta.filas, uow))


class ManejadorCargarInventario:
    """A4 `cargarInventario`: si TODAS las filas son validas, registra los atributos en UNA sola
    transaccion (todo o nada). Reutiliza `ManejadorRegistrarAtributo`, asi que las reglas y los eventos
    'Atributo de negocio registrado' son exactamente los mismos que al registrar uno por uno."""

    def __init__(self, registrador: ManejadorRegistrarAtributo):
        self._registrador = registrador

    def manejar(self, comando: CargarInventario, ctx: Contexto) -> ResultadoCargaInventarioV1:
        revision = revisar_plantilla(comando.filas, ctx.uow)
        if not revision.valida:
            detalle = "; ".join(
                f"fila {e.fila}: {e.motivo}" if e.fila else e.motivo for e in revision.errores
            )
            raise ErrorDeNegocio(f"Plantilla rechazada, no se cargo nada. {detalle}")
        creados = [
            self._registrador.manejar(
                RegistrarAtributoDeNegocio(f.nombre, f.fuente_id, f.contiene_datos_personales), ctx
            )
            for f in revision.filas_validas
        ]
        return ResultadoCargaInventarioV1(atributos_creados=creados)