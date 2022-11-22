FROM mcr.microsoft.com/dotnet/sdk:7.0 as build
WORKDIR /build
RUN git clone --depth=1 https://github.com/Ekwav/websocket-sharp \
    && git clone --depth=1 https://github.com/Coflnet/HypixelSkyblock.git dev \
    && git clone --depth=1 https://github.com/Coflnet/SkyFilter.git \
    && git clone --depth=1 https://github.com/Coflnet/SkyBackendForFrontend.git
WORKDIR /build/sky
COPY SkyModCommands.csproj SkyModCommands.csproj
RUN dotnet restore
COPY . .
RUN dotnet test && dotnet test ../SkyBackendForFrontend/SkyBackendForFrontend.csproj
RUN dotnet publish -c release

FROM mcr.microsoft.com/dotnet/sdk:7.0
WORKDIR /app

COPY --from=build /build/sky/bin/release/net6.0/publish/ .

ENV ASPNETCORE_URLS=http://+:8000
RUN dotnet tool install --global dotnet-gcdump

#RUN useradd --uid $(shuf -i 2000-65000 -n 1) app
#USER app
RUN export PATH="$PATH:$HOME/.dotnet/tools"

ENTRYPOINT ["dotnet", "SkyModCommands.dll", "--hostBuilder:reloadConfigOnChange=false"]
