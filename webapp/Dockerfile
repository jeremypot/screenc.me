# Use the official ASP.NET Core runtime Alpine image as base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS base
WORKDIR /app
EXPOSE 8080

# Install system dependencies for basic operations (Alpine packages)
RUN apk add --no-cache \
    file \
    wget

# Use SDK Alpine image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project file and restore dependencies
COPY ["ScreenConnect.WebApp.csproj", "."]
RUN dotnet restore "./ScreenConnect.WebApp.csproj"

# Install Rust for building the self-extractor (Alpine packages)
RUN apk add --no-cache \
    build-base \
    mingw-w64-gcc \
    curl \
    bash
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y
ENV PATH="/root/.cargo/bin:${PATH}"
RUN rustup target add x86_64-pc-windows-gnu

# Copy and build Rust SelfExtractor project
COPY SelfExtractorRust/ ./SelfExtractorRust/
WORKDIR /src/SelfExtractorRust

RUN cargo build --release --target x86_64-pc-windows-gnu
RUN mkdir -p ../SelfExtractorRust-output/
RUN cp target/x86_64-pc-windows-gnu/release/screenconnect-extractor.exe ../SelfExtractorRust-output/

# Return to main project directory
WORKDIR /src

# Copy app
COPY . .

# Clean any existing build artifacts and build the application
RUN dotnet clean "ScreenConnect.WebApp.csproj"
RUN dotnet build "ScreenConnect.WebApp.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./ScreenConnect.WebApp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage - copy published app to runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Copy Rust self-extractor template to final image
COPY --from=build /src/SelfExtractorRust-output/screenconnect-extractor.exe ./SelfExtractorRust/

# Create necessary directories
RUN mkdir -p /tmp/screenconnect && \
    chown -R 1654:1654 /tmp/screenconnect && \
    chmod 755 /tmp/screenconnect

# Set user for security (use numeric ID for Alpine)
USER 1654

# Start the application
ENTRYPOINT ["dotnet", "ScreenConnect.WebApp.dll"] 