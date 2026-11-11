FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:1a48394b6f29d3a32c79d9edecd4abda0ecbe8776d567dbfc4590ed6979c740c AS base
WORKDIR /app
EXPOSE 7297

USER root
# The order PDFs are rendered by headless Chrome. The aspnet:10.0 image is Ubuntu 24.04, where the
# chromium apt package is a snap stub that does not run in a container, so Google Chrome stable comes
# from Google's apt repository instead.
RUN apt-get update && apt-get install -y --no-install-recommends \
        wget \
        gnupg \
        ca-certificates \
        tzdata \
        fonts-liberation \
        libnss3 \
        libatk-bridge2.0-0 \
        libgtk-3-0 \
        libxss1 \
        libgbm1 \
        libasound2t64 \
    && wget -qO- https://dl.google.com/linux/linux_signing_key.pub \
        | gpg --dearmor -o /usr/share/keyrings/google-chrome.gpg \
    && echo "deb [arch=amd64 signed-by=/usr/share/keyrings/google-chrome.gpg] http://dl.google.com/linux/chrome/deb/ stable main" \
        > /etc/apt/sources.list.d/google-chrome.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends google-chrome-stable \
    && rm -rf /var/lib/apt/lists/*

ENV PUPPETEER_EXECUTABLE_PATH=/usr/bin/google-chrome-stable
USER app

FROM mcr.microsoft.com/dotnet/sdk:11.0@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["altinnendata-api.csproj", "."]
RUN dotnet restore "./altinnendata-api.csproj"
COPY . .
RUN dotnet build "./altinnendata-api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./altinnendata-api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS="http://*:7297"
USER app
ENTRYPOINT ["dotnet", "altinnendata-api.dll"]
