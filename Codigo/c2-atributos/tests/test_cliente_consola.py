"""Prueba el cliente de consola contra un servidor REAL (uvicorn en un hilo), sin teclado."""

import socket
import threading
import time

import pytest
import uvicorn

from c2_atributos.api.app import crear_app
from c2_atributos.cliente.consola import ClienteConsola
from c2_atributos.config import Configuracion
from c2_atributos.contenedor import ContenedorC2


@pytest.fixture
def url_servidor(tmp_path):
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        puerto = s.getsockname()[1]
    config = Configuracion(ruta_bd=str(tmp_path / "c2.db"), clave_cifrado="clave-de-prueba", entorno="test")
    servidor = uvicorn.Server(
        uvicorn.Config(crear_app(ContenedorC2(config)), host="127.0.0.1", port=puerto, log_level="warning")
    )
    hilo = threading.Thread(target=servidor.run, daemon=True)
    hilo.start()
    for _ in range(100):
        if servidor.started:
            break
        time.sleep(0.05)
    yield f"http://127.0.0.1:{puerto}"
    servidor.should_exit = True
    hilo.join(timeout=5)


def correr(url, respuestas):
    cola = iter(respuestas)
    salida = []

    def entrada(_texto):
        try:
            return next(cola)
        except StopIteration:
            raise EOFError

    ClienteConsola(url, entrada=entrada, salida=salida.append).ejecutar()
    return "\n".join(salida)


def test_la_demo_guiada_recorre_el_flujo_completo(url_servidor):
    texto = correr(url_servidor, ["11", "0"])
    assert "HTTP 422" in texto  # la regex invalida se rechaza
    assert '"incompleto": true' in texto and '"incompleto": false' in texto
    assert "MetadatoDeNegocioActualizado.v1" in texto
    assert "HTTP 500" not in texto


def test_opciones_manuales_y_recuerdo_del_ultimo_atributo(url_servidor):
    texto = correr(
        url_servidor,
        [
            "1", "fuente-x", "S9",         # simular C1
            "2", "Telefono", "", "n",       # registrar atributo (fuente por defecto = la ultima)
            "6", "",                        # consultar (atributo por defecto = el ultimo)
            "0",
        ],
    )
    assert "HTTP 202" in texto and "HTTP 201" in texto
    assert '"nombre": "Telefono"' in texto


def test_sin_servicio_no_se_cae(tmp_path):
    texto = correr("http://127.0.0.1:9", ["7", "", "0"])
    assert "No se pudo conectar" in texto



def test_opciones_de_inventario_a4_y_solicitar_completar(url_servidor):
    texto = correr(
        url_servidor,
        [
            "1", "fuente-a4", "S4",                                  # simular C1: la fuente existe
            "13", "Cedula,fuente-a4,s", "",                          # A4: validar plantilla (no guarda)
            "14", "Cedula,fuente-a4,s", "Telefono,fuente-a4,n", "",  # A4: cargar inventario
            "14", "Fantasma,fuente-que-no-existe,n", "",             # A4: fuente desconocida -> se rechaza
            "2", "Correo", "", "n",                                  # registrar un atributo (queda como el ultimo)
            "12", "",                                                # solicitar completar: ese atributo esta incompleto
            "0",
        ],
    )
    assert "HTTP 201" in texto           # inventario cargado
    assert "HTTP 422" in texto           # fuente desconocida rechazada
    assert "HTTP 200" in texto and "Correo" in texto
    assert "HTTP 500" not in texto
    assert "HTTP 404" not in texto and "HTTP 405" not in texto