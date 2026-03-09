FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Crawl.Core/Crawl.Core.csproj   Crawl.Core/
COPY Crawl.Worker/Crawl.Worker.csproj Crawl.Worker/
RUN dotnet restore Crawl.Worker/Crawl.Worker.csproj

COPY Crawl.Core/   Crawl.Core/
COPY Crawl.Worker/ Crawl.Worker/
RUN dotnet publish Crawl.Worker/Crawl.Worker.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Crawl.Worker.dll"]
