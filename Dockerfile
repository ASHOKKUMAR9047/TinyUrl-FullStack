# Use the official .NET 9.0 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR /app

# Copy the csproj file and restore dependencies
COPY backend/TinyUrl.Api/TinyUrl.Api.csproj ./backend/TinyUrl.Api/
RUN dotnet restore backend/TinyUrl.Api/TinyUrl.Api.csproj

# Copy all backend source files and publish
COPY backend/TinyUrl.Api/ ./backend/TinyUrl.Api/
WORKDIR /app/backend/TinyUrl.Api
RUN dotnet publish -c Release -o /app/out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build-env /app/out .

# Expose port 8080 (Render's default port)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "TinyUrl.Api.dll"]
