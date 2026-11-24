# Web app container image. Build from the repository root:
#   docker build -t stockflow-web .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/StockFlow.Core/StockFlow.Core.csproj src/StockFlow.Core/
COPY src/StockFlow.Data/StockFlow.Data.csproj src/StockFlow.Data/
COPY src/StockFlow.Web/StockFlow.Web.csproj src/StockFlow.Web/
COPY tools/StockFlow.DbDeploy/StockFlow.DbDeploy.csproj tools/StockFlow.DbDeploy/
RUN dotnet restore src/StockFlow.Web/StockFlow.Web.csproj \
 && dotnet restore tools/StockFlow.DbDeploy/StockFlow.DbDeploy.csproj
COPY . .
RUN dotnet publish src/StockFlow.Web/StockFlow.Web.csproj -c Release -o /out/web --no-restore \
 && dotnet publish tools/StockFlow.DbDeploy/StockFlow.DbDeploy.csproj -c Release -o /out/dbdeploy --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /out/web ./
COPY --from=build /out/dbdeploy ./dbdeploy/
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "StockFlow.Web.dll"]
