# Use the official image as a parent image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
ENV ASPNETCORE_HTTP_PORTS=80

# Use SDK image to build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["OpenShelf.csproj", "./"]
RUN dotnet restore "OpenShelf.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "OpenShelf.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "OpenShelf.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Ensure the database directory exists and has permissions
# In production, you typically mount a volume here
RUN mkdir -p /app/data
ENV ConnectionStrings__DefaultConnection="Data Source=/app/data/openshelf.db"

ENTRYPOINT ["dotnet", "OpenShelf.dll"]
