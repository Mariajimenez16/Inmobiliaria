# Imagen de C1 (Fuentes y metadatos tecnicos). Dos etapas: compilar con el SDK y ejecutar con la imagen liviana de ASP.NET.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS compilacion
WORKDIR /src
COPY src ./src
RUN dotnet publish src/C1.Host/C1.Host.csproj -c Release -o /publicado /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=compilacion /publicado .

# Usuario sin privilegios (la imagen ya trae uno) y carpeta de datos (aqui vive la base SQLite)
RUN mkdir /data && chown $APP_UID /data
USER $APP_UID

ENV C1_URLS=http://0.0.0.0:8011 \
    C1_RUTA_BD=/data/c1_fuentes.db
VOLUME ["/data"]
EXPOSE 8011

# La imagen no trae curl: se usa el /dev/tcp de bash para pedir /salud
HEALTHCHECK --interval=15s --timeout=3s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8011 && printf 'GET /salud HTTP/1.0\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q 200"]

ENTRYPOINT ["dotnet", "C1.Host.dll"]