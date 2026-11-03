# SnelToets – container image (ASP.NET Core 10, SQLite).
#   docker build -t sneltoets .
#   docker run -d -p 8080:8080 -v sneltoets-data:/data -e Mcp__ApiKey=<sleutel> -e Beheer__Pincode=<pincode> sneltoets

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SnelToets.sln ./
COPY src/SnelToets.Core/SnelToets.Core.csproj src/SnelToets.Core/
COPY src/SnelToets.Web/SnelToets.Web.csproj src/SnelToets.Web/
RUN dotnet restore src/SnelToets.Web/SnelToets.Web.csproj
COPY src/ src/
# The demo content is embedded from samples/demo-nederlands.json.
COPY samples/ samples/
RUN dotnet publish src/SnelToets.Web/SnelToets.Web.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# SQLite database lives on a volume so content and progress survive updates.
RUN mkdir -p /data && chown "$APP_UID" /data
VOLUME /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__SnelToets="Data Source=/data/sneltoets.db"
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "SnelToets.Web.dll"]
