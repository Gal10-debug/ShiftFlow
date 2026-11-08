FROM node:24-bookworm-slim@sha256:01d566e8d94d8c619591d78652babfccd3c6992a3dffa9ed5ab39e11cf6c70e9 AS frontend
WORKDIR /build/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /build
COPY global.json ./
COPY src/ShiftManager/ src/ShiftManager/
COPY --from=frontend /build/src/ShiftManager/wwwroot/ src/ShiftManager/wwwroot/
RUN --mount=type=cache,id=shiftflow-pilot-nuget,target=/root/.nuget/packages \
    dotnet restore src/ShiftManager/ShiftManager.csproj && \
    dotnet publish src/ShiftManager/ShiftManager.csproj -c Release --no-restore --warnaserror -o /publish && \
    mkdir -p /state/keys && chmod 700 /state/keys

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled-extra@sha256:a9298ccc39c6258ae71228479e2f3f8d2ceb6639a9927262a4b0ff7d319a0e7e
WORKDIR /app
COPY --from=build /publish/ ./
# Copy the prepared directory as a child entry. Copying an empty directory's
# contents relies on builder-specific destination-directory chmod behavior.
COPY --from=build --chown=1654:1654 /state/ /var/lib/shiftflow/
ENV ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_HTTP_PORTS=8080 DOTNET_PROCESSOR_COUNT=2
USER 1654:1654
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=4s --start-period=20s --retries=3 CMD ["dotnet", "ShiftManager.dll", "--healthcheck", "ready"]
ENTRYPOINT ["dotnet", "ShiftManager.dll"]
