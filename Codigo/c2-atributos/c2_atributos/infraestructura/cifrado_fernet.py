"""Cifrado simple en reposo (Fernet = AES-128-CBC + HMAC, de la libreria `cryptography`).

Por que cifrar la definicion y la politica de calidad: el propio diccionario puede ser informacion
delicada (Bloque 7 de la entrevista: dice DONDE esta la informacion sensible). Ley 1581: medidas de seguridad.
La clave sale de una frase secreta (variable de entorno) derivada con PBKDF2.

SOLID - DIP/OCP: implementa el puerto `Cifrador`; cambiar de algoritmo no toca el dominio ni la aplicacion.
"""

import base64
import hashlib

from cryptography.fernet import Fernet, InvalidToken

from c2_atributos.aplicacion.puertos import Cifrador


class CifradorFernet(Cifrador):
    _SAL = b"hogar-andino-c2-v1"

    def __init__(self, frase_secreta: str):
        clave = hashlib.pbkdf2_hmac("sha256", frase_secreta.encode("utf-8"), self._SAL, 200_000)
        self._fernet = Fernet(base64.urlsafe_b64encode(clave))

    def cifrar(self, texto: str) -> str:
        return self._fernet.encrypt(texto.encode("utf-8")).decode("ascii")

    def descifrar(self, token: str) -> str:
        try:
            return self._fernet.decrypt(token.encode("ascii")).decode("utf-8")
        except InvalidToken as exc:
            raise ValueError("No se pudo descifrar: clave incorrecta o dato alterado.") from exc