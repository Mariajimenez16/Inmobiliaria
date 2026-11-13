from datetime import datetime, timezone

import pytest

from c2_atributos.aplicacion.comandos import RegistrarAtributoDeNegocio
from c2_atributos.aplicacion.comportamientos import ValidacionComportamiento
from c2_atributos.aplicacion.mediador import Comportamiento, Mediador
from c2_atributos.aplicacion.traductores import crear_traductor_por_defecto
from c2_atributos.building_blocks.errores import ErrorDeValidacion
from c2_atributos.dominio.atributo import AtributoNegocio

AHORA = datetime(2026, 11, 7, 12, 0, tzinfo=timezone.utc)


def test_traductor_convierte_evento_de_dominio_en_contrato_v1():
    a = AtributoNegocio.registrar("a-1", "Cedula", "fuente-1", True, AHORA)
    evento = a.extraer_eventos()[0]
    mensaje = crear_traductor_por_defecto().traducir(evento)
    assert mensaje.tipo == "AtributoDeNegocioRegistrado.v1"
    assert mensaje.message_id == evento.evento_id
    assert mensaje.payload["atributo_id"] == "a-1"
    assert mensaje.payload["contiene_datos_personales"] is True
    assert isinstance(mensaje.payload["ocurrio_en"], str)  # contratos solo con primitivos


def test_traductor_conoce_los_cuatro_eventos():
    a = AtributoNegocio.registrar("a-1", "Cedula", "fuente-1", False, AHORA)
    a.actualizar_metadato_de_negocio("Documento", AHORA)
    a.registrar_politica_de_calidad("Solo numeros", AHORA)
    a.validar_expresion_regular("^[0-9]+$", AHORA)
    t = crear_traductor_por_defecto()
    tipos = [t.traducir(e).tipo for e in a.extraer_eventos()]
    assert tipos == [
        "AtributoDeNegocioRegistrado.v1",
        "MetadatoDeNegocioActualizado.v1",
        "PoliticaDeCalidadRegistrada.v1",
        "ExpresionRegularValidada.v1",
    ]


class _Anota(Comportamiento):
    def __init__(self, nombre, bitacora):
        self.nombre, self.bitacora = nombre, bitacora

    def ejecutar(self, peticion, ctx, siguiente):
        self.bitacora.append(f"entra {self.nombre}")
        resultado = siguiente(peticion, ctx)
        self.bitacora.append(f"sale {self.nombre}")
        return resultado


class _Manejador:
    def __init__(self, bitacora):
        self.bitacora = bitacora

    def manejar(self, peticion, ctx):
        self.bitacora.append("manejador")
        return "ok"


def test_el_pipeline_respeta_el_orden():
    bitacora = []
    m = Mediador([_Anota("A", bitacora), _Anota("B", bitacora)])
    m.registrar(RegistrarAtributoDeNegocio, _Manejador(bitacora))
    assert m.enviar(RegistrarAtributoDeNegocio("Cedula", "f-1")) == "ok"
    assert bitacora == ["entra A", "entra B", "manejador", "sale B", "sale A"]


def test_validacion_corta_el_pipeline_con_400_antes_del_manejador():
    bitacora = []
    m = Mediador([ValidacionComportamiento()])
    m.registrar(RegistrarAtributoDeNegocio, _Manejador(bitacora))
    with pytest.raises(ErrorDeValidacion):
        m.enviar(RegistrarAtributoDeNegocio("   ", "f-1"))
    assert bitacora == []  # el manejador nunca se ejecuto


def test_peticion_sin_manejador_falla():
    with pytest.raises(LookupError):
        Mediador().enviar(RegistrarAtributoDeNegocio("x", "y"))