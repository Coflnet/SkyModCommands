FROM mcr.microsoft.com/dotnet/sdk:6.0 as build
WORKDIR /build
RUN git clone --depth=1 https://github.com/Ekwav/websocket-sharp
RUN git clone --depth=1 https://github.com/Coflnet/HypixelSkyblock.git dev
RUN git clone --depth=1 https://github.com/Coflnet/SkyFilter.git
RUN git clone --depth=1 https://github.com/Coflnet/SkyBackendForFrontend.git
WORKDIR /build/sky
COPY SkyModCommands.csproj SkyModCommands.csproj
RUN dotnet restore
COPY . .
RUN dotnet test
RUN dotnet test ../SkyBackendForFrontend/SkyBackendForFrontend.csproj
RUN dotnet publish -c release

FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /app

COPY --from=build /build/sky/bin/release/net6.0/publish/ .

ENV ASPNETCORE_URLS=http://+:8000

RUN useradd --uid $(shuf -i 2000-65000 -n 1) app
USER app

ENTRYPOINT ["dotnet", "SkyModCommands.dll", "--hostBuilder:reloadConfigOnChange=false"]
