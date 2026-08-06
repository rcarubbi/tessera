FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Tessera.slnx .
COPY src/ src/
RUN dotnet restore src/Tessera.Api/Tessera.Api.csproj
RUN dotnet publish src/Tessera.Api/Tessera.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Tessera.Api.dll"]
