# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/PanVault.Api/PanVault.Api.csproj", "src/PanVault.Api/"]
RUN dotnet restore "src/PanVault.Api/PanVault.Api.csproj"

COPY src/ src/
RUN dotnet publish src/PanVault.Api/PanVault.Api.csproj \
    -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app

COPY --from=build /app .

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "PanVault.Api.dll"]