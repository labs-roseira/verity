# Wave 1 — Scaffold da Solution

## Objetivo

Criar a estrutura completa: solution `Verity.CashFlow.sln`, 8 projetos (6 src + 2 tests),
referências entre projetos, pacotes NuGet, `.editorconfig` e `.gitignore`. Ao final,
tudo compila com `dotnet build` limpo.

## Pré-requisitos

- Convenções lidas (`00-conventions.md`).
- .NET 10 SDK (presente: 10.0.400).
- Instalar o csharp-ls: `dotnet tool install --global csharp-ls` (item de ambiente do README).

## Notas importantes

- Os comandos abaixo rodam na raiz do repo (`C:\Users\gusta\RiderProjects\Verity.Test.API`).
- O template existente `Verity.Test.API/` e o PDF do desafio **permanecem intocados**.
- Versões de pacote: os comandos `dotnet add package` instalam a estável mais recente.
  As versões nos csprojs abaixo são as previstas no planejamento — **ajuste ao que o
  comando instalar** e valide no Learn MCP/doc oficial se houver dúvida de breaking changes.

## Estrutura final

```
/
├── Verity.CashFlow.sln
├── .editorconfig
├── .gitignore
├── docs/plans/                                  (já existe)
├── src/
│   ├── Verity.CashFlow.Domain/
│   ├── Verity.CashFlow.Application/
│   ├── Verity.CashFlow.Infrastructure.Persistence/
│   ├── Verity.CashFlow.Infrastructure.Messaging/
│   ├── Verity.CashFlow.Entries.Api/
│   └── Verity.CashFlow.Consolidation.Api/
└── tests/
    ├── Verity.CashFlow.UnitTests/
    └── Verity.CashFlow.IntegrationTests/
```

## Comandos de criação

```powershell
dotnet new sln -n Verity.CashFlow

dotnet new classlib -n Verity.CashFlow.Domain -o src/Verity.CashFlow.Domain -f net10.0
dotnet new classlib -n Verity.CashFlow.Application -o src/Verity.CashFlow.Application -f net10.0
dotnet new classlib -n Verity.CashFlow.Infrastructure.Persistence -o src/Verity.CashFlow.Infrastructure.Persistence -f net10.0
dotnet new classlib -n Verity.CashFlow.Infrastructure.Messaging -o src/Verity.CashFlow.Infrastructure.Messaging -f net10.0
dotnet new web -n Verity.CashFlow.Entries.Api -o src/Verity.CashFlow.Entries.Api -f net10.0
dotnet new web -n Verity.CashFlow.Consolidation.Api -o src/Verity.CashFlow.Consolidation.Api -f net10.0

dotnet new xunit -n Verity.CashFlow.UnitTests -o tests/Verity.CashFlow.UnitTests -f net10.0
dotnet new xunit -n Verity.CashFlow.IntegrationTests -o tests/Verity.CashFlow.IntegrationTests -f net10.0

dotnet sln Verity.CashFlow.sln add src/Verity.CashFlow.Domain src/Verity.CashFlow.Application src/Verity.CashFlow.Infrastructure.Persistence src/Verity.CashFlow.Infrastructure.Messaging src/Verity.CashFlow.Entries.Api src/Verity.CashFlow.Consolidation.Api tests/Verity.CashFlow.UnitTests tests/Verity.CashFlow.IntegrationTests
```

## Referências entre projetos

```powershell
dotnet add src/Verity.CashFlow.Application reference src/Verity.CashFlow.Domain

dotnet add src/Verity.CashFlow.Infrastructure.Persistence reference src/Verity.CashFlow.Application
dotnet add src/Verity.CashFlow.Infrastructure.Messaging reference src/Verity.CashFlow.Application

dotnet add src/Verity.CashFlow.Entries.Api reference src/Verity.CashFlow.Application src/Verity.CashFlow.Infrastructure.Persistence src/Verity.CashFlow.Infrastructure.Messaging
dotnet add src/Verity.CashFlow.Consolidation.Api reference src/Verity.CashFlow.Application src/Verity.CashFlow.Infrastructure.Persistence src/Verity.CashFlow.Infrastructure.Messaging

dotnet add tests/Verity.CashFlow.UnitTests reference src/Verity.CashFlow.Domain src/Verity.CashFlow.Application src/Verity.CashFlow.Infrastructure.Messaging

dotnet add tests/Verity.CashFlow.IntegrationTests reference src/Verity.CashFlow.Entries.Api src/Verity.CashFlow.Consolidation.Api
```

Grafo resultante (aciclo): `Api(s) → Persistence/Messaging → Application → Domain`.

## Pacotes NuGet

```powershell
dotnet add src/Verity.CashFlow.Infrastructure.Persistence package Dapper
dotnet add src/Verity.CashFlow.Infrastructure.Persistence package Microsoft.Data.SqlClient

dotnet add src/Verity.CashFlow.Infrastructure.Messaging package RabbitMQ.Client
dotnet add src/Verity.CashFlow.Infrastructure.Messaging package Microsoft.Extensions.Hosting.Abstractions
dotnet add src/Verity.CashFlow.Infrastructure.Messaging package Microsoft.Extensions.Logging.Abstractions
dotnet add src/Verity.CashFlow.Infrastructure.Messaging package Microsoft.Extensions.Options

dotnet add src/Verity.CashFlow.Entries.Api package Microsoft.AspNetCore.OpenApi
dotnet add src/Verity.CashFlow.Consolidation.Api package Microsoft.AspNetCore.OpenApi

dotnet add tests/Verity.CashFlow.UnitTests package NSubstitute
dotnet add tests/Verity.CashFlow.UnitTests package Shouldly

dotnet add tests/Verity.CashFlow.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Verity.CashFlow.IntegrationTests package Testcontainers.MsSql
dotnet add tests/Verity.CashFlow.IntegrationTests package Testcontainers.RabbitMq
dotnet add tests/Verity.CashFlow.IntegrationTests package Shouldly
dotnet add tests/Verity.CashFlow.IntegrationTests package Dapper
dotnet add tests/Verity.CashFlow.IntegrationTests package Microsoft.Data.SqlClient
```

## csproj finais (referência — sincronizar versões com as instaladas)

**src/Verity.CashFlow.Domain/Verity.CashFlow.Domain.csproj**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

**src/Verity.CashFlow.Application/Verity.CashFlow.Application.csproj** (idem Domain)

**src/Verity.CashFlow.Infrastructure.Persistence/Verity.CashFlow.Infrastructure.Persistence.csproj**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Dapper" Version="2.1.66" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="6.0.2" />
  </ItemGroup>
</Project>
```

**src/Verity.CashFlow.Infrastructure.Messaging/Verity.CashFlow.Infrastructure.Messaging.csproj**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0" />
    <PackageReference Include="RabbitMQ.Client" Version="7.1.2" />
  </ItemGroup>
</Project>
```

**src/Verity.CashFlow.Entries.Api/Verity.CashFlow.Entries.Api.csproj** (Consolidation idem, só muda o nome)
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.11" />
  </ItemGroup>
</Project>
```

**tests/Verity.CashFlow.UnitTests/Verity.CashFlow.UnitTests.csproj**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

**tests/Verity.CashFlow.IntegrationTests/Verity.CashFlow.IntegrationTests.csproj**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Dapper" Version="2.1.66" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="6.0.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.13.0" />
    <PackageReference Include="Shouldly" Version="4.3.0" />
    <PackageReference Include="Testcontainers.MsSql" Version="4.6.0" />
    <PackageReference Include="Testcontainers.RabbitMq" Version="4.6.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
</Project>
```

## Limpeza dos templates

- Remover `Class1.cs` dos 4 classlibs e `UnitTest1.cs` dos 2 projetos de teste.
- Limpar `Program.cs` de ambas as APIs para o conteúdo mínimo (finalizado na Wave 7):

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();

public partial class Program;
```

## `.editorconfig` (raiz do repo)

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true

[*.cs]
indent_style = space
indent_size = 4

[*.{json,yml,yaml,md}]
indent_style = space
indent_size = 2

[*.{csproj,sln}]
indent_style = space
indent_size = 2
```

## `.gitignore` (raiz do repo — padrão Visual Studio)

```gitignore
bin/
obj/
*.user
*.suo
.vs/
.idea/
TestResults/
*.DotSettings.user
appsettings.*.local.json
```

## appsettings iniciais (valor final — ver `contracts.md`)

**src/Verity.CashFlow.Entries.Api/appsettings.json**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "CashFlowDatabase": "Server=localhost,1433;Database=CashFlowDb;User Id=sa;Password=Verity!CashFlow2026;TrustServerCertificate=True"
  },
  "RabbitMq": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest"
  }
}
```

**src/Verity.CashFlow.Consolidation.Api/appsettings.json** — idêntico (mesmo banco e broker).

## Comandos de verificação

```powershell
dotnet build Verity.CashFlow.sln
dotnet test Verity.CashFlow.sln
```

## Critérios de aceite

1. `dotnet build` sem erros.
2. 8 projetos na solution, referências conforme as seções acima (grafo acíclico).
3. `dotnet test` roda (0 testes, 0 falhas).
4. `docs/plans/`, template `Verity.Test.API/` e PDF inalterados.

## Notas / riscos

- `public partial class Program;` nas duas APIs é obrigatório para
  `WebApplicationFactory<Program>` (onda 7).
- RabbitMQ.Client v7 tem API async (`IChannel`, `*Async`) diferente da v6 —
  a API exata é validada nas ondas 4 e 5 com a doc oficial do cliente.
- `dotnet tool install --global csharp-ls` pode exigir PATH atualizado no terminal.
