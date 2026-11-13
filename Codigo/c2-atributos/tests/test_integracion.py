import sqlite3
from datetime import datetime, timezone

import pytest

from c2_atributos.aplicacion.comandos import (
    ActualizarMetadatoDeNegocio,
    RegistrarAtributoDeNegocio,
    RegistrarPoliticaDeCalidad,
    SincronizarFuenteConocida,
    ValidarSintaxisDeExpresionRegular,
)
from c2_atributos.aplicacion.consultas import ConsultarAtributo, ListarAtributos, ObtenerVersiones
from c2_atributos.aplicacion.despachador import DespachadorDeOutbox
from c2_atributos.aplicacion.puertos import BusDeEventos, MensajeDeIntegracion
from c2_atributos.building_blocks.errores import Conflicto, ErrorDeNegocio, ErrorDeValidacion, NoEncontrado
from c2_atributos.building_blocks.reloj import RelojFijo
from c2_atributos.config import Configuracion
from c2_atributos.contenedor import TIPOS_PROPIOS, ContenedorC2
from c2_atributos.infraestructura.bus_en_memoria import BusEnMemoria

AHORA = datetime(2026, 11, 7, 12, 0, tzinfo=timezone.utc)
SECRETO = "Documento de identidad del titular"


def crear_contenedor(tmp_path, bus=None):
    config = Configuracion(
        ruta_bd=str(tmp_path / "c2.db"), clave_cifrado="clave-de-prueba", entorno="test"
    )
    return ContenedorC2(config, reloj=RelojFijo(AHORA), bus=bus)


@pytest.fixture
def c2(tmp_path):
    contenedor = crear_contenedor(tmp_path)
    contenedor.recibidos = []
    for tipo in TIPOS_PROPIOS:
        contenedor.bus.suscribir(tipo, contenedor.recibidos.append)
    contenedor.ejecutar(SincronizarFuenteConocida("msg-1", "fuente-1", "S1 Arrendamientos"))
    return contenedor


def registrar(c2, nombre="Cedula", fuente="fuente-1", personales=True):
    return c2.ejecutar(RegistrarAtributoDeNegocio(nombre, fuente, personales))


def test_no_se_puede_registrar_un_atributo_de_una_fuente_desconocida(c2):
    with pytest.raises(ErrorDeNegocio):
        registrar(c2, fuente="fuente-que-no-existe")


def test_registrar_y_consultar_muestra_atributo_incompleto(c2):
    atributo_id = registrar(c2)
    ficha = c2.consultar(ConsultarAtributo(atributo_id))
    assert ficha.nombre == "Cedula" and ficha.version == 1
    assert ficha.incompleto is True
    assert ficha.faltantes == ["definicion", "responsable"]
    assert ficha.responsable_obligatorio is True


def test_registrar_publica_el_evento_via_outbox_y_bus(c2):
    registrar(c2)
    assert [m.tipo for m in c2.recibidos] == ["AtributoDeNegocioRegistrado.v1"]
    with c2.fabrica_uow() as uow:
        assert uow.outbox.pendientes() == []  # ya se despacho


def test_duplicado_en_la_misma_fuente_es_conflicto(c2):
    registrar(c2)
    with pytest.raises(Conflicto):
        registrar(c2, nombre="CEDULA")  # mayusculas distintas = mismo atributo


def test_validacion_de_formato_da_400(c2):
    with pytest.raises(ErrorDeValidacion):
        c2.ejecutar(RegistrarAtributoDeNegocio("   ", "fuente-1"))


def test_flujo_completo_versiones_y_completitud(c2):
    atributo_id = registrar(c2)
    c2.ejecutar(ActualizarMetadatoDeNegocio(atributo_id, SECRETO))
    c2.ejecutar(RegistrarPoliticaDeCalidad(atributo_id, "Solo numeros, 6 a 10 digitos"))
    c2.ejecutar(ValidarSintaxisDeExpresionRegular(atributo_id, r"^[0-9]{6,10}$"))
    c2.responsabilidad.asignar(atributo_id)  # C4 simulado

    ficha = c2.consultar(ConsultarAtributo(atributo_id))
    assert ficha.version == 4
    assert ficha.definicion == SECRETO
    assert ficha.expresion_regular == r"^[0-9]{6,10}$"
    assert ficha.incompleto is False

    versiones = c2.consultar(ObtenerVersiones(atributo_id))
    assert [v.version for v in versiones] == [1, 2, 3, 4]
    assert versiones[0].definicion is None and versiones[1].definicion == SECRETO
    assert len(c2.recibidos) == 4  # registrado + metadato + politica + regex


def test_regex_invalida_da_422_y_no_cambia_nada(c2):
    atributo_id = registrar(c2)
    antes = len(c2.recibidos)
    with pytest.raises(ErrorDeNegocio):
        c2.ejecutar(ValidarSintaxisDeExpresionRegular(atributo_id, "(["))
    assert c2.consultar(ConsultarAtributo(atributo_id)).version == 1
    assert len(c2.recibidos) == antes  # no hay evento 'Expresion regular validada'


def test_modificar_atributo_inexistente_da_404(c2):
    with pytest.raises(NoEncontrado):
        c2.ejecutar(ActualizarMetadatoDeNegocio("no-existe", "algo"))
    with pytest.raises(NoEncontrado):
        c2.consultar(ConsultarAtributo("no-existe"))


def test_listar_filtra_por_fuente(c2):
    c2.ejecutar(SincronizarFuenteConocida("msg-2", "fuente-2", "S2 CRM"))
    registrar(c2, "Cedula", "fuente-1")
    registrar(c2, "Telefono", "fuente-2")
    assert len(c2.consultar(ListarAtributos())) == 2
    assert [f.nombre for f in c2.consultar(ListarAtributos("fuente-2"))] == ["Telefono"]


def test_inbox_hace_idempotente_al_consumidor(c2):
    assert c2.ejecutar(SincronizarFuenteConocida("msg-9", "fuente-9", "S9")) is True
    assert c2.ejecutar(SincronizarFuenteConocida("msg-9", "fuente-9", "S9")) is False  # duplicado


def test_la_definicion_se_guarda_cifrada_en_reposo(c2, tmp_path):
    atributo_id = registrar(c2)
    c2.ejecutar(ActualizarMetadatoDeNegocio(atributo_id, SECRETO))
    c2.ejecutar(RegistrarPoliticaDeCalidad(atributo_id, "Politica confidencial"))
    conexion = sqlite3.connect(str(tmp_path / "c2.db"))
    guardado = conexion.execute("SELECT definicion, politica_calidad FROM atributos").fetchone()
    versiones = " ".join(r[0] for r in conexion.execute("SELECT estado FROM atributo_versiones"))
    conexion.close()
    assert guardado[0] != SECRETO and guardado[0].startswith("gAAAA")  # token Fernet
    assert guardado[1] != "Politica confidencial"
    assert SECRETO not in versiones and "Politica confidencial" not in versiones


def test_cache_acierta_y_se_invalida_al_cambiar(c2):
    atributo_id = registrar(c2)
    c2.consultar(ConsultarAtributo(atributo_id))
    c2.consultar(ConsultarAtributo(atributo_id))
    assert (c2.lectura.fallos, c2.lectura.aciertos) == (1, 1)
    c2.ejecutar(ActualizarMetadatoDeNegocio(atributo_id, "Nueva definicion"))  # invalida
    ficha = c2.consultar(ConsultarAtributo(atributo_id))
    assert ficha.definicion == "Nueva definicion"  # no se sirvio un dato viejo
    assert c2.lectura.fallos == 2


class _BusRoto(BusDeEventos):
    def publicar(self, mensaje):
        raise ConnectionError("broker caido")

    def suscribir(self, tipo, manejador):
        pass


def test_si_el_bus_cae_el_comando_funciona_y_el_mensaje_queda_pendiente(tmp_path):
    c2 = crear_contenedor(tmp_path, bus=_BusRoto())
    c2.ejecutar(SincronizarFuenteConocida("msg-1", "fuente-1", "S1"))
    atributo_id = registrar(c2)  # no falla aunque el bus este caido
    assert c2.consultar(ConsultarAtributo(atributo_id)).nombre == "Cedula"
    with c2.fabrica_uow() as uow:
        assert len(uow.outbox.pendientes()) == 1
    # el broker se recupera: se reintenta el despacho
    recibidos = []
    bus_nuevo = BusEnMemoria()
    bus_nuevo.suscribir("AtributoDeNegocioRegistrado.v1", recibidos.append)
    assert DespachadorDeOutbox(c2.fabrica_uow, bus_nuevo).despachar() == 1
    assert len(recibidos) == 1


def test_concurrencia_optimista_da_conflicto(c2):
    atributo_id = registrar(c2)
    with c2.fabrica_uow() as uow_a, c2.fabrica_uow() as uow_b:
        a = uow_a.atributos.obtener(atributo_id)
        b = uow_b.atributos.obtener(atributo_id)
        a.actualizar_metadato_de_negocio("cambio de A", AHORA)
        uow_a.atributos.actualizar(a, 1)
        uow_a.confirmar()
        b.actualizar_metadato_de_negocio("cambio de B", AHORA)
        with pytest.raises(Conflicto):
            uow_b.atributos.actualizar(b, 1)  # B leyo la version 1, pero ya es la 2


def test_el_evento_fuente_registrada_de_c1_llega_por_el_bus(c2):
    c2.bus.publicar(
        MensajeDeIntegracion(
            "msg-77",
            "FuenteRegistrada.v1",
            {"evento_id": "msg-77", "fuente_id": "fuente-77", "nombre": "S4 Web", "ocurrio_en": "x"},
        )
    )
    assert registrar(c2, "Precio", "fuente-77")  # la fuente ya es conocida