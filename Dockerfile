# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy csproj and restore (use exact name with spaces)
COPY "AI-driven teaching platform.csproj" .
RUN dotnet restore "AI-driven teaching platform.csproj"

# Copy everything and publish
COPY . .
RUN dotnet publish "AI-driven teaching platform.csproj" -c Release -o /app

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

# Railway provides PORT env variable
ENV ASPNETCORE_URLS=http://+:$PORT

# ✅ Use exact DLL name with spaces
ENTRYPOINT ["dotnet", "AI-driven teaching platform.dll"]
