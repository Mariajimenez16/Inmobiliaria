"""Pruebas de las dos piezas agregadas para la fidelidad al diseno:
- contrato 3.4 `solicitarCompletar`
- adaptador A4 `validarPlantilla` / `cargarInventario`
"""

import pytest
from fastapi.testclient import TestClient

from c2_atributos.api.app import crear_app
from c2_atributos.config import Configuracion
from c2_atributos.contenedor import ContenedorC2


@pytest.fixture
def cliente(tmp_path):
    config = Configuracion(ruta_bd=str(tmp_path / "c2.db"), clave_cifrado="clave-de-prueba", entorno="test")
    return TestClient(crear_app(ContenedorC2(config)), raise_server_exceptions=False)


@pytest.fixture
def con_fuente(cliente):
    cliente.post(
        "/v1/integracion/fuentes-registradas",
        json={"evento_id": "ev-1", "fuente_id": "f1", "nombre": "S1"},
    )
    return cliente


def fila(nombre="Cedula", fuente="f1", personales=True):
    return {"nombre": nombre, "fuente_id": fuente, "contiene_datos_personales": personales}


# ---------------------------------------------------------------- solicitarCompletar
def test_solicitar_completar_indica_lo_que_falta(con_fuente):
    atributo_id = con_fuente.post("/v1/atributos", json={"nombre": "Cedula", "fuente_id": "f1"}).json()["atributo_id"]
    r = con_fuente.get(f"/v1/atributos/{atributo_id}/solicitud-de-completar")
    assert r.status_code == 200
    cuerpo = r.json()
    assert cuerpo["incompleto"] is True
    assert cuerpo["faltantes"] == ["definicion", "responsable"]
    assert "definicion" in cuerpo["mensaje"]


def test_solicitar_completar_cuando_ya_esta_completo(con_fuente):
    atributo_id = con_fuente.post("/v1/atributos", json={"nombre": "Cedula", "fuente_id": "f1"}).json()["atributo_id"]
    con_fuente.put(f"/v1/atributos/{atributo_id}/metadato-negocio", json={"definicion": "Documento de identidad"})
    con_fuente.post(f"/v1/simulacion/c4/responsables/{atributo_id}")
    cuerpo = con_fuente.get(f"/v1/atributos/{atributo_id}/solicitud-de-completar").json()
    assert cuerpo["incompleto"] is False and cuerpo["faltantes"] == []


def test_solicitar_completar_404(cliente):
    assert cliente.get("/v1/atributos/no-existe/solicitud-de-completar").status_code == 404


# ---------------------------------------------------------------- A4: validarPlantilla
def test_validar_plantilla_correcta_no_guarda_nada(con_fuente):
    r = con_fuente.post("/v1/integracion/inventario/validaciones", json={"filas": [fila("Cedula"), fila("Telefono")]})
    assert r.status_code == 200
    assert r.json() == {"valida": True, "filas_leidas": 2, "errores": []}
    assert con_fuente.get("/v1/atributos").json() == []


def test_validar_plantilla_indica_las_filas_con_problemas(con_fuente):
    filas = [fila("Cedula"), fila("Telefono", fuente="desconocida"), {"nombre": "Email", "fuente_id": "f1", "cedula": "123"}]
    cuerpo = con_fuente.post("/v1/integracion/inventario/validaciones", json={"filas": filas}).json()
    assert cuerpo["valida"] is False
    por_fila = {e["fila"]: e["motivo"] for e in cuerpo["errores"]}
    assert set(por_fila) == {2, 3}
    assert "no esta registrada en C1" in por_fila[2]
    assert "columna no permitida 'cedula'" in por_fila[3]


def test_validar_plantilla_vacia_no_es_valida(con_fuente):
    cuerpo = con_fuente.post("/v1/integracion/inventario/validaciones", json={"filas": []}).json()
    assert cuerpo["valida"] is False


def test_validar_detecta_repetidos_en_el_archivo_y_en_la_base(con_fuente):
    con_fuente.post("/v1/atributos", json={"nombre": "Cedula", "fuente_id": "f1"})
    cuerpo = con_fuente.post(
        "/v1/integracion/inventario/validaciones", json={"filas": [fila("Cedula"), fila("Email"), fila("Email")]}
    ).json()
    por_fila = {e["fila"]: e["motivo"] for e in cuerpo["errores"]}
    assert "ya existe" in por_fila[1] and "repetido" in por_fila[3] and 2 not in por_fila


def test_400_si_no_vienen_filas(con_fuente):
    assert con_fuente.post("/v1/integracion/inventario/validaciones", json={}).status_code == 400


# ---------------------------------------------------------------- A4: cargarInventario
def test_cargar_inventario_crea_los_atributos(con_fuente):
    r = con_fuente.post("/v1/integracion/inventario", json={"filas": [fila("Cedula"), fila("Telefono", personales=False)]})
    assert r.status_code == 201
    assert len(r.json()["atributos_creados"]) == 2
    nombres = {a["nombre"] for a in con_fuente.get("/v1/atributos?fuente_id=f1").json()}
    assert nombres == {"Cedula", "Telefono"}


def test_cargar_inventario_es_todo_o_nada(con_fuente):
    r = con_fuente.post("/v1/integracion/inventario", json={"filas": [fila("Cedula"), fila("Otro", fuente="no-existe")]})
    assert r.status_code == 422
    assert "fila 2" in r.json()["detail"]
    assert con_fuente.get("/v1/atributos").json() == []  # la fila 1 tampoco se guardo