FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files and restore
COPY Backend/collegehomework.csproj Backend/
RUN dotnet restore Backend/collegehomework.csproj

# Copy all code
COPY . .
WORKDIR /src/Backend
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY Frontend /Frontend

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "collegehomework.dll"]
