# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base

ARG APP_USER=appuser
ARG APP_UID=10001

RUN groupadd --gid $APP_UID $APP_USER \
    && useradd --uid $APP_UID --gid $APP_UID --create-home --shell /usr/sbin/nologin $APP_USER \
    && mkdir -p /app /tmp/app \
    && chown -R $APP_USER:$APP_USER /app /tmp/app

WORKDIR /app
EXPOSE 8080

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Gateway.csproj", "."]
RUN dotnet restore "./Gateway.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ARG APP_UID=10001
USER $APP_UID
ENTRYPOINT ["dotnet", "Gateway.dll"]