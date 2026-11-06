# Override with reviewed immutable digests for release builds.
ARG NODE_IMAGE=node:24-bookworm-slim
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0
FROM ${NODE_IMAGE} AS web
WORKDIR /src
COPY HelpDesk.Web/package.json HelpDesk.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY HelpDesk.Web/ ./
RUN npm run build -- --configuration production

FROM ${SDK_IMAGE} AS api
WORKDIR /src
COPY HelpDesk.Api/HelpDesk.Api.csproj HelpDesk.Api/
RUN dotnet restore HelpDesk.Api/HelpDesk.Api.csproj
COPY HelpDesk.Api/ HelpDesk.Api/
RUN dotnet publish HelpDesk.Api/HelpDesk.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false \
    && rm -f /out/appsettings.Development.json
COPY --from=web /src/dist/helpdesk-web/browser/ /out/wwwroot/

FROM ${RUNTIME_IMAGE} AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_HTTP_PORTS=8080
COPY --from=api /out/ ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "HelpDesk.Api.dll"]
