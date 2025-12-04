# Frontend build stage
# Cache-bust: Updated dependencies 2025-12-03
FROM node:24-alpine AS frontend-build
WORKDIR /frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# Backend build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy src directory
COPY src/ .

# Restore and publish
RUN dotnet restore src.csproj
RUN dotnet publish src.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:80
EXPOSE 80
COPY --from=build /app/publish .

# Copy frontend build output to wwwroot
COPY --from=frontend-build /frontend/dist ./wwwroot

ENTRYPOINT ["dotnet", "src.dll"]
