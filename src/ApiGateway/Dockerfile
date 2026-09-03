FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
ARG APP_VERSION=1.0.0
ARG APP_ASSEMBLY_VERSION=1.0.0.0
RUN dotnet publish src/ApiGateway/ApiGateway.csproj -c Release -o /out /p:UseAppHost=false /p:Version=${APP_VERSION} /p:AssemblyVersion=${APP_ASSEMBLY_VERSION} /p:FileVersion=${APP_ASSEMBLY_VERSION} /p:InformationalVersion=${APP_VERSION}
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG APP_VERSION=1.0.0
LABEL org.opencontainers.image.title="ApiGateway" \
      org.opencontainers.image.description="ApiGateway YARP reverse proxy" \
      org.opencontainers.image.version=${APP_VERSION}
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /app/state && chown -R 1654:1654 /app
USER 1654
ENV DOTNET_EnableDiagnostics=0 \
    DatabaseProvider=Sqlite \
    ConnectionStrings__Gateway="Data Source=/app/state/apigateway.db" \
    Gateway__Environment=development \
    Gateway__LastKnownGoodPath=/app/state/last-known-good.json \
    Gateway__DataProtectionKeysPath=/app/state/gateway-data-protection-keys \
    Gateway__InboundTls__HttpPort=8080 \
    Gateway__InboundTls__HttpsPort=8443 \
    CertificateProtection__KeysPath=/app/state/certificate-keys
EXPOSE 8080 8443
ENTRYPOINT ["dotnet", "ApiGateway.dll"]
