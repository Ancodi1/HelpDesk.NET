# Reviewed immutable bases; update digests deliberately with validation.
ARG NODE_IMAGE=node:24-bookworm-slim@sha256:01d566e8d94d8c619591d78652babfccd3c6992a3dffa9ed5ab39e11cf6c70e9
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c
FROM ${NODE_IMAGE} AS web
WORKDIR /src
COPY HelpDesk.Web/package.json HelpDesk.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY HelpDesk.Web/ ./
RUN npm run build -- --configuration production

FROM ${SDK_IMAGE} AS api
WORKDIR /src
COPY Directory.Build.props ./
COPY HelpDesk.Api/HelpDesk.Api.csproj HelpDesk.Api/packages.lock.json HelpDesk.Api/
RUN dotnet restore HelpDesk.Api/HelpDesk.Api.csproj --locked-mode
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
