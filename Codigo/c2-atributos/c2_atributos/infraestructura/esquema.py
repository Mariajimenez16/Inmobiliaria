"""Esquema de la base de datos propia de C2 (una BD por microservicio, diapositivas 9a/9b).

Por que SQL (SQLite aqui; PostgreSQL en produccion cambiando solo la implementacion del repositorio):
  - el agregado y el mensaje del Outbox deben guardarse en UNA transaccion ACID;
  - la regla 'un atributo por nombre y fuente' se garantiza con una restriccion UNIQUE;
  - la concurrencia optimista se resuelve con UPDATE ... WHERE version = ?.
"""

import sqlite3

DDL = """
CREATE TABLE IF NOT EXISTS atributos (
    id                         TEXT PRIMARY KEY,
    nombre                     TEXT NOT NULL COLLATE NOCASE,
    fuente_id                  TEXT NOT NULL,
    contiene_datos_personales  INTEGER NOT NULL,
    definicion                 TEXT,           -- cifrada
    politica_calidad           TEXT,           -- cifrada
    expresion_regular          TEXT,
    version                    INTEGER NOT NULL,
    creado_en                  TEXT NOT NULL,
    actualizado_en             TEXT NOT NULL,
    UNIQUE (nombre, fuente_id)
);

CREATE TABLE IF NOT EXISTS atributo_versiones (
    atributo_id  TEXT NOT NULL,
    version      INTEGER NOT NULL,
    estado       TEXT NOT NULL,                -- memento serializado (campos sensibles cifrados)
    PRIMARY KEY (atributo_id, version)
);

CREATE TABLE IF NOT EXISTS outbox (
    message_id   TEXT PRIMARY KEY,
    tipo         TEXT NOT NULL,
    payload      TEXT NOT NULL,
    creado_en    TEXT NOT NULL,
    enviado_en   TEXT
);

CREATE TABLE IF NOT EXISTS inbox (
    message_id   TEXT PRIMARY KEY,
    recibido_en  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS fuentes_locales (
    fuente_id  TEXT PRIMARY KEY,
    nombre     TEXT NOT NULL
);
"""


def inicializar_esquema(ruta_bd: str) -> None:
    conexion = sqlite3.connect(ruta_bd)
    try:
        conexion.execute("PRAGMA journal_mode=WAL")
        conexion.executescript(DDL)
        conexion.commit()
    finally:
        conexion.close()