FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build

RUN git clone --depth=1 https://github.com/Ekwav/websocket-sharp \
    && git clone --depth=1 https://github.com/Coflnet/HypixelSkyblock.git dev \
    && git clone --depth=1 https://github.com/Coflnet/SkyFilter.git \
    && git clone --depth=1 https://github.com/Coflnet/SkyBackendForFrontend.git
ARG SHARED_GOALS_REVISION=375482b3edaa720082da32812bbb909f109d13e6
RUN git -C /build/SkyBackendForFrontend fetch --depth=1 origin "$SHARED_GOALS_REVISION" \
    && git -C /build/SkyBackendForFrontend checkout --detach FETCH_HEAD
WORKDIR /build/sky
COPY SkyModCommands.csproj SkyModCommands.csproj
RUN dotnet restore
COPY . .
RUN rm -f SkyModCommands.sln && dotnet test
RUN dotnet publish -c release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8000

USER $APP_UID

ENTRYPOINT ["dotnet", "SkyModCommands.dll", "--hostBuilder:reloadConfigOnChange=false"]
