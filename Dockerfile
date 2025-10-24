# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy csproj and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Copy from build stage
COPY --from=build /app/out .

# Expose port (Railway sets PORT env variable)
ENV ASPNETCORE_URLS=http://+:$PORT

# Start the app
ENTRYPOINT ["dotnet", "AI-driven-teaching-platform.dll"]
