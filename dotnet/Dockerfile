# syntax=docker/dockerfile:1
# Una sola receta para los tres servicios:
#   docker build --build-arg PROYECTO=TccEventos.Api         --build-arg BASE=aspnet  -t tcc-api .
#   docker build --build-arg PROYECTO=TccEventos.Procesador  --build-arg BASE=runtime -t tcc-procesador .
#   docker build --build-arg PROYECTO=TccEventos.Notificador --build-arg BASE=runtime -t tcc-notificador .
ARG BASE=aspnet

# ---------- Compilación: SDK completo, nunca llega a producción ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS compilacion
ARG PROYECTO
WORKDIR /src

# Primero solo los .csproj y se restaura: esta capa se reutiliza mientras no cambien las dependencias.
COPY global.json Directory.Build.props ./
COPY src/TccEventos.Dominio/TccEventos.Dominio.csproj                 src/TccEventos.Dominio/
COPY src/TccEventos.Aplicacion/TccEventos.Aplicacion.csproj           src/TccEventos.Aplicacion/
COPY src/TccEventos.Contratos/TccEventos.Contratos.csproj             src/TccEventos.Contratos/
COPY src/TccEventos.Infraestructura/TccEventos.Infraestructura.csproj src/TccEventos.Infraestructura/
COPY src/TccEventos.Api/TccEventos.Api.csproj                         src/TccEventos.Api/
COPY src/TccEventos.Procesador/TccEventos.Procesador.csproj           src/TccEventos.Procesador/
COPY src/TccEventos.Notificador/TccEventos.Notificador.csproj         src/TccEventos.Notificador/
# PublishReadyToRun también aquí: el compilador crossgen se descarga en el restore, no en el publish.
RUN dotnet restore src/${PROYECTO}/${PROYECTO}.csproj -r linux-x64 -p:PublishReadyToRun=true

COPY src/ src/
# ReadyToRun: código precompilado, arranque y primera petición sin el costo del JIT.
RUN dotnet publish src/${PROYECTO}/${PROYECTO}.csproj -c Release -r linux-x64 --self-contained false \
      --no-restore -p:PublishReadyToRun=true -o /app \
 && mv /app/${PROYECTO} /app/servicio

# ---------- Ejecución: imagen "chiseled" (sin shell ni gestor de paquetes), usuario no root ----------
FROM mcr.microsoft.com/dotnet/${BASE}:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=compilacion /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["/app/servicio"]
