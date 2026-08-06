FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Tessera.slnx .
COPY src/ src/
RUN dotnet restore src/Tessera.Worker/Tessera.Worker.csproj
RUN dotnet publish src/Tessera.Worker/Tessera.Worker.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends git && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "Tessera.Worker.dll"]
