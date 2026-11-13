from datetime import datetime, timezone

import pytest

from c2_atributos.building_blocks.errores import ErrorDeNegocio
from c2_atributos.dominio.atributo import AtributoNegocio
from c2_atributos.dominio.especificaciones import evaluar_completitud

AHORA = datetime(2026, 11, 7, 12, 0, tzinfo=timezone.utc)


def crear(datos_personales=False):
    return AtributoNegocio.registrar("a-1", "Cedula", "fuente-1", datos_personales, AHORA)


def test_registrar_produce_evento_y_version_1():
    a = crear()
    eventos = a.extraer_eventos()
    assert a.version == 1
    assert [type(e).__name__ for e in eventos] == ["AtributoDeNegocioRegistrado"]
    assert a.extraer_eventos() == []  # no se publican dos veces


def test_nombre_y_fuente_son_obligatorios():
    with pytest.raises(ErrorDeNegocio):
        AtributoNegocio.registrar("a-1", "  ", "fuente-1", False, AHORA)
    with pytest.raises(ErrorDeNegocio):
        AtributoNegocio.registrar("a-1", "Cedula", "", False, AHORA)


def test_cada_cambio_crea_version_y_evento():
    a = crear()
    a.extraer_eventos()
    a.actualizar_metadato_de_negocio("Documento de identidad", AHORA)
    a.registrar_politica_de_calidad("Solo numeros, 6 a 10 digitos", AHORA)
    a.validar_expresion_regular(r"^[0-9]{6,10}$", AHORA)
    assert a.version == 4
    assert len(a.extraer_eventos()) == 3


def test_regex_invalida_se_rechaza_y_no_produce_evento():
    a = crear()
    a.extraer_eventos()
    with pytest.raises(ErrorDeNegocio):
        a.validar_expresion_regular("([", AHORA)
    assert a.expresion_regular is None
    assert a.version == 1
    assert a.extraer_eventos() == []


def test_definicion_obligatoria():
    a = crear()
    with pytest.raises(ErrorDeNegocio):
        a.actualizar_metadato_de_negocio("   ", AHORA)


def test_memento_restaura_el_estado_y_descarta_eventos():
    a = crear()
    a.extraer_eventos()
    foto = a.crear_memento()
    a.actualizar_metadato_de_negocio("Algo", AHORA)
    a.restaurar(foto)
    assert a.definicion is None
    assert a.version == 1
    assert a.extraer_eventos() == []


def test_completitud():
    a = crear(datos_personales=True)
    c = evaluar_completitud(a, tiene_responsable=False)
    assert c.incompleto and c.faltantes == ["definicion", "responsable"]
    assert c.responsable_obligatorio is True
    a.actualizar_metadato_de_negocio("Documento", AHORA)
    c = evaluar_completitud(a, tiene_responsable=True)
    assert not c.incompleto and c.faltantes == []