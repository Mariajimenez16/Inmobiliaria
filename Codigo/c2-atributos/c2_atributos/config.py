"""Configuracion del microservicio (variables de entorno, patron 12-factor: nada de secretos en el codigo)."""

import os
from dataclasses import dataclass

# Solo para desarrollo local. En cualquier otro entorno la clave DEBE venir de C2_CLAVE_CIFRADO.
_CLAVE_SOLO_DESARROLLO = "clave-solo-para-desarrollo-no-usar-en-produccion"


@dataclass(frozen=True)
class Configuracion:
    ruta_bd: str = "c2_atributos.db"
    clave_cifrado: str | None = None
    entorno: str = "dev"  # dev | test | prod
    ttl_cache_segundos: int = 60
    simulacion_habilitada: bool = True

    @classmethod
    def desde_entorno(cls) -> "Configuracion":
        entorno = os.getenv("C2_ENTORNO", "dev")
        return cls(
            ruta_bd=os.getenv("C2_RUTA_BD", "c2_atributos.db"),
            clave_cifrado=os.getenv("C2_CLAVE_CIFRADO"),
            entorno=entorno,
            ttl_cache_segundos=int(os.getenv("C2_TTL_CACHE", "60")),
            simulacion_habilitada=os.getenv("C2_SIMULACION", "1" if entorno != "prod" else "0") == "1",
        )

    def clave_efectiva(self) -> str:
        if self.clave_cifrado:
            return self.clave_cifrado
        if self.entorno == "dev":
            return _CLAVE_SOLO_DESARROLLO
        raise RuntimeError("Falta la variable de entorno C2_CLAVE_CIFRADO.")