import pytest
from fastapi.testclient import TestClient

from c2_atributos.api.app import crear_app
from c2_atributos.config import Configuracion
from c2_atributos.contenedor import ContenedorC2


@pytest.fixture
def cliente(tmp_path):
    config = Configuracion(ruta_bd=str(tmp_path / "c2.db"), clave_cifrado="clave-de-prueba", entorno="test")
    app = crear_app(ContenedorC2(config))
    return TestClient(app, raise_server_exceptions=False)


def publicar_fuente(cliente, fuente_id="fuente-1", evento_id="ev-1"):
    return cliente.post(
        "/v1/integracion/fuentes-registradas",
        json={"evento_id": evento_id, "fuente_id": fuente_id, "nombre": "S1"},
    )


def registrar(cliente, nombre="Cedula", fuente="fuente-1", personales=True):
    return cliente.post(
        "/v1/atributos", json={"nombre": nombre, "fuente_id": fuente, "contiene_datos_personales": personales}
    )


def test_salud(cliente):
    assert cliente.get("/salud").json()["estado"] == "ok"


def test_registrar_devuelve_201_con_location(cliente):
    assert publicar_fuente(cliente).status_code == 202
    r = registrar(cliente)
    assert r.status_code == 201
    atributo_id = r.json()["atributo_id"]
    assert r.headers["location"] == f"/v1/atributos/{atributo_id}"


def test_400_cuerpo_mal_formado(cliente):
    r = cliente.post("/v1/atributos", json={"nombre": "Cedula"})  # falta fuente_id
    assert r.status_code == 400
    assert r.headers["content-type"].startswith("application/problem+json")
    assert r.json()["status"] == 400 and "fuente_id" in r.json()["detail"]


def test_400_campo_en_blanco(cliente):
    assert registrar(cliente, nombre="   ").status_code == 400


def test_404_atributo_inexistente(cliente):
    r = cliente.get("/v1/atributos/no-existe")
    assert r.status_code == 404 and r.json()["title"] == "Recurso no encontrado"
    assert r.json()["instance"] == "/v1/atributos/no-existe"


def test_409_duplicado(cliente):
    publicar_fuente(cliente)
    assert registrar(cliente).status_code == 201
    assert registrar(cliente).status_code == 409


def test_422_fuente_desconocida_y_regex_invalida(cliente):
    assert registrar(cliente, fuente="desconocida").status_code == 422
    publicar_fuente(cliente)
    atributo_id = registrar(cliente).json()["atributo_id"]
    r = cliente.post(f"/v1/atributos/{atributo_id}/expresion-regular/validaciones", json={"patron": "(["})
    assert r.status_code == 422
    assert cliente.get(f"/v1/atributos/{atributo_id}").json()["version"] == 1


def test_flujo_completo_por_http(cliente):
    publicar_fuente(cliente)
    atributo_id = registrar(cliente).json()["atributo_id"]
    ficha = cliente.get(f"/v1/atributos/{atributo_id}").json()
    assert ficha["incompleto"] is True and ficha["faltantes"] == ["definicion", "responsable"]

    assert cliente.put(f"/v1/atributos/{atributo_id}/metadato-negocio", json={"definicion": "Documento"}).status_code == 200
    assert cliente.put(f"/v1/atributos/{atributo_id}/politica-calidad", json={"descripcion": "Solo numeros"}).status_code == 200
    r = cliente.post(f"/v1/atributos/{atributo_id}/expresion-regular/validaciones", json={"patron": "^[0-9]+$"})
    assert r.json()["sintaxis_valida"] is True
    assert cliente.post(f"/v1/simulacion/c4/responsables/{atributo_id}").status_code == 200

    ficha = cliente.get(f"/v1/atributos/{atributo_id}").json()
    assert ficha["incompleto"] is False and ficha["version"] == 4
    assert [v["version"] for v in cliente.get(f"/v1/atributos/{atributo_id}/versiones").json()] == [1, 2, 3, 4]
    assert len(cliente.get("/v1/atributos", params={"fuente_id": "fuente-1"}).json()) == 1


def test_eventos_llegan_a_los_contextos_simulados(cliente):
    publicar_fuente(cliente)
    atributo_id = registrar(cliente).json()["atributo_id"]
    cliente.put(f"/v1/atributos/{atributo_id}/metadato-negocio", json={"definicion": "Documento"})
    eventos = cliente.get("/v1/simulacion/eventos-publicados").json()
    assert [e["tipo"] for e in eventos["C6"]] == [
        "AtributoDeNegocioRegistrado.v1",
        "MetadatoDeNegocioActualizado.v1",
    ]
    assert [e["tipo"] for e in eventos["C4"]] == ["MetadatoDeNegocioActualizado.v1"]


def test_evento_duplicado_de_c1_es_idempotente(cliente):
    assert publicar_fuente(cliente).json() == {"procesado": True, "duplicado": False}
    assert publicar_fuente(cliente).json() == {"procesado": False, "duplicado": True}