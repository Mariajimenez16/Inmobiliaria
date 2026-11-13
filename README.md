# Hogar Andino · Gobierno de datos (Ley 1581)

Implementación de **dos dominios** del diseño de gobierno de datos de **Hogar Andino Inmobiliaria S.A.S.**, hecho con ArchiMate y DDD en la Evaluación 3 y llevado a código para la Evaluación Final de **Arquitectura de Software**.

> Un dominio está construido con **arquitectura hexagonal (C#)** y el otro con **microservicios (Python)**. Los demás dominios del diseño (C3 a C7) se **simulan** para mostrar cómo se comunican.

---

## ¿Qué problema resuelve?

Hogar Andino maneja información de arrendamientos, contabilidad, clientes y sitio web repartida en varios sistemas. Nadie tiene un inventario claro de **dónde está cada dato, qué significa y quién responde por él**, y eso es un riesgo frente a la **Ley 1581 de 2012** (protección de datos personales en Colombia).

El proyecto implementa el corazón de ese inventario:

| Dominio | Qué hace | Arquitectura | Lenguaje | Puerto |
|---|---|---|---|---|
| **C1 · Fuentes y metadatos técnicos** | Registra las fuentes de datos, controla quién autoriza leer su estructura, importa los metadatos técnicos (tablas y columnas), guarda versiones y detecta cambios | **Hexagonal** (puertos y adaptadores) | C# / .NET 10 | `8011` |
| **C2 · Atributos de negocio** *(dominio principal)* | Describe cada atributo una sola vez: definición, política de calidad y expresión regular. Marca lo que está incompleto | **Microservicios** | Python 3.13 / FastAPI | `8012` |

---

## Tecnologías y herramientas

- **C1:** C# · .NET 10 · ASP.NET Core (Minimal API) · SQLite · xUnit
- **C2:** Python 3.13 · FastAPI · Uvicorn · SQLite · `cryptography` (Fernet) · pytest
- **Despliegue:** Docker y Docker Compose
- **Diseño:** ArchiMate (herramienta Archi) y DDD
- **Automatización:** script en Python que lee el código y genera las vistas de ArchiMate (ingeniería inversa)

**Lo que incluye cada dominio** (requisitos del enunciado):

| Requisito | C1 (C#) | C2 (Python) |
|---|---|---|
| Base de datos SQL | SQLite | SQLite |
| Caché | Decorator sobre las versiones (no cambian, no hay que invalidar) | Cache-aside con vencimiento (TTL) sobre `consultarAtributo` |
| Estado / Memento | `MementoDeFuente`: si guardar falla, la fuente vuelve a su estado anterior | `AtributoMemento`: punto de retorno y también historial de versiones |
| Cifrado simple | AES-GCM para las credenciales de las fuentes | Fernet para definición y política de calidad |
| Despliegue | Docker (multi-etapa) | Docker |
| Cliente | Consola | Consola |

---

## Cómo ejecutarlo

### Requisitos

- **Docker Desktop** (opción más fácil), o bien
- **.NET SDK 10** y **Python 3.13** (opción local)

> Los comandos están escritos para **PowerShell en Windows**.

### Opción A · Con Docker (recomendada)

1. Entra a la carpeta del código:
   ```powershell
   cd Codigo
   ```
2. Crea tu archivo de variables a partir del ejemplo:
   ```powershell
   Copy-Item .env.example .env
   ```
   Abre el `.env` y cambia las dos claves por valores largos y aleatorios. Puedes generar cada una con este comando (ejecútalo dos veces, una por clave):
   ```powershell
   $b = New-Object byte[] 32; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
   ```
   ```env
   C1_CLAVE_CIFRADO=<clave-larga-para-C1>
   C2_CLAVE_CIFRADO=<clave-larga-para-C2>
   ```
3. Levanta los dos servicios:
   ```powershell
   docker compose up --build
   ```
4. Comprueba que ambos respondan:
   ```powershell
   Invoke-RestMethod http://localhost:8011/salud
   Invoke-RestMethod http://localhost:8012/salud
   ```
   Si C1 aún dice que está arrancando, espera unos segundos y repite.

C2 arranca primero y C1 le envía sus eventos (`C2_URL=http://c2:8012` dentro de Docker).

> **Solo C2 con Docker:** la carpeta `c2-atributos` tiene su propio `docker-compose.yml`. Desde `Codigo\c2-atributos` ejecuta `docker compose up --build`.

### Opción B · Local, sin Docker

**C2 (Python):**

```powershell
cd Codigo\c2-atributos
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
python -m uvicorn c2_atributos.main:crear_app_desde_entorno --factory --port 8012
```

**C1 (C#)**, en otra terminal:

```powershell
cd Codigo\c1-fuentes
$env:C2_URL = "http://localhost:8012"    # opcional: para que C1 le envíe sus eventos a C2 de verdad
dotnet run --project src\C1.Host --urls http://localhost:8011
```

### Clientes de consola

```powershell
# C2 (con el entorno virtual activo, desde Codigo\c2-atributos)
python -m c2_atributos.cliente

# C1 (desde Codigo\c1-fuentes)
dotnet run --project src\C1.Cliente
```

El cliente de C1 trae **demostraciones guiadas**, incluida la que muestra cómo un evento de C1 llega a C2.

### Pruebas

```powershell
# C1
cd Codigo\c1-fuentes
dotnet test

# C2
cd Codigo\c2-atributos
python -m pytest
```

Resultado esperado: **71 pruebas en C1** y **51 en C2**.

### Variables de entorno

| Variable | Dominio | Para qué sirve | Por defecto |
|---|---|---|---|
| `C1_URLS` | C1 | Dirección donde escucha | `http://localhost:8011` |
| `C1_RUTA_BD` | C1 | Archivo SQLite | `datos/c1_fuentes.db` |
| `C1_CLAVE_CIFRADO` | C1 | Clave para cifrar credenciales | Obligatoria fuera de `dev` |
| `C1_ENTORNO` | C1 | `dev` permite la clave de prueba | `dev` |
| `C1_TTL_CACHE_SEGUNDOS` | C1 | Vencimiento de la caché | `60` |
| `C2_URL` | C1 y clientes | Dirección de C2 para enviarle eventos | Sin definir = C2 simulado en memoria |
| `C2_RUTA_BD` | C2 | Archivo SQLite | `c2_atributos.db` |
| `C2_CLAVE_CIFRADO` | C2 | Clave para cifrar definición y política | Obligatoria fuera de `dev` |
| `C2_ENTORNO` | C2 | `dev`, `test` o `prod` | `dev` |
| `C2_TTL_CACHE` | C2 | Vencimiento de la caché | `60` |
| `C2_SIMULACION` | C2 | Activa las rutas de simulación | Activa salvo en `prod` |

---

## Rutas principales

**C2 · Atributos de negocio** (`http://localhost:8012`)

| Método | Ruta | Qué hace |
|---|---|---|
| `POST` | `/v1/atributos` | Registrar atributo de negocio |
| `PUT` | `/v1/atributos/{id}/metadato-negocio` | Actualizar metadato (definición) |
| `PUT` | `/v1/atributos/{id}/politica-calidad` | Registrar política de calidad |
| `POST` | `/v1/atributos/{id}/expresion-regular/validaciones` | Validar la sintaxis de una expresión regular |
| `GET` | `/v1/atributos`, `/v1/atributos/{id}`, `/v1/atributos/{id}/versiones` | Consultas |
| `GET` | `/v1/atributos/{id}/solicitud-de-completar` | Qué le falta a un atributo |
| `POST` | `/v1/integracion/inventario` y `/validaciones` | Carga del inventario manual (adaptador A4) |

**C1 · Fuentes** (`http://localhost:8011`)

| Método | Ruta | Qué hace |
|---|---|---|
| `POST` | `/v1/fuentes` | Registrar fuente |
| `POST` | `/v1/fuentes/{id}/autorizaciones` | Autorizar acceso a metadatos |
| `DELETE` | `/v1/fuentes/{id}/autorizacion` | Revocar la autorización |
| `POST` | `/v1/fuentes/{id}/importaciones` | Importar metadatos técnicos |
| `POST` | `/v1/fuentes/{id}/comparaciones` | Comparar estructuras |
| `GET` | `/v1/fuentes/{id}/estado-importacion` | Estado: sin importar, disponible o desactualizada |
| `POST`/`GET` | `/v1/avisos-de-cambio` | Avisos de cambio de proveedores (adaptador A3) |

En C1 el usuario se indica con el encabezado `X-Usuario` (representa la autenticación, que no está en el alcance).

---

## ¿Qué está simulado?

El diseño tiene siete contextos. Solo implementamos C1 y C2; el resto se **simula** para demostrar la interoperabilidad:

- **C7 (control de acceso):** C1 le pregunta si el usuario tiene permiso. Matriz de permisos simulada.
- **C6 (diccionario y reporte):** recibe los eventos de C1 y C2 y los anota.
- **C4 (responsables):** C2 le pregunta si un atributo tiene responsable. Es un simulador.
- **Sistemas externos (A1, A2):** lectores simulados de motores relacionales y del CRM.

Las rutas `/v1/simulacion/...` permiten ver qué recibió cada contexto simulado.

---

## Diseño y vistas de ArchiMate

Las vistas creadas están en la carpeta `Vistas/`.

---

## Documentos

Las decisiones técnicas (por qué cada arquitectura, base de datos, patrones y SOLID) y la guía de estudio para la sustentación están en la carpeta `Documento/`.

---

## Notas de seguridad

- Las claves de cifrado **no están en el código**: vienen de variables de entorno.
- C1 **no almacena datos personales**, solo estructura.
- El `.env` y las bases `*.db` están en `.gitignore`. En el repositorio solo hay archivos `.env.example` con valores de relleno.
- La autenticación real y un gestor de secretos quedan fuera del alcance (ver límites en el documento de decisiones técnicas).

---

## Equipo

| Integrante |
|---|
| María Alexandra Jiménez Suárez |
| Juan José Álvarez Restrepo |
| Carolina Ramírez Lotero |
| Natalia Giraldo Morales |

**Materia:** Arquitectura de Software · **Docente:** César López · **Periodo:** [2026-2]
