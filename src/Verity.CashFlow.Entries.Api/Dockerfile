FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/Verity.CashFlow.Domain/Verity.CashFlow.Domain.csproj", "src/Verity.CashFlow.Domain/"]
COPY ["src/Verity.CashFlow.Application/Verity.CashFlow.Application.csproj", "src/Verity.CashFlow.Application/"]
COPY ["src/Verity.CashFlow.Infrastructure.Persistence/Verity.CashFlow.Infrastructure.Persistence.csproj", "src/Verity.CashFlow.Infrastructure.Persistence/"]
COPY ["src/Verity.CashFlow.Infrastructure.Messaging/Verity.CashFlow.Infrastructure.Messaging.csproj", "src/Verity.CashFlow.Infrastructure.Messaging/"]
COPY ["src/Verity.CashFlow.Entries.Api/Verity.CashFlow.Entries.Api.csproj", "src/Verity.CashFlow.Entries.Api/"]
RUN dotnet restore "src/Verity.CashFlow.Entries.Api/Verity.CashFlow.Entries.Api.csproj"

COPY . .
RUN dotnet publish "src/Verity.CashFlow.Entries.Api/Verity.CashFlow.Entries.Api.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8080

ENTRYPOINT ["dotnet", "Verity.CashFlow.Entries.Api.dll"]
