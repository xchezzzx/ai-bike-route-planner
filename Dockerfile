FROM node:24-bookworm-slim AS frontend
WORKDIR /src
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
# Serialize the frontend and backend builds on small/shared builders.
COPY --from=frontend /src/dist /frontend
COPY src/backend/CyclingRoutes.Api/CyclingRoutes.Api.csproj CyclingRoutes.Api/
COPY src/backend/CyclingRoutes.Application/CyclingRoutes.Application.csproj CyclingRoutes.Application/
COPY src/backend/CyclingRoutes.Contracts/CyclingRoutes.Contracts.csproj CyclingRoutes.Contracts/
COPY src/backend/CyclingRoutes.Domain/CyclingRoutes.Domain.csproj CyclingRoutes.Domain/
COPY src/backend/CyclingRoutes.Infrastructure/CyclingRoutes.Infrastructure.csproj CyclingRoutes.Infrastructure/
RUN DOTNET_PROCESSOR_COUNT=2 dotnet restore CyclingRoutes.Api/CyclingRoutes.Api.csproj --disable-parallel
COPY src/backend/ ./
RUN DOTNET_PROCESSOR_COUNT=2 dotnet publish CyclingRoutes.Api/CyclingRoutes.Api.csproj -c Release --no-restore -o /app/publish -m:1 /p:UseAppHost=false /p:UseSharedCompilation=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
COPY --from=backend /app/publish ./
COPY --from=backend /frontend ./wwwroot
USER $APP_UID
ENTRYPOINT ["dotnet", "CyclingRoutes.Api.dll"]
