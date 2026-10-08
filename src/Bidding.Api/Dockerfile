# Build context: repository root (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first, from project files only, so the layer is cached until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Bidding.Domain/Bidding.Domain.csproj src/Bidding.Domain/
COPY src/Bidding.Application/Bidding.Application.csproj src/Bidding.Application/
COPY src/Bidding.Infrastructure/Bidding.Infrastructure.csproj src/Bidding.Infrastructure/
COPY src/Bidding.Api/Bidding.Api.csproj src/Bidding.Api/
RUN dotnet restore src/Bidding.Api/Bidding.Api.csproj

COPY src/ src/
RUN dotnet publish src/Bidding.Api/Bidding.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Non-root user that ships with the .NET 8 images.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bidding.Api.dll"]
