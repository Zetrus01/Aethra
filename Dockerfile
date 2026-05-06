# A .NET SDK használata a buildhez
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Csak a .csproj fájl másolása a függőségek visszaállításához
COPY ["Krajcsovics Christofer.csproj", "."]
RUN dotnet restore

# A teljes forráskód másolása és az alkalmazás buildelése
COPY . .
RUN dotnet publish "Krajcsovics Christofer.csproj" -c Release -o /app/publish

# A könnyű, csak futáshoz szükséges runtime környezet
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# A lefordított alkalmazás másolása az előző stage-ből
COPY --from=build /app/publish .

# A Render által használt port beállítása
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

# Az alkalmazás indítása
ENTRYPOINT ["dotnet", "Krajcsovics Christofer.dll"]
