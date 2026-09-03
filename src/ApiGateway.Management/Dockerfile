FROM node:24-alpine AS web
WORKDIR /src/ApiGateway
COPY Packages/Aditify/package.json Packages/Aditify/yarn.lock Packages/Aditify/
COPY Packages/Aditify/Ui/package.json Packages/Aditify/Ui/package.json
COPY Packages/Aditify/Identity/package.json Packages/Aditify/Identity/package.json
COPY package.json yarn.lock ./
COPY src/ApiGateway.Admin/package.json src/ApiGateway.Admin/package.json
COPY documentation/package.json documentation/package.json
COPY tests/ApiGateway.BrowserTests/package.json tests/ApiGateway.BrowserTests/package.json
RUN corepack enable \
    && yarn --cwd Packages/Aditify install --frozen-lockfile \
    && yarn install --frozen-lockfile
COPY Packages/Aditify/Ui Packages/Aditify/Ui
COPY Packages/Aditify/Identity Packages/Aditify/Identity
COPY src/ApiGateway.Admin src/ApiGateway.Admin
COPY documentation documentation
WORKDIR /src/ApiGateway/Packages/Aditify
RUN yarn build
WORKDIR /src/ApiGateway
RUN yarn workspace api-gateway-admin build \
    && yarn docs:build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src/ApiGateway
COPY . .
COPY --from=web /src/ApiGateway/src/ApiGateway.Management/wwwroot/admin src/ApiGateway.Management/wwwroot/admin
COPY --from=web /src/ApiGateway/src/ApiGateway.Management/wwwroot/docs src/ApiGateway.Management/wwwroot/docs
ARG APP_VERSION=1.0.0
ARG APP_ASSEMBLY_VERSION=1.0.0.0
RUN dotnet publish src/ApiGateway.Management/ApiGateway.Management.csproj -c Release -o /out /p:UseAppHost=false /p:Version=${APP_VERSION} /p:AssemblyVersion=${APP_ASSEMBLY_VERSION} /p:FileVersion=${APP_ASSEMBLY_VERSION} /p:InformationalVersion=${APP_VERSION}
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG APP_VERSION=1.0.0
LABEL org.opencontainers.image.title="ApiGateway Management" \
      org.opencontainers.image.description="ApiGateway management API and administration UI" \
      org.opencontainers.image.version=${APP_VERSION}
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /app/state/data-protection-keys && chown -R 1654:1654 /app
USER 1654
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    DatabaseProvider=Sqlite \
    ConnectionStrings__Gateway="Data Source=/app/state/apigateway.db" \
    DataProtection__KeysPath=/app/state/management-data-protection-keys \
    CertificateProtection__KeysPath=/app/state/certificate-keys \
    Gateway__ApplyMigrationsOnStartup=false
EXPOSE 8080
ENTRYPOINT ["dotnet", "ApiGateway.Management.dll"]
