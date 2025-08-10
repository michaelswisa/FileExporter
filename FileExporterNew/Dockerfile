# Use the official .NET SDK image for building the application
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies
COPY FileExporterNew/FileExporterNew.csproj ./FileExporterNew/
RUN dotnet restore FileExporterNew/FileExporterNew.csproj

# Copy the rest of the application source code
COPY . .
WORKDIR /src/FileExporterNew

# Publish the application
RUN dotnet publish -c Release -o /app/publish

# Copy certificates to a temporary location for installation
COPY certificates /tmp/certificates/

# Use the official .NET ASP.NET runtime image for running the application
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS final
WORKDIR /app

# Install certificates
COPY --from=build /tmp/certificates/ /usr/local/share/ca-certificates/
RUN update-ca-certificates

# Copy the published application from the build stage
COPY --from=build /app/publish .

# Expose the port the application listens on
EXPOSE 80

# Define the entry point for the application
ENTRYPOINT ["dotnet", "FileExporterNew.dll"]
