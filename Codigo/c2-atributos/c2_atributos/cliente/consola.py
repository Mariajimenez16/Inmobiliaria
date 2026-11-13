"""Cliente de consola de C2. Es un cliente DELGADO: no tiene reglas de negocio, solo llama a la API.

Usa solo la libreria estandar (urllib), asi se puede ejecutar sin instalar nada extra.

    python -m c2_atributos.cliente            (usa http://localhost:8012 o la variable C2_URL)

SOLID - SRP: cada opcion del menu es un metodo; DIP: la entrada/salida se inyectan (se prueba sin teclado).
"""

import json
import os
import urllib.error
import urllib.request
import uuid
from typing import Any, Callable


class ClienteConsola:
    def __init__(
        self,
        url_base: str,
        entrada: Callable[[str], str] = input,
        salida: Callable[[str], None] = print,
    ):
        self._url = url_base.rstrip("/")
        self._entrada = entrada
        self._salida = salida
        self._ultimo_atributo: str | None = None
        self._ultima_fuente: str | None = None
        self._acciones: list[tuple[str, Callable[[], None]]] = [
            ("Simular C1: publicar 'Fuente registrada'", self.simular_fuente),
            ("Registrar atributo de negocio", self.registrar_atributo),
            ("Actualizar metadato de negocio (definicion)", self.actualizar_metadato),
            ("Registrar politica de calidad", self.registrar_politica),
            ("Validar sintaxis de expresion regular", self.validar_regex),
            ("Consultar atributo", self.consultar_atributo),
            ("Listar atributos", self.listar_atributos),
            ("Ver versiones de un atributo", self.ver_versiones),
            ("Simular C4: asignar responsable a un atributo", self.simular_responsable),
            ("Ver eventos recibidos por C4 y C6 (simulados)", self.ver_eventos),
            ("Demo guiada completa", self.demo),
            ("Solicitar completar un atributo (que le falta)", self.solicitar_completar),
            ("A4: validar plantilla de inventario (no guarda nada)", self.validar_plantilla),
            ("A4: cargar inventario manual", self.cargar_inventario),
        ]

    # ------------------------------------------------------------------ bucle principal
    def ejecutar(self) -> None:
        self._salida("=== C2 - Atributos de negocio (Hogar Andino) ===")
        while True:
            for i, (texto, _) in enumerate(self._acciones, start=1):
                self._salida(f"  {i}. {texto}")
            self._salida("  0. Salir")
            try:
                opcion = self._entrada("Opcion: ").strip()
            except EOFError:
                return
            if opcion == "0":
                return
            if not opcion.isdigit() or not (1 <= int(opcion) <= len(self._acciones)):
                self._salida("Opcion no valida.")
                continue
            try:
                self._acciones[int(opcion) - 1][1]()
            except ConnectionError as exc:
                self._salida(f"No se pudo conectar con el servicio: {exc}")
            except EOFError:
                return

    # ------------------------------------------------------------------ opciones
    def simular_fuente(self) -> None:
        fuente_id = self._pedir("ID de la fuente", self._ultima_fuente or "fuente-s1")
        nombre = self._pedir("Nombre de la fuente", "S1 Administracion de arrendamientos")
        estado, cuerpo = self._llamar(
            "POST",
            "/v1/integracion/fuentes-registradas",
            {"evento_id": str(uuid.uuid4()), "fuente_id": fuente_id, "nombre": nombre},
        )
        if estado < 300:
            self._ultima_fuente = fuente_id
        self._mostrar(estado, cuerpo)

    def registrar_atributo(self) -> None:
        nombre = self._pedir("Nombre del atributo", "Cedula")
        fuente_id = self._pedir("ID de la fuente", self._ultima_fuente or "fuente-s1")
        personales = self._pedir("Contiene datos personales? (s/n)", "s").lower().startswith("s")
        estado, cuerpo = self._llamar(
            "POST",
            "/v1/atributos",
            {"nombre": nombre, "fuente_id": fuente_id, "contiene_datos_personales": personales},
        )
        if estado == 201:
            self._ultimo_atributo = cuerpo["atributo_id"]
        self._mostrar(estado, cuerpo)

    def actualizar_metadato(self) -> None:
        atributo_id = self._pedir_atributo()
        definicion = self._pedir("Definicion de negocio", "Documento de identidad del titular")
        self._mostrar(*self._llamar("PUT", f"/v1/atributos/{atributo_id}/metadato-negocio", {"definicion": definicion}))

    def registrar_politica(self) -> None:
        atributo_id = self._pedir_atributo()
        descripcion = self._pedir("Politica de calidad", "Solo numeros, de 6 a 10 digitos")
        self._mostrar(*self._llamar("PUT", f"/v1/atributos/{atributo_id}/politica-calidad", {"descripcion": descripcion}))

    def validar_regex(self) -> None:
        atributo_id = self._pedir_atributo()
        patron = self._pedir("Expresion regular", r"^[0-9]{6,10}$")
        self._mostrar(
            *self._llamar("POST", f"/v1/atributos/{atributo_id}/expresion-regular/validaciones", {"patron": patron})
        )

    def consultar_atributo(self) -> None:
        self._mostrar(*self._llamar("GET", f"/v1/atributos/{self._pedir_atributo()}"))

    def listar_atributos(self) -> None:
        fuente = self._pedir("Filtrar por fuente (Enter = todas)", "")
        ruta = "/v1/atributos" + (f"?fuente_id={urllib.request.quote(fuente)}" if fuente else "")
        self._mostrar(*self._llamar("GET", ruta))

    def ver_versiones(self) -> None:
        self._mostrar(*self._llamar("GET", f"/v1/atributos/{self._pedir_atributo()}/versiones"))

    def simular_responsable(self) -> None:
        self._mostrar(*self._llamar("POST", f"/v1/simulacion/c4/responsables/{self._pedir_atributo()}"))

    def ver_eventos(self) -> None:
        self._mostrar(*self._llamar("GET", "/v1/simulacion/eventos-publicados"))

    def solicitar_completar(self) -> None:
        self._mostrar(*self._llamar("GET", f"/v1/atributos/{self._pedir_atributo()}/solicitud-de-completar"))

    def validar_plantilla(self) -> None:
        self._mostrar(*self._llamar("POST", "/v1/integracion/inventario/validaciones", {"filas": self._pedir_filas()}))

    def cargar_inventario(self) -> None:
        self._mostrar(*self._llamar("POST", "/v1/integracion/inventario", {"filas": self._pedir_filas()}))

    def demo(self) -> None:
        """Recorre el flujo completo sin pedir datos: es la que se usa en la sustentacion."""
        sufijo = uuid.uuid4().hex[:6]
        fuente_id = f"fuente-demo-{sufijo}"
        self._salida("\n[1] C1 publica 'Fuente registrada' (simulado)")
        self._mostrar(*self._llamar("POST", "/v1/integracion/fuentes-registradas",
                                    {"evento_id": str(uuid.uuid4()), "fuente_id": fuente_id, "nombre": "S1 (demo)"}))
        self._salida("\n[2] Registrar atributo 'Cedula' (datos personales)")
        estado, cuerpo = self._llamar("POST", "/v1/atributos",
                                      {"nombre": "Cedula", "fuente_id": fuente_id, "contiene_datos_personales": True})
        self._mostrar(estado, cuerpo)
        if estado != 201:
            return
        atributo_id = self._ultimo_atributo = cuerpo["atributo_id"]
        self._salida("\n[3] Consultar: debe salir INCOMPLETO (falta definicion y responsable)")
        self._mostrar(*self._llamar("GET", f"/v1/atributos/{atributo_id}"))
        self._salida("\n[4] Expresion regular INVALIDA -> 422, sin cambios")
        self._mostrar(*self._llamar("POST", f"/v1/atributos/{atributo_id}/expresion-regular/validaciones", {"patron": "(["}))
        self._salida("\n[5] Definicion, politica y expresion regular validas")
        self._mostrar(*self._llamar("PUT", f"/v1/atributos/{atributo_id}/metadato-negocio",
                                    {"definicion": "Documento de identidad del titular"}))
        self._mostrar(*self._llamar("PUT", f"/v1/atributos/{atributo_id}/politica-calidad",
                                    {"descripcion": "Solo numeros, de 6 a 10 digitos"}))
        self._mostrar(*self._llamar("POST", f"/v1/atributos/{atributo_id}/expresion-regular/validaciones",
                                    {"patron": r"^[0-9]{6,10}$"}))
        self._salida("\n[6] C4 (simulado) asigna responsable")
        self._mostrar(*self._llamar("POST", f"/v1/simulacion/c4/responsables/{atributo_id}"))
        self._salida("\n[7] Consultar: ya NO es incompleto")
        self._mostrar(*self._llamar("GET", f"/v1/atributos/{atributo_id}"))
        self._salida("\n[8] Historial de versiones (Memento)")
        self._mostrar(*self._llamar("GET", f"/v1/atributos/{atributo_id}/versiones"))
        self._salida("\n[9] Eventos que recibieron C4 y C6 (simulados)")
        self._mostrar(*self._llamar("GET", "/v1/simulacion/eventos-publicados"))

    # ------------------------------------------------------------------ utilidades
    def _pedir(self, etiqueta: str, por_defecto: str) -> str:
        valor = self._entrada(f"{etiqueta} [{por_defecto}]: ").strip()
        return valor or por_defecto

    def _pedir_atributo(self) -> str:
        return self._pedir("ID del atributo", self._ultimo_atributo or "")

    def _pedir_filas(self) -> list[dict]:
        """Lee la plantilla de inventario (A4): una fila por linea como  nombre,fuente_id,s/n  (linea vacia = terminar)."""
        self._salida("Escriba una fila por linea como: nombre,fuente_id,datos_personales(s/n)   (linea vacia para terminar)")
        filas: list[dict] = []
        while True:
            linea = self._entrada("  > ").strip()
            if not linea:
                return filas
            partes = [p.strip() for p in linea.split(",")]
            if len(partes) != 3:
                self._salida("  Formato: nombre,fuente_id,s/n")
                continue
            filas.append(
                {"nombre": partes[0], "fuente_id": partes[1], "contiene_datos_personales": partes[2].lower().startswith("s")}
            )

    def _mostrar(self, estado: int, cuerpo: Any) -> None:
        self._salida(f"HTTP {estado}")
        if cuerpo is not None:
            self._salida(json.dumps(cuerpo, indent=2, ensure_ascii=False))

    def _llamar(self, metodo: str, ruta: str, cuerpo: dict | None = None) -> tuple[int, Any]:
        datos = None if cuerpo is None else json.dumps(cuerpo).encode("utf-8")
        peticion = urllib.request.Request(
            self._url + ruta, data=datos, method=metodo, headers={"Content-Type": "application/json"}
        )
        try:
            with urllib.request.urlopen(peticion, timeout=10) as respuesta:
                texto = respuesta.read().decode("utf-8")
                return respuesta.status, (json.loads(texto) if texto else None)
        except urllib.error.HTTPError as exc:  # el servicio respondio con 4xx/5xx (ProblemDetails)
            texto = exc.read().decode("utf-8")
            try:
                return exc.code, json.loads(texto)
            except json.JSONDecodeError:
                return exc.code, {"detail": texto}
        except urllib.error.URLError as exc:
            raise ConnectionError(exc.reason) from exc


def main() -> None:
    ClienteConsola(os.getenv("C2_URL", "http://localhost:8012")).ejecutar()


if __name__ == "__main__":
    main()