FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build

RUN git clone --depth=1 https://github.com/Ekwav/websocket-sharp \
    && git init dev && git -C dev remote add origin https://github.com/Coflnet/HypixelSkyblock.git && git -C dev fetch --depth=1 origin 2df0f7c7a34c8c04653e4cf7c02ab6805876eb7a && git -C dev checkout --detach FETCH_HEAD \
    && git clone --depth=1 https://github.com/Coflnet/SkyFilter.git \
    && git clone --depth=1 https://github.com/Coflnet/SkyBackendForFrontend.git
ARG SHARED_GOALS_REVISION=816a967a89fa318c1e48bb3585d769b76bd921b6
RUN git -C /build/SkyBackendForFrontend fetch --depth=1 origin "$SHARED_GOALS_REVISION" \
    && git -C /build/SkyBackendForFrontend checkout --detach FETCH_HEAD
WORKDIR /build/sky
COPY SkyModCommands.csproj SkyModCommands.csproj
RUN dotnet restore --disable-build-servers
COPY . .
RUN rm -f SkyModCommands.sln && dotnet test --disable-build-servers
RUN dotnet publish -c release -o /app --disable-build-servers

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8000

USER $APP_UID

ENTRYPOINT ["dotnet", "SkyModCommands.dll", "--hostBuilder:reloadConfigOnChange=false"]
