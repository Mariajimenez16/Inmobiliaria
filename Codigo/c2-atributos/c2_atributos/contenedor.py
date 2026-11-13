"""Composition root: el UNICO lugar donde se crean los objetos concretos y se conectan entre si.

SOLID - DIP: aqui (y solo aqui) se decide que implementacion concreta recibe cada puerto.
Pipeline de comandos: Registro -> Validacion -> Despacho -> Transaccion -> manejador.
Pipeline de consultas: Registro -> Validacion -> manejador (no hay transaccion de escritura).
"""

import uuid
from typing import Any, Callable

from c2_atributos.aplicacion.comandos import (
    ActualizarMetadatoDeNegocio,
    CargarInventario,
    RegistrarAtributoDeNegocio,
    RegistrarPoliticaDeCalidad,
    SincronizarFuenteConocida,
    ValidarSintaxisDeExpresionRegular,
)
from c2_atributos.aplicacion.comportamientos import (
    DespachoComportamiento,
    RegistroComportamiento,
    TransaccionComportamiento,
    ValidacionComportamiento,
)
from c2_atributos.aplicacion.consultas import (
    ConsultarAtributo,
    ListarAtributos,
    ObtenerVersiones,
    SolicitarCompletar,
    ValidarPlantilla,
)
from c2_atributos.aplicacion.despachador import DespachadorDeOutbox
from c2_atributos.aplicacion.manejadores import (
    ManejadorActualizarMetadato,
    ManejadorCargarInventario,
    ManejadorConsultarAtributo,
    ManejadorListarAtributos,
    ManejadorObtenerVersiones,
    ManejadorRegistrarAtributo,
    ManejadorRegistrarPolitica,
    ManejadorSincronizarFuente,
    ManejadorSolicitarCompletar,
    ManejadorValidarExpresion,
    ManejadorValidarPlantilla,
)
from c2_atributos.aplicacion.mediador import Mediador
from c2_atributos.aplicacion.puertos import BusDeEventos, ConsultaResponsabilidad, MensajeDeIntegracion
from c2_atributos.aplicacion.traductores import crear_traductor_por_defecto
from c2_atributos.building_blocks.reloj import Reloj, RelojDelSistema
from c2_atributos.config import Configuracion
from c2_atributos.contracts.v1 import (
    AtributoDeNegocioRegistradoV1,
    ExpresionRegularValidadaV1,
    FuenteRegistradaV1,
    MetadatoDeNegocioActualizadoV1,
    PoliticaDeCalidadRegistradaV1,
)
from c2_atributos.infraestructura.bus_en_memoria import BusEnMemoria
from c2_atributos.infraestructura.cache import CacheEnMemoria, LecturaAtributosConCache
from c2_atributos.infraestructura.cifrado_fernet import CifradorFernet
from c2_atributos.infraestructura.esquema import inicializar_esquema
from c2_atributos.infraestructura.unidad_de_trabajo_sqlite import LecturaAtributosSqlite, UnidadDeTrabajoSqlite
from c2_atributos.simulacion.responsabilidad_simulada import ResponsabilidadSimuladaC4

TIPOS_PROPIOS = [
    AtributoDeNegocioRegistradoV1.TIPO,
    MetadatoDeNegocioActualizadoV1.TIPO,
    PoliticaDeCalidadRegistradaV1.TIPO,
    ExpresionRegularValidadaV1.TIPO,
]


class ContenedorC2:
    def __init__(
        self,
        config: Configuracion,
        reloj: Reloj | None = None,
        bus: BusDeEventos | None = None,
        responsabilidad: ConsultaResponsabilidad | None = None,
        generar_id: Callable[[], str] | None = None,
    ):
        self.config = config
        self.reloj = reloj or RelojDelSistema()
        self.bus = bus or BusEnMemoria()
        self.responsabilidad = responsabilidad or ResponsabilidadSimuladaC4()
        cifrador = CifradorFernet(config.clave_efectiva())
        inicializar_esquema(config.ruta_bd)

        self.fabrica_uow = lambda: UnidadDeTrabajoSqlite(config.ruta_bd, cifrador, self.reloj)
        traductor = crear_traductor_por_defecto()
        generar_id = generar_id or (lambda: str(uuid.uuid4()))

        # lectura con cache (Decorator)
        self.cache = CacheEnMemoria(config.ttl_cache_segundos)
        self.lectura = LecturaAtributosConCache(LecturaAtributosSqlite(config.ruta_bd, cifrador), self.cache)

        # comandos
        self.despachador = DespachadorDeOutbox(self.fabrica_uow, self.bus)
        self.mediador_comandos = Mediador(
            [
                RegistroComportamiento(),
                ValidacionComportamiento(),
                DespachoComportamiento(self.despachador),
                TransaccionComportamiento(self.fabrica_uow),
            ]
        )
        registrador = ManejadorRegistrarAtributo(self.reloj, traductor, generar_id)
        self.mediador_comandos.registrar(RegistrarAtributoDeNegocio, registrador)
        self.mediador_comandos.registrar(CargarInventario, ManejadorCargarInventario(registrador))
        self.mediador_comandos.registrar(
            ActualizarMetadatoDeNegocio, ManejadorActualizarMetadato(self.reloj, traductor)
        )
        self.mediador_comandos.registrar(
            RegistrarPoliticaDeCalidad, ManejadorRegistrarPolitica(self.reloj, traductor)
        )
        self.mediador_comandos.registrar(
            ValidarSintaxisDeExpresionRegular, ManejadorValidarExpresion(self.reloj, traductor)
        )
        self.mediador_comandos.registrar(SincronizarFuenteConocida, ManejadorSincronizarFuente())

        # consultas
        self.mediador_consultas = Mediador([RegistroComportamiento(), ValidacionComportamiento()])
        self.mediador_consultas.registrar(
            ConsultarAtributo, ManejadorConsultarAtributo(self.lectura, self.responsabilidad)
        )
        self.mediador_consultas.registrar(
            ListarAtributos, ManejadorListarAtributos(self.lectura, self.responsabilidad)
        )
        self.mediador_consultas.registrar(ObtenerVersiones, ManejadorObtenerVersiones(self.lectura))
        self.mediador_consultas.registrar(
            SolicitarCompletar, ManejadorSolicitarCompletar(self.lectura, self.responsabilidad)
        )
        self.mediador_consultas.registrar(ValidarPlantilla, ManejadorValidarPlantilla(self.fabrica_uow))

        # suscripciones (Observer)
        for tipo in TIPOS_PROPIOS:
            self.bus.suscribir(tipo, self._invalidar_cache)
        self.bus.suscribir(FuenteRegistradaV1.TIPO, self._al_llegar_fuente_registrada)

    # -------------------------------------------------------------- API de uso
    def ejecutar(self, comando: Any) -> Any:
        return self.mediador_comandos.enviar(comando)

    def consultar(self, consulta: Any) -> Any:
        return self.mediador_consultas.enviar(consulta)

    def recibir_fuente_registrada(self, payload: dict) -> bool:
        """Entrada del evento 'Fuente registrada' de C1 (por bus o por el endpoint de integracion)."""
        return self.ejecutar(
            SincronizarFuenteConocida(
                mensaje_id=payload.get("evento_id", ""),
                fuente_id=payload.get("fuente_id", ""),
                nombre=payload.get("nombre", ""),
            )
        )

    # -------------------------------------------------------------- reacciones internas
    def _invalidar_cache(self, mensaje: MensajeDeIntegracion) -> None:
        self.lectura.invalidar(mensaje.payload["atributo_id"])

    def _al_llegar_fuente_registrada(self, mensaje: MensajeDeIntegracion) -> None:
        self.recibir_fuente_registrada(mensaje.payload)