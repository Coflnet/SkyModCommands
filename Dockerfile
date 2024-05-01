FROM mcr.microsoft.com/dotnet/sdk:8.0 as build
WORKDIR /build
RUN git clone --depth=1 https://github.com/Ekwav/websocket-sharp \
    && git clone --depth=1 https://github.com/Coflnet/HypixelSkyblock.git dev \
    && git clone --depth=1 https://github.com/Coflnet/SkyFilter.git \
    && git clone --depth=1 https://github.com/Coflnet/SkyBackendForFrontend.git
WORKDIR /build/sky
COPY SkyModCommands.csproj SkyModCommands.csproj
RUN dotnet restore
COPY . .
RUN rm SkyModCommands.sln && dotnet test
RUN dotnet publish -c release -o /app

FROM mcr.microsoft.com/dotnet/sdk:8.0
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8000

RUN useradd --uid $(shuf -i 2000-65000 -n 1) app-user
USER app-user
RUN export PATH="$PATH:$HOME/.dotnet/tools"

ENTRYPOINT ["dotnet", "SkyModCommands.dll", "--hostBuilder:reloadConfigOnChange=false"]
