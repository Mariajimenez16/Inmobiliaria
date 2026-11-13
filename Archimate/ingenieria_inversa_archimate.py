#!/usr/bin/env python3
"""Ingenieria inversa: codigo (C1 en C#, C2 en Python)  ->  modelo ArchiMate importable en Archi.

Genera en la carpeta de salida:
  modelo_c1_c2.xml        Open Exchange Format (Archi: File > Import > Open Exchange XML Model...)
                          Trae elementos, relaciones, propiedades Y una vista por dominio ya dibujada.
  csv/elements.csv, relations.csv, properties.csv
                          Alternativa (Archi: File > Import > CSV Data Into Selected Model...). No trae vista.
  reporte_ingenieria_inversa.md
                          Que se detecto, con que regla, y (si se pasa --diseno) contraste con el diseno.

Solo usa la biblioteca estandar de Python 3.9+. No ejecuta el codigo analizado: solo lo lee (ast / regex).

Uso:
  python ingenieria_inversa_archimate.py --raiz "Proyecto_Final.zip" --salida salida_archimate
  python ingenieria_inversa_archimate.py --raiz "C:/ruta/Proyecto Final" --diseno "Diseno_completo.archimate"

SOLID (para la sustentacion):
  SRP  cada clase hace una cosa: AnalizadorPython/AnalizadorCSharp leen codigo; Modelo guarda;
       EscritorExchange/EscritorCsv escriben; Reporte compara.
  OCP  un lenguaje nuevo = otra clase con el metodo analizar(); el Modelo y los escritores no cambian.
  LSP  los dos analizadores son intercambiables para main().
  DIP  los analizadores solo conocen la abstraccion Modelo, no los formatos de salida.
"""

from __future__ import annotations

import argparse
import ast
import csv
import re
import sys
import tempfile
import unicodedata
import uuid
import zipfile
from dataclasses import dataclass, field
from pathlib import Path
from xml.sax.saxutils import escape, quoteattr

# =====================================================================================
# CONFIGURACION (lo unico que se toca para otro proyecto)
# =====================================================================================
DOMINIOS = [
    {"codigo": "C2", "nombre": "Atributos de negocio", "ruta": "c2-atributos", "lenguaje": "python",
     "paquete": "c2_atributos"},
    {"codigo": "C1", "nombre": "Fuentes y metadatos técnicos", "ruta": "c1-fuentes", "lenguaje": "csharp"},
]

# Nombres de los componentes externos tal como estan en el diseno (Entrega 3).
CONTEXTOS = {
    "C1": "C1 · Fuentes y metadatos técnicos", "C2": "C2 · Atributos de negocio",
    "C3": "C3 · Clasificación y validación jurídica", "C4": "C4 · Responsables, autoridad y decisiones",
    "C5": "C5 · Catálogo de términos", "C6": "C6 · Diccionario y reporte", "C7": "C7 · Control de acceso por rol",
}
ADAPTADORES_EXTERNOS = {  # adaptador de la capa anticorrupcion -> sistema externo que aisla
    "A1": "Motores relacionales de S1, S3 y S4 (externo)",
    "A2": "S2 · CRM comercial (externo)",
    "A3": "Proveedores de software de S1 a S4 (externo)",
    "A4": "Hojas, carpetas, correo y WhatsApp (externo)",
}
# Convenciones del codigo (por carpeta / sufijo). Si cambian en el proyecto, se cambian aqui.
PY = {"mod_comandos": {"comandos"}, "mod_consultas": {"consultas"}, "mod_eventos": {"eventos"},
      "mod_puertos": {"puertos"}, "prefijo_manejador": "Manejador", "sub_dominio": "dominio",
      "sub_api": "api", "sub_adaptadores": {"infraestructura", "simulacion"},
      "bases_reglas": {"IReglaDeNegocio", "Especificacion"}, "bases_entidad": {"Entidad", "RaizDeAgregado"},
      "ignorar_sub": {"tests", "__pycache__", ".venv", "building_blocks"}}
CS = {"ignorar_dir": {"bin", "obj", "tests", ".venv", ".git"}, "ignorar_tipos": {"Guarda"}}

# El codigo no lleva tildes en los identificadores; esto solo las repone en los NOMBRES del modelo.
ACENTOS = {"politica": "política", "expresion": "expresión", "tecnico": "técnico", "tecnicos": "técnicos",
           "autorizacion": "autorización", "validacion": "validación", "clasificacion": "clasificación",
           "importacion": "importación", "descripcion": "descripción", "informacion": "información",
           "comparacion": "comparación", "revision": "revisión", "estructura": "estructura",
           "diccionario": "diccionario", "catalogo": "catálogo", "relacion": "relación"}

# Relaciones usadas (mismas que el diseno de la Entrega 3; ver reporte).
R_COMPONENTE_SERVICIO = "Assignment"

NS_ID = uuid.UUID("6f1c2f4e-0000-4000-8000-0000000a4b1d")  # ids estables: reimportar no duplica


# =====================================================================================
# MODELO NEUTRO (lo que producen los analizadores y consumen los escritores)
# =====================================================================================
@dataclass
class Elemento:
    id: str
    tipo: str
    nombre: str
    doc: str = ""
    props: dict = field(default_factory=dict)
    dominio: str = ""


@dataclass
class Relacion:
    id: str
    tipo: str
    origen: str
    destino: str
    nombre: str = ""
    acceso: str = ""


class Modelo:
    def __init__(self) -> None:
        self.elementos: dict[str, Elemento] = {}
        self.relaciones: dict[tuple, Relacion] = {}
        self.avisos: list[str] = []

    @staticmethod
    def _id(clave: str) -> str:
        return "id-" + uuid.uuid5(NS_ID, clave).hex

    def elemento(self, tipo: str, nombre: str, dominio: str, doc: str = "", props: dict | None = None,
                 clave: str | None = None) -> Elemento:
        eid = self._id(f"{dominio}|{tipo}|{clave or nombre}")
        e = self.elementos.get(eid)
        if e is None:
            e = self.elementos[eid] = Elemento(eid, tipo, nombre, doc, {}, dominio)
        if doc and not e.doc:
            e.doc = doc
        for k, v in (props or {}).items():
            if v not in (None, "", []):
                e.props[k] = str(v)
        return e

    def relacion(self, tipo: str, a: Elemento, b: Elemento, nombre: str = "", acceso: str = "") -> None:
        if a.id == b.id:
            return
        clave = (tipo, a.id, b.id)
        if clave not in self.relaciones:
            self.relaciones[clave] = Relacion(self._id("rel|" + "|".join(clave)), tipo, a.id, b.id, nombre, acceso)

    def externo(self, codigo_ctx: str | None = None, nombre: str | None = None) -> Elemento:
        n = nombre or CONTEXTOS[codigo_ctx]
        return self.elemento("ApplicationComponent", n, "compartido",
                             props={"Origen": "Contexto/sistema externo al código analizado"})


# =====================================================================================
# UTILIDADES
# =====================================================================================
def humanizar(ident: str, quitar: tuple = ()) -> str:
    for s in quitar:
        if ident.endswith(s) and len(ident) > len(s):
            ident = ident[: -len(s)]
        if ident.startswith(s) and len(ident) > len(s) and s == "Manejador":
            ident = ident[len(s):]
    ident = ident.replace("_", " ")
    # corta en minuscula->Mayuscula y en 'AAcceso' (mayusculas seguidas de Mayuscula+minuscula)
    palabras = re.sub(r"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ", ident).split()
    texto = " ".join(p if re.fullmatch(r"[A-Z]\d", p) else ACENTOS.get(p.lower(), p.lower()) for p in palabras)
    return texto[:1].upper() + texto[1:]


def normalizar(texto: str) -> str:
    t = unicodedata.normalize("NFD", texto.lower())
    t = "".join(c for c in t if unicodedata.category(c) != "Mn")
    t = re.sub(r"[^a-z0-9 ]", " ", t)
    return " ".join(p for p in t.split() if p not in {"de", "del", "la", "el", "los", "las"})


def primera_linea(doc: str | None) -> str:
    return (doc or "").strip().split("\n\n")[0].replace("\n", " ").strip()


def codigos_externos(texto: str, propio: str) -> list[str]:
    return sorted({c for c in re.findall(r"C[1-7](?![0-9a-z])", texto) if c != propio})


def codigos_adaptador(texto: str) -> list[str]:
    return sorted(set(re.findall(r"A[1-4](?![0-9a-z])", texto)))


# =====================================================================================
# ANALIZADOR PYTHON  (C2)
# =====================================================================================
@dataclass
class ClasePy:
    nombre: str
    sub: str
    modulo: str
    archivo: str
    nodo: ast.ClassDef
    bases: list
    doc: str
    abstracta: bool
    dataclass: bool

    def campos(self) -> list[str]:
        return [f"{n.target.id}: {ast.unparse(n.annotation)}" for n in self.nodo.body
                if isinstance(n, ast.AnnAssign) and isinstance(n.target, ast.Name)]

    def metodos(self) -> list[ast.FunctionDef]:
        return [n for n in self.nodo.body if isinstance(n, ast.FunctionDef)]


def firma(fn: ast.FunctionDef, sin_self: bool = True) -> str:
    args = [a for a in fn.args.args if not (sin_self and a.arg in ("self", "cls"))]
    txt = ", ".join(a.arg + (f": {ast.unparse(a.annotation)}" if a.annotation else "") for a in args)
    ret = f" -> {ast.unparse(fn.returns)}" if fn.returns else ""
    return f"{fn.name}({txt}){ret}"


def nombres_en(nodo: ast.AST) -> set[str]:
    out = set()
    for n in ast.walk(nodo):
        if isinstance(n, ast.Name):
            out.add(n.id)
        elif isinstance(n, ast.Constant) and isinstance(n.value, str):
            out.update(re.findall(r"[A-Za-z_]\w*", n.value))
    return out


class AnalizadorPython:
    def __init__(self, modelo: Modelo, raiz: Path, cfg: dict):
        self.m, self.raiz, self.cfg = modelo, raiz, cfg
        self.cod, self.dom = cfg["codigo"], cfg["codigo"]
        self.clases: dict[str, ClasePy] = {}
        self.rutas: list[tuple] = []  # (metodo, ruta, status, fn, archivo)
        self.suscripciones: list[tuple] = []  # (nombre_clase_evento_v1, codigo_ctx)

    # ---------------------------------------------------------------- lectura
    def _leer(self) -> Path:
        paquete = self.raiz / self.cfg["paquete"]
        for f in sorted(paquete.rglob("*.py")):
            rel = f.relative_to(paquete)
            if set(rel.parts) & PY["ignorar_sub"] and rel.parts[0] != "dominio":
                continue
            if f.name == "__init__.py":
                continue
            sub = rel.parts[0] if len(rel.parts) > 1 else ""
            try:
                arbol = ast.parse(f.read_text(encoding="utf-8"))
            except SyntaxError as exc:
                self.m.avisos.append(f"No se pudo leer {f.name}: {exc}")
                continue
            for n in arbol.body:
                if isinstance(n, ast.ClassDef):
                    bases = [ast.unparse(b) for b in n.bases]
                    decs = [ast.unparse(d) for d in n.decorator_list]
                    self.clases[n.name] = ClasePy(
                        n.name, sub, f.stem, str(f.relative_to(self.raiz)).replace("\\", "/"), n, bases,
                        ast.get_docstring(n) or "",
                        any(b.startswith(("ABC", "Protocol")) for b in bases),
                        any(d.startswith("dataclass") for d in decs))
            self._rutas_y_suscripciones(arbol, f)
        return paquete

    def _rutas_y_suscripciones(self, arbol: ast.AST, f: Path) -> None:
        for fn in ast.walk(arbol):
            if isinstance(fn, ast.FunctionDef):
                for d in fn.decorator_list:
                    if (isinstance(d, ast.Call) and isinstance(d.func, ast.Attribute)
                            and d.func.attr in {"get", "post", "put", "delete", "patch"}
                            and d.args and isinstance(d.args[0], ast.Constant)):
                        status = next((str(k.value.value) for k in d.keywords
                                       if k.arg == "status_code" and isinstance(k.value, ast.Constant)), "200")
                        self.rutas.append((d.func.attr.upper(), d.args[0].value, status, fn, f.name))
                # eventos suscritos: bus.suscribir(XV1.TIPO, variable.recibir)
                vars_ctx = {}
                for n in ast.walk(fn):
                    if (isinstance(n, ast.Assign) and isinstance(n.value, ast.Call) and n.value.args
                            and isinstance(n.value.args[0], ast.Constant) and isinstance(n.targets[0], ast.Name)):
                        vars_ctx[n.targets[0].id] = str(n.value.args[0].value)
                for n in ast.walk(fn):
                    if (isinstance(n, ast.Call) and isinstance(n.func, ast.Attribute) and n.func.attr == "suscribir"
                            and len(n.args) == 2 and isinstance(n.args[0], ast.Attribute)
                            and isinstance(n.args[0].value, ast.Name) and isinstance(n.args[1], ast.Attribute)
                            and isinstance(n.args[1].value, ast.Name)):
                        ctx = vars_ctx.get(n.args[1].value.id, "")
                        if re.fullmatch(r"C[1-7]", ctx):
                            self.suscripciones.append((n.args[0].value.id, ctx))

    # ---------------------------------------------------------------- analisis
    def analizar(self) -> None:
        paquete = self._leer()
        m, dom = self.m, self.dom
        nombre_ctx = f"{self.cod} · {self.cfg['nombre']}"
        raiz = m.elemento("ApplicationComponent", nombre_ctx, dom, props={
            "Lenguaje": "Python", "Carpeta": self.cfg["ruta"], "Arquitectura": "Microservicio (capas + CQRS + Outbox)"
            if self.cfg["codigo"] == "C2" else ""})
        capas: dict[str, Elemento] = {}
        for sub in sorted({c.sub for c in self.clases.values() if c.sub and c.sub not in PY["ignorar_sub"]}):
            capas[sub] = m.elemento("ApplicationComponent", f"{self.cod} / {sub}", dom,
                                    props={"Carpeta": f"{self.cfg['paquete']}/{sub}", "Rol": "Módulo (capa)"})
            m.relacion("Composition", raiz, capas[sub])

        def clases_de(mods): return [c for c in self.clases.values() if c.modulo in mods]

        # --- comandos, consultas, eventos
        comandos = {c.nombre: self._proceso(c, "Comando", raiz) for c in clases_de(PY["mod_comandos"])}
        consultas = {c.nombre: self._proceso(c, "Consulta", raiz) for c in clases_de(PY["mod_consultas"])}
        eventos = {}
        for c in clases_de(PY["mod_eventos"]):
            eventos[c.nombre] = m.elemento("ApplicationEvent", "Evento: " + humanizar(c.nombre), dom,
                                           doc=primera_linea(c.doc), props={
                                               "Clase": c.nombre, "Archivo": c.archivo,
                                               "Campos": "; ".join(c.campos())}, clave=c.nombre)
        # --- puertos
        puertos = {}
        for c in clases_de(PY["mod_puertos"]):
            if c.abstracta:
                puertos[c.nombre] = m.elemento("ApplicationInterface", "Puerto: " + re.sub(r"^Puerto ", "", humanizar(c.nombre)), dom,
                                               doc=primera_linea(c.doc), props={
                                                   "Clase": c.nombre, "Archivo": c.archivo,
                                                   "Operaciones": "; ".join(firma(f) for f in c.metodos()
                                                                            if not f.name.startswith("__"))},
                                               clave=c.nombre)
                if "aplicacion" in capas:
                    m.relacion("Composition", capas["aplicacion"], puertos[c.nombre])
        # --- dominio: entidades, objetos de valor, reglas
        entidades = self._dominio(capas, raiz)
        # --- manejadores (servicios de aplicacion)
        manejadores = self._manejadores(comandos, consultas, eventos, puertos, entidades, capas)
        # --- adaptadores / anticorrupcion
        self._adaptadores(puertos, capas, raiz, comandos)
        # --- contratos (rutas HTTP)
        self._contratos(manejadores, comandos, consultas, raiz, capas)
        # --- eventos publicados a otros contextos (suscripciones simuladas)
        for nombre_v1, ctx in self.suscripciones:
            base = re.sub(r"V\d+$", "", nombre_v1)
            ev = eventos.get(base)
            if ev:
                dest = m.externo(ctx)
                m.relacion("Flow", raiz, dest, nombre=f"publica: {ev.nombre}")

    # ---------------------------------------------------------------- piezas
    def _proceso(self, c: ClasePy, prefijo: str, raiz: Elemento) -> Elemento:
        validar = next((f for f in c.metodos() if f.name == "validar"), None)
        e = self.m.elemento("ApplicationProcess", f"{prefijo}: {humanizar(c.nombre)}", self.dom,
                            doc=primera_linea(c.doc), props={
                                "Clase": c.nombre, "Archivo": c.archivo, "Entrada": "; ".join(c.campos()),
                                "Validación de formato": "sí (validar())" if validar else "no"}, clave=c.nombre)
        for ctx in codigos_externos(c.doc + " " + c.nombre, self.cod):  # p.ej. 'Fuente registrada de C1'
            if prefijo == "Comando":
                self.m.relacion("Flow", self.m.externo(ctx), raiz, nombre=f"evento consumido: {humanizar(c.nombre)}")
        return e

    def _dominio(self, capas: dict, raiz: Elemento) -> dict[str, Elemento]:
        m, dom, ent = self.m, self.dom, {}
        sub = PY["sub_dominio"]
        for c in self.clases.values():
            if c.sub != sub or c.modulo in PY["mod_eventos"]:
                continue
            if set(c.bases) & PY["bases_reglas"] or any(b.startswith("Especificacion") for b in c.bases):
                e = m.elemento("ApplicationService", "Regla de dominio: " + humanizar(c.nombre), dom,
                               doc=primera_linea(c.doc), props={
                                   "Clase": c.nombre, "Archivo": c.archivo, "Rol": "Regla de negocio / Especificación",
                                   "Operaciones": "; ".join(firma(f) for f in c.metodos() if not f.name.startswith("_"))},
                               clave=c.nombre)
                if sub in capas:
                    m.relacion(R_COMPONENTE_SERVICIO, capas[sub], e)
                continue
            es_raiz = any(b.split("[")[0] in PY["bases_entidad"] for b in c.bases)
            if not (es_raiz or c.dataclass):
                continue
            rol = "Raíz de agregado" if es_raiz else ("Memento" if "Memento" in c.nombre else "Objeto de valor")
            ops = "; ".join(firma(f) for f in c.metodos() if not f.name.startswith("_"))
            e = m.elemento("DataObject", humanizar(c.nombre), dom, doc=primera_linea(c.doc), props={
                "Clase": c.nombre, "Archivo": c.archivo, "Rol DDD": rol, "Atributos": "; ".join(c.campos()),
                "Comportamiento": ops}, clave=c.nombre)
            ent[c.nombre] = e
        # Composicion raiz -> objeto que usa como tipo (memento, VO)
        for nombre, e in ent.items():
            c = self.clases[nombre]
            for otro in nombres_en(c.nodo):
                if otro in ent and otro != nombre and e.props.get("Rol DDD") == "Raíz de agregado":
                    m.relacion("Association", e, ent[otro], nombre="usa")
        return ent

    def _clase_y_bases(self, nombre: str) -> list[ClasePy]:
        out, pend = [], [nombre]
        while pend:
            c = self.clases.get(pend.pop())
            if c and c not in out:
                out.append(c)
                pend += [b.split("[")[0] for b in c.bases]
        return out

    def _manejadores(self, comandos, consultas, eventos, puertos, entidades, capas) -> dict:
        m, dom = self.m, self.dom
        cuerpos_dominio = {}  # metodo -> eventos que crea
        for c in self.clases.values():
            if c.sub == PY["sub_dominio"] and c.modulo not in PY["mod_eventos"]:
                for f in c.metodos():
                    evs = {n.func.id for n in ast.walk(f) if isinstance(n, ast.Call) and isinstance(n.func, ast.Name)
                           and n.func.id in eventos}
                    if evs:
                        cuerpos_dominio.setdefault(f.name, set()).update(evs)
        uow = next((c for c in self.clases.values() if "UnidadDeTrabajo" in c.nombre and c.abstracta), None)
        sub_puertos_uow = {}
        if uow:
            for n in uow.nodo.body:
                if isinstance(n, ast.AnnAssign) and isinstance(n.target, ast.Name):
                    sub_puertos_uow[n.target.id] = ast.unparse(n.annotation)
        resultado = {}
        for c in list(self.clases.values()):
            if not c.nombre.startswith(PY["prefijo_manejador"]) or c.sub != "aplicacion":
                continue
            cadena = self._clase_y_bases(c.nombre)
            msg, tipo_msg, retorno = None, None, ""
            for k in cadena:
                for f in k.metodos():
                    if f.name in ("manejar", "aplicar"):
                        for a in f.args.args:
                            an = ast.unparse(a.annotation) if a.annotation else ""
                            if an in comandos or an in consultas:
                                msg, tipo_msg = an, ("comandos" if an in comandos else "consultas")
                                if f.name == "manejar" and f.returns:
                                    retorno = ast.unparse(f.returns)
                    if msg and f.name == "manejar" and not retorno and f.returns:
                        retorno = ast.unparse(f.returns)
                if msg:
                    break
            if not msg:
                self.m.avisos.append(f"{c.nombre}: clase base/auxiliar sin comando asociado; no se modela como servicio.")
                continue
            # retorno: buscar en la cadena (el 'manejar' puede estar en la clase base)
            for k in cadena:
                for f in k.metodos():
                    if f.name == "manejar" and f.returns and not retorno:
                        retorno = ast.unparse(f.returns)
            proceso = (comandos if tipo_msg == "comandos" else consultas)[msg]
            deps = []
            for k in cadena:
                for f in k.metodos():
                    if f.name == "__init__":
                        deps += [(a.arg, ast.unparse(a.annotation)) for a in f.args.args if a.annotation]
            svc = m.elemento("ApplicationService", "Manejador: " + humanizar(c.nombre, ("Manejador",)), dom,
                             doc=primera_linea(c.doc), props={
                                 "Clase": c.nombre, "Archivo": c.archivo, "Maneja": proceso.nombre,
                                 "Entrada": proceso.props.get("Entrada", ""), "Salida": retorno,
                                 "Dependencias (constructor)": "; ".join(f"{n}: {t}" for n, t in deps),
                                 "Patrón": "Template Method" if len(cadena) > 1 and cadena[1].abstracta else ""},
                             clave=c.nombre)
            resultado[msg] = svc
            if "aplicacion" in capas:
                m.relacion(R_COMPONENTE_SERVICIO, capas["aplicacion"], svc)
            m.relacion("Realization", proceso, svc)
            # dependencias fisicas -> relacion Serving (puerto sirve al manejador)
            usados, via_uow = set(), set()
            for _, t in deps:
                if t in puertos:
                    usados.add(t)
                elif t in resultado.values() or t in self.clases and t.startswith(PY["prefijo_manejador"]):
                    pend = next((x for x in resultado.values() if x.props.get("Clase") == t), None)
                    if pend:
                        m.relacion("Serving", pend, svc)
                    else:
                        usados.add("__pend__" + t)
            for k in cadena:
                for n in ast.walk(k.nodo):
                    es_uow = (isinstance(n, ast.Attribute) and ((isinstance(n.value, ast.Attribute) and n.value.attr == "uow")
                                                                or (isinstance(n.value, ast.Name) and n.value.id == "uow")))
                    if es_uow:
                        p = sub_puertos_uow.get(n.attr)
                        if p in puertos:
                            via_uow.add(p)
                    if isinstance(n, ast.Attribute) and n.attr == "uow" and uow:
                        usados.add(uow.nombre)
            for u in sorted(usados):
                if u in puertos:
                    m.relacion("Serving", puertos[u], svc)
            if via_uow and uow and uow.nombre in puertos:  # el manejador usa la UoW; la UoW agrupa los repositorios
                for u in sorted(via_uow):
                    m.relacion("Aggregation", puertos[uow.nombre], puertos[u])
                svc.props["Puertos usados vía unidad de trabajo"] = "; ".join(sorted(via_uow))
            # acceso al agregado y eventos que dispara
            refs = set()
            for k in cadena:
                refs |= nombres_en(k.nodo)
            for nombre_ent, e in entidades.items():
                if nombre_ent in refs and e.props.get("Rol DDD") == "Raíz de agregado" and tipo_msg == "comandos":
                    m.relacion("Access", proceso, e, acceso="ReadWrite")
            if tipo_msg == "comandos":
                for k in cadena:
                    for n in ast.walk(k.nodo):
                        if isinstance(n, ast.Call) and isinstance(n.func, ast.Attribute):
                            recibe = ast.unparse(n.func.value)
                            if any(x in recibe for x in ("uow", "ctx", "self._", "outbox", "inbox")):
                                continue
                            for ev in cuerpos_dominio.get(n.func.attr, ()):
                                m.relacion("Triggering", proceso, eventos[ev])
        # segunda pasada: manejadores que dependen de otros manejadores definidos despues
        for msg, svc in resultado.items():
            for c in self._clase_y_bases(svc.props["Clase"]):
                for f in c.metodos():
                    if f.name == "__init__":
                        for a in f.args.args:
                            t = ast.unparse(a.annotation) if a.annotation else ""
                            dep = next((x for x in resultado.values() if x.props.get("Clase") == t), None)
                            if dep:
                                m.relacion("Serving", dep, svc)
        return resultado

    def _adaptadores(self, puertos: dict, capas: dict, raiz: Elemento, comandos: dict) -> None:
        m, dom = self.m, self.dom
        for c in self.clases.values():
            if c.nombre.startswith(PY["prefijo_manejador"]):
                continue  # los manejadores ya son servicios de aplicacion
            es_adapt_sub = c.sub in PY["sub_adaptadores"] and not c.nombre.startswith("Suscriptor")
            implementa = [b for b in c.bases if b in puertos]
            codigos_a = codigos_adaptador(c.nombre + " " + primera_linea(c.doc)) if c.sub in ("aplicacion", *PY["sub_adaptadores"]) else []
            if not (es_adapt_sub and (implementa or c.sub == "simulacion")) and not codigos_a:
                continue
            if c.modulo in PY["mod_puertos"] | PY["mod_comandos"] | PY["mod_consultas"]:
                continue
            simulado = c.sub == "simulacion"
            e = m.elemento("ApplicationComponent", "Adaptador: " + humanizar(c.nombre), dom, doc=primera_linea(c.doc),
                           props={"Clase": c.nombre, "Archivo": c.archivo, "Rol": "Adaptador (capa anticorrupción)"
                                  if codigos_a else "Adaptador de infraestructura",
                                  "Simulado": "sí (simula otro contexto)" if simulado else "no",
                                  "Implementa": "; ".join(implementa), "Operaciones": "; ".join(
                                      firma(f) for f in c.metodos() if not f.name.startswith("_"))}, clave=c.nombre)
            if c.sub in capas:
                m.relacion("Composition", capas[c.sub], e)
            for b in implementa:
                m.relacion("Serving", e, puertos[b], nombre="implementa")
            for a in codigos_a:
                ext = m.externo(nombre=ADAPTADORES_EXTERNOS[a])
                m.relacion("Flow", ext, e, nombre=f"entrada {a}")
                m.relacion("Association", e, raiz, nombre=f"aísla {a}")
            for ctx in codigos_externos(c.nombre, self.cod):
                m.relacion("Association", e, m.externo(ctx), nombre="simula a")

    def _contratos(self, manejadores: dict, comandos: dict, consultas: dict, raiz: Elemento, capas: dict) -> None:
        m, dom = self.m, self.dom
        modelos = {c.nombre: c for c in self.clases.values() if c.sub == PY["sub_api"]}
        for metodo, ruta, status, fn, archivo in self.rutas:
            pars = []
            for a in fn.args.args:
                an = ast.unparse(a.annotation) if a.annotation else ""
                if an in modelos:
                    pars.append(f"{a.arg}: {an}{{{'; '.join(modelos[an].campos())}}}")
                elif an != "Response":
                    pars.append(f"{a.arg}: {an}" if an else a.arg)
            usados = [n.func.id for n in ast.walk(fn) if isinstance(n, ast.Call) and isinstance(n.func, ast.Name)
                      and (n.func.id in comandos or n.func.id in consultas)]
            svc = manejadores.get(usados[0]) if usados else None
            retorno = svc.props.get("Salida", "") if svc else ""
            devuelve = next((ast.unparse(n.value) for n in ast.walk(fn) if isinstance(n, ast.Return) and n.value), "")
            salida = f"HTTP {status}; " + (retorno or devuelve[:90])
            excs, reglas = set(), ""
            if svc:
                for k in self._clase_y_bases(svc.props["Clase"]):
                    for n in ast.walk(k.nodo):
                        if isinstance(n, ast.Raise) and isinstance(n.exc, ast.Call) and isinstance(n.exc.func, ast.Name):
                            excs.add(n.exc.func.id)
                reglas = svc.doc
            nombre = f"Contrato: {metodo} {ruta}"
            c = m.elemento("Contract", nombre, dom, doc="Contrato derivado del decorador HTTP de la ruta (Contract First inverso).",
                           props={"Servicio especificado": svc.nombre if svc else "(ruta sin comando/consulta)",
                                  "Operaciones": firma(fn), "Entrada": "; ".join(pars), "Salida": salida,
                                  "Reglas de negocio": reglas, "Excepciones": "; ".join(sorted(excs)),
                                  "Consumidores": "Cliente HTTP / contexto que consume la ruta", "Archivo": archivo},
                           clave=nombre)
            if "api" in capas:
                m.relacion("Association", capas["api"], c)
            if svc:
                m.relacion("Association", c, svc, nombre="especifica")
            if "/integracion/" in ruta or "/fuentes-registradas" in ruta:
                for ctx in codigos_externos(svc.doc + " " + (usados[0] if usados else ""), self.cod) if svc else []:
                    m.relacion("Flow", m.externo(ctx), raiz, nombre=f"{metodo} {ruta}")


# =====================================================================================
# ANALIZADOR C#  (C1)
# =====================================================================================
@dataclass
class TipoCs:
    clase: str
    nombre: str
    params: str
    bases: list
    cuerpo: str
    archivo: str
    proyecto: str
    carpeta: str
    doc: str
    estatica: bool


def _balanceado(texto: str, i: int, ab: str, ce: str) -> int:
    prof = 0
    for j in range(i, len(texto)):
        if texto[j] == ab:
            prof += 1
        elif texto[j] == ce:
            prof -= 1
            if prof == 0:
                return j
    return len(texto) - 1


def _partir(texto: str) -> list[str]:
    out, prof, act = [], 0, ""
    for ch in texto:
        if ch in "<([":
            prof += 1
        elif ch in ">)]":
            prof -= 1
        if ch == "," and prof == 0:
            out.append(act.strip())
            act = ""
        else:
            act += ch
    if act.strip():
        out.append(act.strip())
    return out


DECL = re.compile(r"^[ \t]*public\s+((?:sealed\s+|abstract\s+|static\s+|readonly\s+|partial\s+)*)"
                  r"(record\s+struct|record|class|interface|enum)\s+(\w+)", re.M)


class AnalizadorCSharp:
    def __init__(self, modelo: Modelo, raiz: Path, cfg: dict):
        self.m, self.raiz, self.cfg = modelo, raiz, cfg
        self.cod = self.dom = cfg["codigo"]
        self.tipos: dict[str, TipoCs] = {}
        self.proyectos: dict[str, list[str]] = {}

    def _leer(self) -> None:
        src = self.raiz / "src"
        for csproj in sorted(src.rglob("*.csproj")):
            if set(csproj.parts) & CS["ignorar_dir"]:
                continue
            refs = re.findall(r'ProjectReference\s+Include="[^"]*?([\w\.]+)\.csproj"', csproj.read_text(encoding="utf-8"))
            self.proyectos[csproj.stem] = refs
        for f in sorted(src.rglob("*.cs")):
            if set(f.parts) & CS["ignorar_dir"]:
                continue
            texto = f.read_text(encoding="utf-8-sig")
            rel = f.relative_to(src)
            proyecto, carpeta = rel.parts[0], "/".join(rel.parts[1:-1])
            for mt in DECL.finditer(texto):
                clase, nombre = mt.group(2), mt.group(3)
                i = mt.end()
                resto = texto[i:]
                params, j = "", 0
                gen = re.match(r"\s*<[^>]*>", resto)
                if gen:
                    j = gen.end()
                if resto[j:].lstrip().startswith("(") and "record" in clase:
                    ini = i + j + resto[j:].index("(")
                    fin = _balanceado(texto, ini, "(", ")")
                    params = texto[ini + 1: fin]
                    i2 = fin + 1
                else:
                    i2 = i + j
                m_fin = re.search(r"[{;]", texto[i2:])
                cab = texto[i2: i2 + m_fin.start()] if m_fin else ""
                cab = re.split(r"\bwhere\b", cab)[0]  # 'where T : IAlgo' NO es una lista de bases
                bases = _partir(cab.split(":", 1)[1]) if ":" in cab else []
                bases = [re.sub(r"\(.*", "", b).strip() for b in bases]
                cuerpo = ""
                if m_fin and texto[i2 + m_fin.start()] == "{":
                    ini = i2 + m_fin.start()
                    cuerpo = texto[ini + 1: _balanceado(texto, ini, "{", "}")]
                previo = texto[:mt.start()].rstrip().split("\n")
                doc = " ".join(l.strip().lstrip("/").strip() for l in reversed(
                    [x for x in previo[-4:] if x.strip().startswith("//")][-3:]))
                self.tipos[nombre] = TipoCs(clase, nombre, params, bases, cuerpo,
                                            str(f.relative_to(self.raiz)).replace("\\", "/"), proyecto, carpeta, doc,
                                            "static" in mt.group(1))

    def _params(self, txt: str) -> list[str]:
        return [re.sub(r"\s*=.*", "", p).strip() for p in _partir(txt)]

    def _firmas_interfaz(self, t: TipoCs) -> str:
        sig = re.findall(r"^\s*([\w<>\[\],\?\. ]+?\s+\w+\s*\([^;{]*\))\s*;", t.cuerpo, re.M)
        props = re.findall(r"^\s*([\w<>\?\.]+\s+\w+)\s*\{\s*get;", t.cuerpo, re.M)
        return "; ".join(" ".join(s.split()) for s in sig + props)

    def analizar(self) -> None:
        self._leer()
        m, dom = self.m, self.dom
        raiz = m.elemento("ApplicationComponent", f"{self.cod} · {self.cfg['nombre']}", dom, props={
            "Lenguaje": "C# / .NET", "Carpeta": self.cfg["ruta"], "Arquitectura": "Hexagonal (puertos y adaptadores)"})
        comps = {}
        for p, refs in self.proyectos.items():
            comps[p] = m.elemento("ApplicationComponent", f"{self.cod} / {p}", dom, props={
                "Proyecto": p + ".csproj", "Rol": "Módulo (proyecto .NET)"})
            m.relacion("Composition", raiz, comps[p])
        for p, refs in self.proyectos.items():
            for r in refs:
                if r in comps:
                    m.relacion("Serving", comps[r], comps[p], nombre="referencia de proyecto")
        T = self.tipos
        comandos = {n: self._proceso(t, "Comando", "Comando") for n, t in T.items()
                    if n.endswith("Comando") and any(b.startswith("IComando") for b in t.bases)}
        consultas = {n: self._proceso(t, "Consulta", "Consulta") for n, t in T.items()
                     if n.endswith("Consulta") and any(b.startswith("IConsulta") for b in t.bases)}
        eventos = {n: m.elemento("ApplicationEvent", "Evento: " + humanizar(n), dom, doc=t.doc, props={
            "Clase": n, "Archivo": t.archivo, "Campos": "; ".join(self._params(t.params))}, clave=n)
            for n, t in T.items() if any(b.startswith("EventoDeDominio") for b in t.bases) and n != "EventoDeDominio"}
        puertos = {}
        for n, t in T.items():
            if t.clase == "interface" and t.carpeta.endswith("Salida"):
                puertos[n] = m.elemento("ApplicationInterface", "Puerto: " + humanizar(n[1:] if n.startswith("I") else n),
                                        dom, doc=t.doc, props={"Clase": n, "Archivo": t.archivo,
                                                               "Operaciones": self._firmas_interfaz(t)}, clave=n)
                if t.proyecto in comps:
                    m.relacion("Composition", comps[t.proyecto], puertos[n])
        entidades = self._dominio(comps)
        metodos_evento = self._metodos_que_levantan(eventos)
        manejadores = self._manejadores(comandos, consultas, eventos, puertos, entidades, comps, metodos_evento)
        self._adaptadores(puertos, comps, raiz)
        self._contratos(manejadores, comandos, consultas, comps, raiz)

    def _proceso(self, t: TipoCs, prefijo: str, sufijo: str) -> Elemento:
        e = self.m.elemento("ApplicationProcess", f"{prefijo}: {humanizar(t.nombre, (sufijo,))}", self.dom, doc=t.doc,
                            props={"Clase": t.nombre, "Archivo": t.archivo, "Entrada": "; ".join(self._params(t.params)),
                                   "Permiso requerido": "sí" if any("IRequierePermiso" in b for b in t.bases) else "no"},
                            clave=t.nombre)
        return e

    def _dominio(self, comps: dict) -> dict:
        m, dom, out = self.m, self.dom, {}
        for n, t in self.tipos.items():
            if t.carpeta != "Dominio" or n in CS["ignorar_tipos"]:
                continue
            if any(b.startswith("EventoDeDominio") for b in t.bases) or n in ("EventoDeDominio", "RaizDeAgregado"):
                continue
            if any("Exception" in b or "Error" in b for b in t.bases) or t.estatica:
                continue
            es_raiz = any(b.startswith("RaizDeAgregado") for b in t.bases)
            ops = re.findall(r"public\s+(?:static\s+)?[\w<>\?\[\], ]+\s+(\w+)\s*\(", t.cuerpo)
            if t.clase == "enum":
                rol, atributos = "Enumeración", t.cuerpo.strip().replace("\n", " ")
            else:
                rol = "Raíz de agregado" if es_raiz else ("Servicio de dominio" if not t.params and not re.search(
                    r"\{\s*get;", t.cuerpo) and ops else "Entidad / objeto de valor")
                atributos = "; ".join(self._params(t.params) + [" ".join(x.split()) for x in re.findall(
                    r"public\s+([\w<>\?\.]+\s+\w+)\s*\{\s*get", t.cuerpo)])
            if rol == "Servicio de dominio":
                e = m.elemento("ApplicationService", "Regla de dominio: " + humanizar(n), dom, doc=t.doc, props={
                    "Clase": n, "Archivo": t.archivo, "Rol": rol, "Operaciones": "; ".join(sorted(set(ops)))}, clave=n)
                if t.proyecto in comps:
                    m.relacion(R_COMPONENTE_SERVICIO, comps[t.proyecto], e)
                continue
            out[n] = m.elemento("DataObject", humanizar(n), dom, doc=t.doc, props={
                "Clase": n, "Archivo": t.archivo, "Rol DDD": rol, "Atributos": atributos,
                "Comportamiento": "; ".join(sorted(set(ops)))}, clave=n)
        return out

    def _metodos_que_levantan(self, eventos: dict) -> dict:
        res: dict[str, set] = {}
        for t in self.tipos.values():
            if t.carpeta != "Dominio":
                continue
            for mt in re.finditer(r"(?:public|internal)\s+(?:static\s+)?[\w<>\?\[\], ]+\s+(\w+)\s*\([^)]*\)\s*(?:\{|=>)", t.cuerpo):
                ini = t.cuerpo.find("{", mt.end() - 1) if t.cuerpo[mt.end() - 1] == "{" else mt.end()
                fin = _balanceado(t.cuerpo, ini, "{", "}") if t.cuerpo[mt.end() - 1] == "{" else t.cuerpo.find(";", ini)
                cuerpo = t.cuerpo[ini:fin]
                for ev in re.findall(r"new\s+(\w+)\s*\(", cuerpo):
                    if ev in eventos:
                        res.setdefault(mt.group(1), set()).add(ev)
        return res

    def _manejadores(self, comandos, consultas, eventos, puertos, entidades, comps, metodos_evento) -> dict:
        m, dom, res = self.m, self.dom, {}
        uow = self.tipos.get("IUnidadDeTrabajo")
        sub_uow = dict((p, t) for t, p in re.findall(r"^\s*(I\w+)\s+(\w+)\s*\{\s*get;", uow.cuerpo, re.M)) if uow else {}
        for n, t in self.tipos.items():
            mt = next((re.match(r"IManejador\w*<\s*(\w+)\s*,\s*(.+)>", b) for b in t.bases
                       if b.startswith("IManejador")), None)
            if not mt or t.clase != "class":
                continue
            msg, retorno = mt.group(1), mt.group(2).strip()
            es_cmd = msg in comandos
            proceso = (comandos if es_cmd else consultas).get(msg)
            if not proceso:
                continue
            ctor = re.search(r"public\s+" + n + r"\s*\(([^)]*)\)", t.cuerpo)
            deps = self._params(ctor.group(1)) if ctor else []
            svc = m.elemento("ApplicationService", "Manejador: " + humanizar(n, ("Manejador",)), dom, doc=t.doc,
                             props={"Clase": n, "Archivo": t.archivo, "Maneja": proceso.nombre,
                                    "Entrada": proceso.props.get("Entrada", ""), "Salida": retorno,
                                    "Dependencias (constructor)": "; ".join(deps)}, clave=n)
            res[msg] = svc
            if t.proyecto in comps:
                m.relacion(R_COMPONENTE_SERVICIO, comps[t.proyecto], svc)
            m.relacion("Realization", proceso, svc)
            usados, via_uow = set(), set()
            for d in deps:
                tipo = d.split()[0]
                if tipo in puertos:
                    usados.add(tipo)
            for prop in re.findall(r"\b_?uow\.(\w+)", t.cuerpo):
                if sub_uow.get(prop) in puertos:
                    via_uow.add(sub_uow[prop])
            for u in sorted(usados):
                m.relacion("Serving", puertos[u], svc)
            if via_uow and "IUnidadDeTrabajo" in puertos:
                for u in sorted(via_uow):
                    m.relacion("Aggregation", puertos["IUnidadDeTrabajo"], puertos[u])
                svc.props["Puertos usados vía unidad de trabajo"] = "; ".join(sorted(via_uow))
            if es_cmd:
                for ne, e in entidades.items():
                    if e.props.get("Rol DDD") == "Raíz de agregado" and re.search(r"\b" + ne + r"\b", t.cuerpo):
                        m.relacion("Access", proceso, e, acceso="ReadWrite")
                for recibe, metodo in re.findall(r"\b([\w\.]+)\.(\w+)\(", t.cuerpo):
                    if recibe.startswith("_"):
                        continue
                    for ev in metodos_evento.get(metodo, ()):
                        m.relacion("Triggering", proceso, eventos[ev])
        for msg, svc in res.items():  # manejador que depende de otro manejador
            t = self.tipos[svc.props["Clase"]]
            for d in svc.props.get("Dependencias (constructor)", "").split(";"):
                tipo = d.strip().split(" ")[0]
                dep = next((x for x in res.values() if x.props.get("Clase") == tipo), None)
                if dep:
                    m.relacion("Serving", dep, svc)
        return res

    def _adaptadores(self, puertos: dict, comps: dict, raiz: Elemento) -> None:
        m, dom = self.m, self.dom
        for n, t in self.tipos.items():
            if t.clase != "class" or not t.proyecto.endswith(("Salida",)) or t.estatica:
                continue
            implementa = [b for b in t.bases if b in puertos]
            codigos_a = codigos_adaptador(n + " " + t.doc)
            if not implementa and not codigos_a:
                continue
            simulado = t.carpeta == "Simulacion"
            e = m.elemento("ApplicationComponent", "Adaptador: " + humanizar(n), dom, doc=t.doc, props={
                "Clase": n, "Archivo": t.archivo, "Implementa": "; ".join(implementa),
                "Rol": "Adaptador (capa anticorrupción)" if codigos_a else "Adaptador de infraestructura",
                "Simulado": "sí (simula otro sistema/contexto)" if simulado else "no"}, clave=n)
            m.relacion("Composition", comps[t.proyecto], e)
            for b in implementa:
                m.relacion("Serving", e, puertos[b], nombre="implementa")
            for a in codigos_a:
                ext = m.externo(nombre=ADAPTADORES_EXTERNOS[a])
                m.relacion("Flow", ext, e, nombre=f"entrada {a}")
                m.relacion("Association", e, raiz, nombre=f"aísla {a}")
            for ctx in codigos_externos(n, self.cod):
                m.relacion("Association", e, m.externo(ctx), nombre="simula a")

    def _contratos(self, manejadores: dict, comandos: dict, consultas: dict, comps: dict, raiz: Elemento) -> None:
        m, dom = self.m, self.dom
        for n, t in self.tipos.items():
            if not (t.estatica and "Endpoints" in n):
                continue
            grupos = dict(re.findall(r"var\s+(\w+)\s*=\s*\w+\.MapGroup\(\"([^\"]*)\"\)", t.cuerpo))
            for var, verbo, ruta, handler in re.findall(
                    r"(\w+)\.Map(Get|Post|Put|Delete|Patch)\(\"([^\"]*)\"\s*,\s*(\w+)\)", t.cuerpo):
                prefijo = grupos.get(var, "")
                ruta_total = (prefijo + ("" if ruta == "/" else ruta)) or "/"
                mh = re.search(r"static\s+(?:async\s+)?[\w<>\?\[\], ]+\s+" + handler + r"\s*\(([^)]*)\)", t.cuerpo)
                firma_h = f"{handler}({' '.join(mh.group(1).split())})" if mh else handler
                cuerpo = ""
                if mh:
                    ini = t.cuerpo.find("{", mh.end())
                    cuerpo = t.cuerpo[ini: _balanceado(t.cuerpo, ini, "{", "}")]
                usados = [x for x in re.findall(r"new\s+(\w+(?:Comando|Consulta))\b", cuerpo)
                          if x in comandos or x in consultas]
                svc = manejadores.get(usados[0]) if usados else None
                estados = sorted(set(re.findall(r"Results\.(Created|Ok|NoContent|Accepted)", cuerpo)))
                excs = set()
                if svc:
                    th = self.tipos[svc.props["Clase"]]
                    excs |= set(re.findall(r"throw\s+new\s+(\w+)", th.cuerpo))
                nombre = f"Contrato: {verbo.upper()} {ruta_total}"
                c = m.elemento("Contract", nombre, dom, doc="Contrato derivado de la ruta HTTP (Contract First inverso).",
                               props={"Servicio especificado": svc.nombre if svc else "(ruta sin comando/consulta)",
                                      "Operaciones": firma_h, "Entrada": svc.props.get("Entrada", "") if svc else "",
                                      "Salida": ("HTTP " + "/".join(estados) + "; " if estados else "") + (
                                          svc.props.get("Salida", "") if svc else ""),
                                      "Reglas de negocio": svc.doc if svc else "", "Excepciones": "; ".join(sorted(excs)),
                                      "Consumidores": "Cliente HTTP / contexto que consume la ruta", "Archivo": t.archivo},
                               clave=nombre)
                m.relacion("Association", comps[t.proyecto], c)
                if svc:
                    m.relacion("Association", c, svc, nombre="especifica")


# =====================================================================================
# ESCRITORES
# =====================================================================================
TIPOS_REL_CSV = {t: t + "Relationship" for t in
                 ("Composition", "Aggregation", "Assignment", "Realization", "Serving", "Access", "Triggering",
                  "Flow", "Association", "Influence", "Specialization")}


class EscritorCsv:
    def escribir(self, m: Modelo, carpeta: Path) -> None:
        carpeta.mkdir(parents=True, exist_ok=True)
        with open(carpeta / "elements.csv", "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f, quoting=csv.QUOTE_ALL)
            w.writerow(["ID", "Type", "Name", "Documentation"])
            for e in m.elementos.values():
                w.writerow([e.id, e.tipo, e.nombre, e.doc])
        with open(carpeta / "relations.csv", "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f, quoting=csv.QUOTE_ALL)
            w.writerow(["ID", "Type", "Name", "Documentation", "Source", "Target"])
            for r in m.relaciones.values():
                silencio = r.nombre in ETIQUETAS_SILENCIOSAS
                w.writerow([r.id, TIPOS_REL_CSV[r.tipo], "" if silencio else r.nombre, r.nombre if silencio else "", r.origen, r.destino])
        with open(carpeta / "properties.csv", "w", newline="", encoding="utf-8") as f:
            w = csv.writer(f, quoting=csv.QUOTE_ALL)
            w.writerow(["ID", "Key", "Value"])
            for e in m.elementos.values():
                for k, v in e.props.items():
                    w.writerow([e.id, k, v])


# Etiquetas que se omiten en los diagramas (el tipo de relacion ya lo dice); el texto pasa a la documentacion.
ETIQUETAS_SILENCIOSAS = {"implementa", "especifica", "referencia de proyecto", "usa"}


@dataclass
class VistaDef:
    nombre: str
    columnas: list  # lista de columnas; cada columna = lista de ids de elementos
    relaciones: list


def _nivel_por_camino(ids: list, rels: list) -> list:
    """Capas por camino mas largo: toda relacion apunta a una capa posterior (sin ciclos infinitos)."""
    nivel = {i: 0 for i in ids}
    for _ in range(len(ids)):
        cambio = False
        for r in rels:
            if r.origen in nivel and r.destino in nivel and nivel[r.destino] <= nivel[r.origen] < len(ids) - 1:
                nivel[r.destino] = nivel[r.origen] + 1
                cambio = True
        if not cambio:
            break
    cols: dict = {}
    for i in ids:
        cols.setdefault(nivel[i], []).append(i)
    return [cols[k] for k in sorted(cols)]


def construir_vistas(m: "Modelo", d: dict) -> list:
    """Parte el 'detalle completo' en sub-vistas pequenas y tematicas (cada una cabe en una pantalla)."""
    cod = d["codigo"]
    base = f"{cod} · {d['nombre']}"
    propios = [e for e in m.elementos.values() if e.dominio == cod]
    rels = list(m.relaciones.values())
    E = m.elementos
    ord_n = lambda ids: sorted(ids, key=lambda i: E[i].nombre)  # noqa: E731
    vistas: list[VistaDef] = []

    # 1) Estructura: modulos del codigo + contextos/sistemas externos y sus flujos
    comps = [e.id for e in propios if e.tipo == "ApplicationComponent" and not e.nombre.startswith("Adaptador")]
    tipos = {"Serving", "Flow"}  # la composicion raiz->modulo ya la dice el nombre "C2 / modulo"
    r1 = [r for r in rels if r.tipo in tipos and E[r.origen].tipo == "ApplicationComponent"
          and E[r.destino].tipo == "ApplicationComponent" and not E[r.origen].nombre.startswith("Adaptador")
          and not E[r.destino].nombre.startswith("Adaptador")
          and (r.origen in comps or r.destino in comps)]
    ids1 = set(comps) | {x for r in r1 for x in (r.origen, r.destino)}
    ids1 = ord_n(i for i in ids1 if any(i in (r.origen, r.destino) for r in r1) or (i in comps and " / " in E[i].nombre))
    vistas.append(VistaDef(f"{base} – 1 Estructura (módulos y contextos externos)", _nivel_por_camino(ids1, r1), r1))

    # 2) Contratos -> servicios <- comandos/consultas (en fila, sin cruces)
    pares = [r for r in rels if r.tipo == "Association" and E[r.origen].tipo == "Contract"
             and E[r.destino].tipo == "ApplicationService" and E[r.origen].dominio == cod]
    servicios = ord_n({r.destino for r in pares})
    TAM2 = 7
    for k in range(0, len(servicios), TAM2):
        grupo = set(servicios[k:k + TAM2])
        r2 = [r for r in pares if r.destino in grupo] + [
            r for r in rels if r.tipo == "Realization" and r.destino in grupo and E[r.origen].tipo == "ApplicationProcess"]
        cont = ord_n({r.origen for r in r2 if E[r.origen].tipo == "Contract"})
        proc = ord_n({r.origen for r in r2 if E[r.origen].tipo == "ApplicationProcess"})
        n_v = len(range(0, len(servicios), TAM2))
        suf = f" ({k // TAM2 + 1}/{n_v})" if n_v > 1 else ""
        vistas.append(VistaDef(f"{base} – 2 Contratos, servicios y comandos{suf}", [cont, ord_n(grupo), proc], r2))

    # 3) Adaptadores -> puertos (con el sistema externo que cada adaptador aisla)
    puertos_all = ord_n([e.id for e in propios if e.tipo == "ApplicationInterface"])
    r_ap = [r for r in rels if r.tipo == "Serving" and r.destino in puertos_all and E[r.origen].tipo == "ApplicationComponent"]
    adapt = ord_n({r.origen for r in r_ap} | {r.destino for r in rels if r.tipo == "Flow" and E[r.destino].dominio == cod
                                             and E[r.destino].nombre.startswith("Adaptador")})
    r_ext = [r for r in rels if r.tipo == "Flow" and r.destino in adapt]
    ext = ord_n({r.origen for r in r_ext})
    usados = {r.destino for r in r_ap}
    vistas.append(VistaDef(f"{base} – 3 Adaptadores y puertos (anticorrupción)",
                           [c for c in (ext, adapt, ord_n(usados)) if c], r_ext + r_ap))

    # 4) Unidad de trabajo y los repositorios que agrupa
    r_uow = [r for r in rels if r.tipo == "Aggregation" and E[r.origen].tipo == "ApplicationInterface"
             and E[r.origen].dominio == cod]
    if r_uow:
        raiz_u = ord_n({r.origen for r in r_uow})
        hijos = ord_n({r.destino for r in r_uow})
        vistas.append(VistaDef(f"{base} – 4 Unidad de trabajo y sus repositorios", [raiz_u, hijos], r_uow))

    # 5) Puertos que usa cada grupo de servicios (grupos de 5 para que las lineas se lean)
    TAM4 = 5
    todos_svc = ord_n({e.id for e in propios if e.tipo == "ApplicationService" and e.nombre.startswith("Manejador")})
    n4 = len(range(0, len(todos_svc), TAM4))
    for k in range(0, len(todos_svc), TAM4):
        grupo = set(todos_svc[k:k + TAM4])
        r_ps = [r for r in rels if r.tipo == "Serving" and r.destino in grupo
                and E[r.origen].tipo in ("ApplicationInterface", "ApplicationService")]
        puertos = ord_n({r.origen for r in r_ps if E[r.origen].tipo == "ApplicationInterface"})
        otros = ord_n({r.origen for r in r_ps if E[r.origen].tipo == "ApplicationService" and r.origen not in grupo})
        if not r_ps:
            continue
        cols = [puertos + otros, ord_n(grupo)]  # dependencias entre manejadores: junto a los puertos (todo contiguo)
        suf = f" ({k // TAM4 + 1}/{n4})" if n4 > 1 else ""
        vistas.append(VistaDef(f"{base} – 5 Servicios y los puertos que usan{suf}", cols, r_ps))

    # 6) Comandos -> eventos y agregado
    cmds = ord_n([e.id for e in propios if e.nombre.startswith("Comando:")])
    evs = ord_n([e.id for e in propios if e.tipo == "ApplicationEvent"])
    datos = ord_n([e.id for e in propios if e.tipo == "DataObject"])
    r4 = [r for r in rels if (r.tipo in ("Triggering", "Access") and r.origen in cmds)
          or (r.tipo == "Association" and r.origen in datos and r.destino in datos)]
    vistas.append(VistaDef(f"{base} – 6 Comandos, eventos y agregado", [c for c in (evs, cmds, datos) if c], r4))
    return vistas


class EscritorExchange:
    """Open Group ArchiMate Model Exchange File Format 3.x (lo importa Archi).

    Disposicion (SRP: solo coloca y dibuja): columnas ordenadas por baricentro para reducir cruces; cada relacion
    sale como linea ortogonal con puntos de quiebre en un 'canal' propio del hueco entre columnas.
    """

    ORDEN_FILAS = ["ApplicationComponent", "Contract", "ApplicationService", "ApplicationProcess",
                   "ApplicationEvent", "ApplicationInterface", "DataObject"]
    ANCHO, ALTO, VGAP = 240, 76, 46
    GAP_MIN, GAP_POR_CANAL, MARGEN = 110, 24, 30

    def escribir(self, m: Modelo, ruta: Path, titulo: str, vistas: list) -> None:
        defs: dict[str, str] = {}
        for e in m.elementos.values():
            for k in e.props:
                defs.setdefault(k, f"propid-{len(defs) + 1}")
        L = ['<?xml version="1.0" encoding="UTF-8"?>',
             '<model xmlns="http://www.opengroup.org/xsd/archimate/3.0/" '
             'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
             'xsi:schemaLocation="http://www.opengroup.org/xsd/archimate/3.0/ '
             'http://www.opengroup.org/xsd/archimate/3.1/archimate3_Diagram.xsd" identifier="id-modelo-ingenieria-inversa">',
             f'  <name xml:lang="es">{escape(titulo)}</name>', "  <elements>"]
        for e in m.elementos.values():
            L.append(f'    <element identifier={quoteattr(e.id)} xsi:type="{e.tipo}">')
            L.append(f'      <name xml:lang="es">{escape(e.nombre)}</name>')
            if e.doc:
                L.append(f'      <documentation xml:lang="es">{escape(e.doc)}</documentation>')
            if e.props:
                L.append("      <properties>")
                for k, v in e.props.items():
                    L.append(f'        <property propertyDefinitionRef="{defs[k]}"><value xml:lang="es">{escape(v)}</value></property>')
                L.append("      </properties>")
            L.append("    </element>")
        L.append("  </elements>")
        L.append("  <relationships>")
        for r in m.relaciones.values():
            extra = ' isDirected="false"' if r.tipo == "Association" else (f' accessType="{r.acceso}"' if r.acceso else "")
            L.append(f'    <relationship identifier={quoteattr(r.id)} source={quoteattr(r.origen)} '
                     f'target={quoteattr(r.destino)} xsi:type="{r.tipo}"{extra}>')
            silencio = r.nombre in ETIQUETAS_SILENCIOSAS
            if r.nombre and not silencio:
                L.append(f'      <name xml:lang="es">{escape(r.nombre)}</name>')
            if silencio:
                L.append(f'      <documentation xml:lang="es">{escape(r.nombre)}</documentation>')
            L.append("    </relationship>")
        L.append("  </relationships>")
        L.append("  <propertyDefinitions>")
        for k, pid in defs.items():
            L.append(f'    <propertyDefinition identifier="{pid}" type="string"><name>{escape(k)}</name></propertyDefinition>')
        L.append("  </propertyDefinitions>")
        L.append("  <views>\n    <diagrams>")
        for n_vista, v in enumerate(vistas, 1):
            L += self._dibujar(m, v, n_vista)
        L.append("    </diagrams>\n  </views>\n</model>")
        ruta.write_text("\n".join(L), encoding="utf-8")

    # ------------------------------------------------------------------ disposicion
    @staticmethod
    def _ordenar(cols: list, rels: list) -> None:
        """Baricentro: cada nodo se acerca a la altura media de sus vecinos (2 barridos ida y vuelta)."""
        col_de = {i: c for c, col in enumerate(cols) for i in col}
        vecinos: dict = {}
        for r in rels:
            if r.origen in col_de and r.destino in col_de and col_de[r.origen] != col_de[r.destino]:
                vecinos.setdefault(r.origen, []).append(r.destino)
                vecinos.setdefault(r.destino, []).append(r.origen)
        for orden in (range(1, len(cols)), range(len(cols) - 2, -1, -1)) * 2:
            for c in orden:
                pos = {i: k for col in cols for k, i in enumerate(col)}
                cols[c].sort(key=lambda i, c=c, pos=pos: (
                    sum(pos[v] for v in vecinos.get(i, ()) if col_de[v] != c) / max(
                        1, sum(1 for v in vecinos.get(i, ()) if col_de[v] != c)) if any(
                        col_de[v] != c for v in vecinos.get(i, ())) else pos[i], pos[i]))

    def _dibujar(self, m: Modelo, v: VistaDef, n: int) -> list[str]:
        cols = [list(c) for c in v.columnas if c]
        col_de = {i: c for c, col in enumerate(cols) for i in col}
        rels = [r for r in v.relaciones if r.origen in col_de and r.destino in col_de]
        self._ordenar(cols, rels)
        col_de = {i: c for c, col in enumerate(cols) for i in col}
        nc = len(cols)
        # -- que canales necesita cada hueco (hueco g = a la derecha de la columna g)
        uso: dict[int, list] = {g: [] for g in range(nc)}
        rutas = []
        for r in rels:
            a, b = col_de[r.origen], col_de[r.destino]
            if a == b:
                tipo, ga, gb = "lazo", a, a
            elif abs(a - b) == 1:
                tipo, ga, gb = "adj", min(a, b), min(a, b)
            else:
                tipo = "largo"
                ga = a if a < b else a - 1
                gb = b - 1 if a < b else b
            rutas.append([r, tipo, ga, gb, None, None])
        for ruta in rutas:
            for idx, g in ((4, ruta[2]), (5, ruta[3])):
                if idx == 5 and ruta[1] != "largo":
                    continue
                uso[g].append(ruta)
                ruta[idx] = len(uso[g])  # numero de canal (1..k) provisional
        n_lanes = sum(1 for x in rutas if x[1] == "largo")
        top = 30 + n_lanes * 14
        anchos = {g: max(self.GAP_MIN, self.GAP_POR_CANAL * (len(uso[g]) + 1)) for g in range(nc)}
        x_col, x = [], self.MARGEN
        for c in range(nc):
            x_col.append(x)
            x += self.ANCHO + anchos[c]
        max_filas = max(len(c) for c in cols)
        paso = self.ALTO + self.VGAP
        centro, caja = {}, {}
        for c, col in enumerate(cols):
            desplaza = (max_filas - len(col)) * paso / 2
            for k, i in enumerate(col):
                y = int(top + desplaza + k * paso)
                caja[i] = (x_col[c], y)
                centro[i] = y + self.ALTO // 2
        L = [f'      <view identifier="id-vista-{n}" xsi:type="Diagram"><name xml:lang="es">{escape(v.nombre)}</name>']
        nodo = {}
        for c, col in enumerate(cols):
            for i in col:
                nodo[i] = f"id-nodo-{n}-{len(nodo) + 1}"
                L.append(f'        <node identifier="{nodo[i]}" elementRef={quoteattr(i)} xsi:type="Element" '
                         f'x="{caja[i][0]}" y="{caja[i][1]}" w="{self.ANCHO}" h="{self.ALTO}"/>')

        def canal(g: int, k: int) -> int:  # x del canal k (1..) dentro del hueco g
            ini = x_col[g] + self.ANCHO
            return int(ini + k * anchos[g] / (len(uso[g]) + 1))

        # ordenar los canales de cada hueco por altura media para que las lineas no se monten
        for g in range(nc):
            uso[g].sort(key=lambda ru: (centro[ru[0].origen] + centro[ru[0].destino]))
        # puntos de salida/llegada repartidos a lo largo del borde de cada nodo (para que no compartan tramo)
        lados: dict = {}
        for idx, (r, tipo, *_resto) in enumerate(rutas):
            ca, cb = col_de[r.origen], col_de[r.destino]
            lado_s = "R" if (tipo == "lazo" or cb > ca) else "L"
            lado_t = "R" if (tipo == "lazo" or cb < ca) else "L"
            lados.setdefault((r.origen, lado_s), []).append((idx, "s", centro[r.destino]))
            lados.setdefault((r.destino, lado_t), []).append((idx, "t", centro[r.origen]))
        desv, borde = {}, {}
        for (nid, lado), lst in lados.items():
            lst.sort(key=lambda t: (t[2], t[0]))
            paso_d = min(14, (self.ALTO - 24) / max(len(lst) - 1, 1))
            for k, (idx, rol, _) in enumerate(lst):
                desv[(idx, rol)] = int((k - (len(lst) - 1) / 2) * paso_d)
                borde[(idx, rol)] = caja[nid][0] + self.ANCHO + 8 if lado == "R" else caja[nid][0] - 8
        lane = 0
        for k_con, (r, tipo, ga, gb, _, _) in enumerate(rutas, 1):
            idx = k_con - 1
            ys, yt = centro[r.origen] + desv[(idx, "s")], centro[r.destino] + desv[(idx, "t")]
            ini, fin = (borde[(idx, "s")], ys), (borde[(idx, "t")], yt)
            ka = uso[ga].index(next(x for x in uso[ga] if x[0] is r)) + 1
            if tipo in ("lazo", "adj"):
                cx = canal(ga, ka)
                puntos = [ini, (cx, ys), (cx, yt), fin]
            else:
                kb = uso[gb].index(next(x for x in uso[gb] if x[0] is r)) + 1
                lane += 1
                y_lane = 10 + lane * 14
                puntos = [ini, (canal(ga, ka), ys), (canal(ga, ka), y_lane), (canal(gb, kb), y_lane),
                          (canal(gb, kb), yt), fin]
            if tipo == "adj" and ys == yt and desv[(idx, "s")] == 0 and desv[(idx, "t")] == 0:
                puntos = []  # linea recta horizontal: no necesita quiebres
            cab = (f'        <connection identifier="id-con-{n}-{k_con}" relationshipRef={quoteattr(r.id)} '
                   f'xsi:type="Relationship" source="{nodo[r.origen]}" target="{nodo[r.destino]}"')
            if puntos:
                L.append(cab + ">")
                L += [f'          <bendpoint x="{px}" y="{py}"/>' for px, py in puntos]
                L.append("        </connection>")
            else:
                L.append(cab + "/>")
        L.append("      </view>")
        return L


# =====================================================================================
# REPORTE (+ contraste opcional con el diseno)
# =====================================================================================
class Reporte:
    def construir(self, m: Modelo, dominios: list[dict], diseno: Path | None) -> str:
        L = ["# Reporte de ingeniería inversa (código → ArchiMate)\n",
             "Lo que sigue es lo que el script **detectó leyendo el código**. No se ejecutó nada.\n"]
        for d in dominios:
            es = [e for e in m.elementos.values() if e.dominio == d["codigo"]]
            L.append(f"## {d['codigo']} · {d['nombre']} ({d['lenguaje']})\n")
            L.append("| Elemento ArchiMate | Cantidad |\n|---|---|")
            for t in EscritorExchange.ORDEN_FILAS:
                L.append(f"| {t} | {sum(1 for e in es if e.tipo == t)} |")
            L.append("")
            cmd = [e for e in es if e.tipo == "ApplicationProcess"]
            ev = {e.id: e for e in es if e.tipo == "ApplicationEvent"}
            L.append("**Comando → evento (relación Triggering detectada en el código):**\n")
            for c in sorted(cmd, key=lambda x: x.nombre):
                if c.nombre.startswith("Comando"):
                    evs = [ev[r.destino].nombre for r in m.relaciones.values()
                           if r.origen == c.id and r.tipo == "Triggering" and r.destino in ev]
                    L.append(f"- {c.nombre} → {', '.join(evs) if evs else '(ninguno: no publica evento de dominio)'}")
            L.append("")
        rels = {}
        for r in m.relaciones.values():
            rels[r.tipo] = rels.get(r.tipo, 0) + 1
        L.append("## Relaciones creadas\n\n" + ", ".join(f"{k}: {v}" for k, v in sorted(rels.items())) + "\n")
        if diseno:
            L += self._contraste(m, diseno)
        if m.avisos:
            L.append("## Avisos del analizador\n\n" + "\n".join(f"- {a}" for a in m.avisos) + "\n")
        L.append("## Reglas de detección usadas (resumen)\n\n"
                 "- Comando/Consulta = clase en `comandos`/`consultas` (Python) o sufijo `Comando`/`Consulta` (C#).\n"
                 "- Evento = clase en `eventos` o `record ... : EventoDeDominio`.\n"
                 "- Manejador (ApplicationService) = clase `Manejador*` con un comando/consulta resoluble.\n"
                 "- Puerto (ApplicationInterface) = clase abstracta en `puertos` / interfaz en `Puertos/Salida`.\n"
                 "- Adaptador (ApplicationComponent) = clase que implementa un puerto, o que lleva A1–A4 en el nombre.\n"
                 "- Contrato = ruta HTTP (decorador FastAPI / `MapGet|MapPost...`); Operaciones, Entrada, Salida,\n"
                 "  Reglas de negocio y Excepciones salen de la firma, los modelos y los `raise`/`throw` del manejador.\n"
                 "- Relación Triggering comando→evento: el manejador llama a un método del dominio que crea ese evento.\n"
                 "- Relación Serving puerto→manejador: el manejador lo recibe en su constructor o usa `uow.<puerto>`.\n")
        return "\n".join(L)

    def _contraste(self, m: Modelo, ruta: Path) -> list[str]:
        import xml.etree.ElementTree as ET
        xsi = "{http://www.w3.org/2001/XMLSchema-instance}type"
        raiz = ET.parse(ruta).getroot()
        els = {e.get("id"): e for e in raiz.iter("element")}
        diseno: dict[str, dict[str, str]] = {"cmd": {}, "evt": {}, "dato": {}}
        for e in els.values():
            n, t = e.get("name") or "", (e.get(xsi) or "").replace("archimate:", "")
            if t == "ApplicationProcess" and n.startswith("Comando:"):
                diseno["cmd"][normalizar(n.split(":", 1)[1])] = e.get("id")
            elif t == "ApplicationEvent" and n.startswith("Evento:"):
                diseno["evt"][normalizar(n.split(":", 1)[1])] = e.get("id")
            elif t == "DataObject":
                diseno["dato"][normalizar(n)] = e.get("id")
        accede = {}
        for r in raiz.iter("element"):
            if (r.get(xsi) or "").endswith("AccessRelationship"):
                accede.setdefault(r.get("source"), []).append(els[r.get("target")].get("name"))
        L = ["## Contraste con el diseño (Entrega 3)\n"]
        for clave, etiqueta, prefijo in (("cmd", "Comandos", "Comando:"), ("evt", "Eventos", "Evento:")):
            ok, extra = [], []
            for e in m.elementos.values():
                if e.nombre.startswith(prefijo) and e.dominio in ("C1", "C2"):
                    (ok if normalizar(e.nombre.split(":", 1)[1]) in diseno[clave] else extra).append(e.nombre)
            L.append(f"**{etiqueta}:** {len(ok)} del código coinciden con el diseño; {len(extra)} no están en el diseño.")
            for x in sorted(extra):
                L.append(f"- Solo en el código: {x}")
            L.append("")
        codigo_datos = {normalizar(e.nombre) for e in m.elementos.values() if e.tipo == "DataObject"}
        L.append("**Objetos de datos que el diseño asocia (Access) a los comandos hallados y que no existen como clase en el código:**\n")
        vistos = set()
        for e in m.elementos.values():
            if e.nombre.startswith("Comando:") and e.dominio in ("C1", "C2"):
                cid = diseno["cmd"].get(normalizar(e.nombre.split(":", 1)[1]))
                for objeto in accede.get(cid, []):
                    if normalizar(objeto) not in codigo_datos and (e.nombre, objeto) not in vistos:
                        vistos.add((e.nombre, objeto))
                        L.append(f"- {e.nombre} → «{objeto}» (puede estar como campo dentro de otra clase, o ser de otro contexto)")
        L.append("")
        return L


# =====================================================================================
# PRINCIPAL
# =====================================================================================
def preparar_raiz(entrada: Path) -> Path:
    if entrada.suffix.lower() != ".zip":
        return entrada
    destino = Path(tempfile.mkdtemp(prefix="ingenieria_inversa_"))
    with zipfile.ZipFile(entrada) as z:
        for info in z.infolist():  # solo lo necesario: se salta entornos virtuales y compilados
            partes = Path(info.filename).parts
            if any(p in {".venv", "bin", "obj", "__pycache__", ".git", ".pytest_cache"} for p in partes):
                continue
            z.extract(info, destino)  # extract() ya neutraliza rutas absolutas y '..'
    hijos = [p for p in destino.iterdir() if p.is_dir()]
    for base in [destino, *hijos]:
        if any((base / d["ruta"]).exists() for d in DOMINIOS):
            return base
    return destino


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--raiz", required=True, help="carpeta del proyecto o el .zip (contiene c1-fuentes y c2-atributos)")
    ap.add_argument("--salida", default="salida_archimate")
    ap.add_argument("--diseno", help="(opcional) .archimate del diseno para contrastar nombres")
    a = ap.parse_args(argv)
    raiz = preparar_raiz(Path(a.raiz))
    modelo, hechos = Modelo(), []
    analizadores = {"python": AnalizadorPython, "csharp": AnalizadorCSharp}
    for d in DOMINIOS:
        carpeta = raiz / d["ruta"]
        if not carpeta.exists():
            print(f"[aviso] no existe {carpeta}; se omite {d['codigo']}", file=sys.stderr)
            continue
        analizadores[d["lenguaje"]](modelo, carpeta, d).analizar()
        hechos.append(d)
    if not hechos:
        print("No se encontro ningun dominio. Revise --raiz.", file=sys.stderr)
        return 1
    salida = Path(a.salida)
    salida.mkdir(parents=True, exist_ok=True)
    EscritorCsv().escribir(modelo, salida / "csv")
    vistas = [v for d in hechos for v in construir_vistas(modelo, d)]
    EscritorExchange().escribir(modelo, salida / "modelo_c1_c2.xml",
                                "Hogar Andino – Modelo obtenido por ingeniería inversa del código", vistas)
    (salida / "reporte_ingenieria_inversa.md").write_text(
        Reporte().construir(modelo, hechos, Path(a.diseno) if a.diseno else None), encoding="utf-8")
    print(f"Elementos: {len(modelo.elementos)} | Relaciones: {len(modelo.relaciones)} | Salida: {salida.resolve()}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
