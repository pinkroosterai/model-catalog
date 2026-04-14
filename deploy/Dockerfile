FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Clarive.ModelRegistry.Service/Clarive.ModelRegistry.Service.csproj \
    -c Release -o /app/publish --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/data && chown -R app:app /app
USER app
ENV ModelRegistry__SnapshotPath=/app/data/snapshot.json
EXPOSE 8080
ENTRYPOINT ["dotnet", "Clarive.ModelRegistry.Service.dll"]
