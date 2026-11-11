# Bimbel Testing: Testcontainers + EF Core + PostgreSQ

Tutorial pengujian database dengan PostgreSQL asli memakai Testcontainers
(README lengkap akan ditulis setelah test selesai)

## Requirement
- .NET SDK 10 (`dotnet --version`)
- Docker Desktop yang sedang berjalan (`docker --version`)
- Tool EF Core: `dotnet tool install --global dotnet-ef`

## Setup 
```bash
dotnet new sln -n BimbelTesting
dotnet sln add src/Bimbel.Data/Bimbel.Data.csproj
dotnet restore
dotnet build

# Migrasi awal
dotnet ef migrations add InitialCreate --project src/Bimbel.Data
```
