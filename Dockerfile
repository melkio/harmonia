FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env

WORKDIR /app

COPY *.slnx .
COPY src/ ./src/
COPY test/ ./test/

RUN dotnet restore
RUN dotnet publish src/Harmonia.Host/Harmonia.Host.csproj -c Release -o ./out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build-env /app/out ./Harmonia.Host
ENTRYPOINT ["dotnet", "Harmonia.Host/Harmonia.Host.dll"]    