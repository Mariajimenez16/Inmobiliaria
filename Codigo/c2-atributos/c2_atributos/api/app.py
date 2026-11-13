"""API REST de C2 (adaptador de entrada). Rutas versionadas bajo /v1.

Cada ruta hace 3 cosas: recibe JSON -> crea un comando/consulta -> se lo entrega al mediador.
No contiene reglas de negocio.

SOLID:
  - SRP: solo traduce HTTP; los errores los formatea `problemas.py`.
  - DIP: depende del contenedor (ejecutar/consultar), no de SQLite ni del dominio.
"""

from dataclasses import asdict

from fastapi import FastAPI, Response

from c2_atributos.aplicacion.comandos import (
    ActualizarMetadatoDeNegocio,
    CargarInventario,
    RegistrarAtributoDeNegocio,
    RegistrarPoliticaDeCalidad,
    ValidarSintaxisDeExpresionRegular,
)
from c2_atributos.aplicacion.consultas import (
    ConsultarAtributo,
    ListarAtributos,
    ObtenerVersiones,
    SolicitarCompletar,
    ValidarPlantilla,
)
from c2_atributos.api.modelos import (
    ExpresionRegularIn,
    FuenteRegistradaIn,
    MetadatoNegocioIn,
    PlantillaInventarioIn,
    PoliticaCalidadIn,
    RegistrarAtributoIn,
)
from c2_atributos.api.problemas import registrar_manejadores_de_error
from c2_atributos.contenedor import ContenedorC2
from c2_atributos.simulacion.suscriptores_simulados import conectar_suscriptores_simulados


def crear_app(c2: ContenedorC2) -> FastAPI:
    app = FastAPI(title="C2 - Atributos de negocio", version="1.0.0")
    registrar_manejadores_de_error(app)

    @app.get("/salud")
    def salud():
        return {"estado": "ok", "contexto": "C2 - Atributos de negocio"}

    # ------------------------------------------------------------ comandos (V6.b)
    @app.post("/v1/atributos", status_code=201)
    def registrar_atributo(cuerpo: RegistrarAtributoIn, respuesta: Response):
        atributo_id = c2.ejecutar(
            RegistrarAtributoDeNegocio(cuerpo.nombre, cuerpo.fuente_id, cuerpo.contiene_datos_personales)
        )
        respuesta.headers["Location"] = f"/v1/atributos/{atributo_id}"
        return {"atributo_id": atributo_id}

    @app.put("/v1/atributos/{atributo_id}/metadato-negocio")
    def actualizar_metadato(atributo_id: str, cuerpo: MetadatoNegocioIn):
        c2.ejecutar(ActualizarMetadatoDeNegocio(atributo_id, cuerpo.definicion))
        return {"atributo_id": atributo_id}

    @app.put("/v1/atributos/{atributo_id}/politica-calidad")
    def registrar_politica(atributo_id: str, cuerpo: PoliticaCalidadIn):
        c2.ejecutar(RegistrarPoliticaDeCalidad(atributo_id, cuerpo.descripcion))
        return {"atributo_id": atributo_id}

    @app.post("/v1/atributos/{atributo_id}/expresion-regular/validaciones")
    def validar_expresion(atributo_id: str, cuerpo: ExpresionRegularIn):
        c2.ejecutar(ValidarSintaxisDeExpresionRegular(atributo_id, cuerpo.patron))
        return {"atributo_id": atributo_id, "sintaxis_valida": True}

    # ------------------------------------------------------------ consultas
    @app.get("/v1/atributos/{atributo_id}")
    def consultar_atributo(atributo_id: str):
        return asdict(c2.consultar(ConsultarAtributo(atributo_id)))

    @app.get("/v1/atributos")
    def listar_atributos(fuente_id: str | None = None):
        return [asdict(f) for f in c2.consultar(ListarAtributos(fuente_id))]

    @app.get("/v1/atributos/{atributo_id}/versiones")
    def obtener_versiones(atributo_id: str):
        return [asdict(v) for v in c2.consultar(ObtenerVersiones(atributo_id))]

    @app.get("/v1/atributos/{atributo_id}/solicitud-de-completar")
    def solicitar_completar(atributo_id: str):
        return asdict(c2.consultar(SolicitarCompletar(atributo_id)))

    # ------------------------------------------------------------ adaptador A4 (inventario manual)
    @app.post("/v1/integracion/inventario/validaciones")
    def validar_plantilla(cuerpo: PlantillaInventarioIn):
        return asdict(c2.consultar(ValidarPlantilla(cuerpo.filas)))

    @app.post("/v1/integracion/inventario", status_code=201)
    def cargar_inventario(cuerpo: PlantillaInventarioIn):
        return asdict(c2.ejecutar(CargarInventario(cuerpo.filas)))

    # ------------------------------------------------------------ integracion (entrada desde C1, simulada)
    @app.post("/v1/integracion/fuentes-registradas", status_code=202)
    def recibir_fuente_registrada(cuerpo: FuenteRegistradaIn):
        procesado = c2.recibir_fuente_registrada(cuerpo.model_dump())
        return {"procesado": procesado, "duplicado": not procesado}

    # ------------------------------------------------------------ simulacion de otros contextos
    if c2.config.simulacion_habilitada:
        suscriptores = conectar_suscriptores_simulados(c2.bus)

        @app.post("/v1/simulacion/c4/responsables/{atributo_id}")
        def simular_c4_asigna_responsable(atributo_id: str):
            c2.consultar(ConsultarAtributo(atributo_id))  # 404 si no existe
            c2.responsabilidad.asignar(atributo_id)
            return {"atributo_id": atributo_id, "responsable_asignado": True}

        @app.get("/v1/simulacion/eventos-publicados")
        def eventos_recibidos_por_los_contextos_simulados():
            return {nombre: s.recibidos for nombre, s in suscriptores.items()}

    return app