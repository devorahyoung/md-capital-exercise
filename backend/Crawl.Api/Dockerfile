FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Crawl.Core/Crawl.Core.csproj Crawl.Core/
COPY Crawl.Api/Crawl.Api.csproj   Crawl.Api/
RUN dotnet restore Crawl.Api/Crawl.Api.csproj

COPY Crawl.Core/ Crawl.Core/
COPY Crawl.Api/  Crawl.Api/
RUN dotnet publish Crawl.Api/Crawl.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Crawl.Api.dll"]
