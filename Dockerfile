FROM node:22 AS ui
WORKDIR /repo/admin-ui
COPY admin-ui/package.json admin-ui/package-lock.json ./
RUN npm ci
COPY admin-ui/ ./
# Vite writes to ../src/CrmNlQuery.Web/wwwroot/admin-next (see vite.config.ts)
RUN mkdir -p /repo/src/CrmNlQuery.Web/wwwroot && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY global.json Directory.Build.props Directory.Packages.props CrmNlQuery.slnx ./
COPY src/ src/
COPY --from=ui /repo/src/CrmNlQuery.Web/wwwroot/admin-next src/CrmNlQuery.Web/wwwroot/admin-next
RUN dotnet publish src/CrmNlQuery.Web/CrmNlQuery.Web.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Windows (NTLM/Negotiate) sign-in to the CRM from Linux needs the GSSAPI libraries and the NTLM plugin.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 gss-ntlmssp && rm -rf /var/lib/apt/lists/*
COPY --from=build /out ./
ENV ASPNETCORE_ENVIRONMENT=Development \
    Service__Url=http://0.0.0.0:5080
EXPOSE 5080
ENTRYPOINT ["dotnet", "CrmNlQuery.Web.dll"]
