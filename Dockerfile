FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
WORKDIR /opt/build

# Copy everything
COPY . ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /opt/run
COPY --from=build-env /opt/build/out .
COPY --from=build-env /opt/build/config.json .
ENTRYPOINT ["dotnet", "/opt/run/Discord-IRC-Sharp.dll"]
