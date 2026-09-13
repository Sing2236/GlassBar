$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'GlassBar.csproj'
dotnet run --project $project
