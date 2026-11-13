# Imagen del microservicio C2 (Atributos de negocio)
FROM python:3.13-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1

WORKDIR /app

# 1) Dependencias primero: Docker reutiliza esta capa si requirements.txt no cambia
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# 2) Codigo de la aplicacion
COPY c2_atributos ./c2_atributos

# 3) Usuario sin privilegios y carpeta de datos (aqui vive la base SQLite)
RUN useradd --create-home appuser && mkdir /data && chown appuser /data
USER appuser

ENV C2_RUTA_BD=/data/c2_atributos.db
VOLUME ["/data"]
EXPOSE 8012

HEALTHCHECK --interval=15s --timeout=3s --retries=3 \
  CMD python -c "import urllib.request as u; u.urlopen('http://localhost:8012/salud', timeout=2)"

CMD ["python", "-m", "uvicorn", "c2_atributos.main:crear_app_desde_entorno", "--factory", "--host", "0.0.0.0", "--port", "8012"]