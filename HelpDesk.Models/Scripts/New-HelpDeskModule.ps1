#Requires -Version 5.1
<#
.SYNOPSIS
  Scaffolds a new HelpDesk.{Name} Razor class library (Books-style) and optionally wires HelpDesk.Web.

.DESCRIPTION
  Prompts for a module name and for A (library + solution only) or B (also patch HelpDesk.Web).
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-Utf8 {
    param([string]$Path)
    [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Write-Utf8 {
    param([string]$Path, [string]$Text)
    # UTF-8 without a byte-order mark, exactly as given (no trimming), so untouched lines stay identical.
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-RepoRoot {
    param([string]$StartDir)
    $dir = (Resolve-Path $StartDir).Path
    while ($true) {
        $sln = Join-Path $dir 'HelpDesk.sln'
        if (Test-Path -LiteralPath $sln) { return $dir }
        $parent = Split-Path $dir -Parent
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $dir) {
            throw "Could not find HelpDesk.sln walking up from $StartDir"
        }
        $dir = $parent
    }
}

function ConvertTo-PascalCase {
    param([string]$Raw)
    $Raw = $Raw.Trim()
    if ([string]::IsNullOrWhiteSpace($Raw)) { return $null }
    $parts = $Raw -split '[^a-zA-Z0-9]+' | Where-Object { $_ -ne '' }
    if ($parts.Count -eq 0) { return $null }
    ($parts | ForEach-Object {
        $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1).ToLowerInvariant()
    }) -join ''
}

function ConvertTo-CamelCase {
    param([string]$Pascal)
    if ([string]::IsNullOrEmpty($Pascal)) { return $Pascal }
    $Pascal.Substring(0, 1).ToLowerInvariant() + $Pascal.Substring(1)
}

function ConvertTo-KebabCase {
    param([string]$Pascal)
    if ([string]::IsNullOrEmpty($Pascal)) { return '' }
    $s = [regex]::Replace($Pascal, '(?<!^)(?=[A-Z0-9])', '-')
    return $s.ToLowerInvariant()
}

function Test-CSharpIdentifier {
    param([string]$Name)
    if ([string]::IsNullOrEmpty($Name)) { return $false }
    if ($Name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { return $false }
    return $true
}

function Add-AdditionalAssemblyToSource {
    param(
        [string]$Content,
        [string]$ModulePascal
    )
    $typeRef = "typeof(HelpDesk.$ModulePascal.Models.Data.${ModulePascal}DbContext)"
    if ($Content -match [regex]::Escape($typeRef)) {
        return @{ Content = $Content; Changed = $false }
    }
    if ($Content -match 'AdditionalAssemblies\s*=\s*"new\[\]\s*\{\s*([^}]+)\s*\}"') {
        $inner = $Matches[1].Trim()
        $newInner = "$inner, $typeRef.Assembly"
        $newContent = $Content -replace 'AdditionalAssemblies\s*=\s*"new\[\]\s*\{\s*[^}]+\s*\}"', "AdditionalAssemblies=`"new[] { $newInner }`""
        return @{ Content = $newContent; Changed = $true }
    }
    throw "Could not parse AdditionalAssemblies in source; update Routes.razor / Program.cs manually."
}

function Add-ProgramAdditionalAssemblies {
    param(
        [string]$Content,
        [string]$ModulePascal
    )
    $typeRef = "typeof(HelpDesk.$ModulePascal.Models.Data.${ModulePascal}DbContext)"
    if ($Content -match [regex]::Escape($typeRef)) {
        return @{ Content = $Content; Changed = $false }
    }
    # The argument list itself contains parentheses (typeof(...)), so match up to the statement's closing ");".
    $pattern = '\.AddAdditionalAssemblies\((.*?)\)\s*;'
    $m = [regex]::Match($Content, $pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $m.Success) {
        throw "Could not find .AddAdditionalAssemblies(...) in Program.cs"
    }
    $inner = $m.Groups[1].Value.Trim()
    if ($inner -match [regex]::Escape($ModulePascal + 'DbContext')) {
        return @{ Content = $Content; Changed = $false }
    }
    $newInner = "$inner,`r`n    $typeRef.Assembly"
    $replacement = ".AddAdditionalAssemblies($newInner);"
    $newContent = $Content.Remove($m.Index, $m.Length).Insert($m.Index, $replacement)
    return @{ Content = $newContent; Changed = $true }
}

$repoRoot = Get-RepoRoot -StartDir $PSScriptRoot
Set-Location $repoRoot

$rawName = Read-Host 'Enter module name (e.g. Inventory, AssetTracking)'
$pascal = ConvertTo-PascalCase -Raw $rawName
if ($null -eq $pascal) {
    Write-Error 'Module name is required.'
    exit 1
}
if (-not (Test-CSharpIdentifier -Name $pascal)) {
    Write-Error "Invalid module name for C# identifiers: $pascal"
    exit 1
}

$projectDir = Join-Path $repoRoot "HelpDesk.$pascal"
if (Test-Path -LiteralPath $projectDir) {
    Write-Error "Folder already exists: $projectDir"
    exit 1
}

do {
    $scope = (Read-Host 'Choose A = library + solution only, B = also wire HelpDesk.Web (enter A or B)').Trim().ToUpperInvariant()
} while ($scope -ne 'A' -and $scope -ne 'B')

$kebab = ConvertTo-KebabCase -Pascal $pascal
$camel = ConvertTo-CamelCase -Pascal $pascal
$csprojName = "HelpDesk.$pascal.csproj"

# --- Project files ---
New-Item -ItemType Directory -Path $projectDir | Out-Null
New-Item -ItemType Directory -Path (Join-Path $projectDir 'Extensions') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $projectDir 'Models\Data') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $projectDir 'wwwroot') | Out-Null
Set-Content -LiteralPath (Join-Path $projectDir 'wwwroot\.gitkeep') -Value '' -Encoding utf8

$dbCtx = @"
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.$pascal.Models.Data;

/// <summary>
/// EF Core context for the external $pascal SQL database.
/// </summary>
public sealed class ${pascal}DbContext : DbContext
{
    public ${pascal}DbContext(DbContextOptions<${pascal}DbContext> options)
        : base(options)
    {
    }
}
"@

$dbFactory = @"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace HelpDesk.$pascal.Models.Data;

/// <summary>
/// Design-time factory for $pascal database migrations and tooling.
/// </summary>
public class ${pascal}DbContextFactory : IDesignTimeDbContextFactory<${pascal}DbContext>
{
    /// <inheritdoc />
    public ${pascal}DbContext CreateDbContext(string[] args)
    {
        var basePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "HelpDesk.Web"));
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(typeof(${pascal}DbContextFactory).Assembly)
            .AddEnvironmentVariables()
            .Build();

        var cs = configuration.GetConnectionString("$pascal")
            ?? throw new InvalidOperationException("Connection string '$pascal' is not configured.");

        var options = new DbContextOptionsBuilder<${pascal}DbContext>()
            .UseSqlServer(cs)
            .Options;

        return new ${pascal}DbContext(options);
    }
}
"@

$extensions = @"
using HelpDesk.$pascal.Models.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HelpDesk.$pascal.Extensions;

/// <summary>
/// Extension methods for registering HelpDesk.$pascal services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds HelpDesk.$pascal services to the dependency injection container.
    /// </summary>
    public static IServiceCollection AddHelpDesk$pascal(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<${pascal}DbContext>(options =>
            options.UseSqlServer(connectionString), ServiceLifetime.Scoped);

        return services;
    }
}

/// <summary>
/// Extension methods for configuring HelpDesk.$pascal in the application pipeline.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Maps HelpDesk.$pascal routes and static files.
    /// </summary>
    public static WebApplication MapHelpDesk$pascal(this WebApplication app)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.EmbeddedFileProvider(
                typeof(ServiceCollectionExtensions).Assembly,
                "HelpDesk.$pascal.wwwroot"),
            RequestPath = new Microsoft.AspNetCore.Http.PathString("/$kebab-assets")
        });

        return app;
    }
}
"@

$moduleMarker = @"
namespace HelpDesk.$pascal.Models;

/// <summary>Entity types for this module belong in this namespace.</summary>
public static class Module
{
}
"@

$imports = @"
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using System.Net.Http
@using System.Net.Http.Json
@using HelpDesk.$pascal.Models
@using HelpDesk.$pascal.Models.Data
"@

$csproj = @"
<Project Sdk="Microsoft.NET.Sdk.Razor">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.11" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.11">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="wwwroot\**\*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\HelpDesk.Models\HelpDesk.Models.csproj" />
  </ItemGroup>

</Project>
"@

Write-Utf8 -Path (Join-Path $projectDir $csprojName) -Text ($csproj.TrimEnd() + "`r`n")
Write-Utf8 -Path (Join-Path $projectDir 'Models\Module.cs') -Text ($moduleMarker.TrimEnd() + "`r`n")
Write-Utf8 -Path (Join-Path $projectDir "Models\Data\${pascal}DbContext.cs") -Text ($dbCtx.TrimEnd() + "`r`n")
Write-Utf8 -Path (Join-Path $projectDir "Models\Data\${pascal}DbContextFactory.cs") -Text ($dbFactory.TrimEnd() + "`r`n")
Write-Utf8 -Path (Join-Path $projectDir 'Extensions\ServiceCollectionExtensions.cs') -Text ($extensions.TrimEnd() + "`r`n")
Write-Utf8 -Path (Join-Path $projectDir '_Imports.razor') -Text ($imports.TrimEnd() + "`r`n")

$relProj = "HelpDesk.$pascal\$csprojName"
& dotnet sln (Join-Path $repoRoot 'HelpDesk.sln') add $relProj
if ($LASTEXITCODE -ne 0) { throw "dotnet sln add failed" }

if ($scope -eq 'B') {
    $webCsproj = Join-Path $repoRoot 'HelpDesk.Web\HelpDesk.Web.csproj'
    $webXml = Read-Utf8 -Path $webCsproj
    $projRefLine = "    <ProjectReference Include=`"..\HelpDesk.$pascal\HelpDesk.$pascal.csproj`" />"
    if ($webXml -notmatch [regex]::Escape("HelpDesk.$pascal\HelpDesk.$pascal.csproj")) {
        $webXml = $webXml -replace '(<ProjectReference Include="\.\.\\HelpDesk\.Books\\HelpDesk\.Books\.csproj" />)', "`$1`r`n$projRefLine"
        Write-Utf8 -Path $webCsproj -Text $webXml
    } else {
        Write-Warning 'HelpDesk.Web.csproj already references this project; skipping reference.'
    }

    $placeholderCs = "Server=.;Database=$pascal;MultipleActiveResultSets=true"
    foreach ($cfg in @('appsettings.json', 'appsettings.Development.json')) {
        $cfgPath = Join-Path $repoRoot "HelpDesk.Web\$cfg"
        $json = Read-Utf8 -Path $cfgPath
        if ($json -match "`"$pascal`"\s*:") {
            Write-Warning "Connection string '$pascal' already in $cfg; skipping."
            continue
        }
        if ($json -notmatch '"Books"\s*:\s*"[^"]*"') {
            throw "Could not find Books connection string in $cfgPath"
        }
        $json = $json -replace '("Books"\s*:\s*"[^"]*")(\s*\r?\n\s*\},)', "`$1,`r`n    `"$pascal`": `"$placeholderCs`"`$2"
        Write-Utf8 -Path $cfgPath -Text $json
    }

    $programPath = Join-Path $repoRoot 'HelpDesk.Web\Program.cs'
    $prog = Read-Utf8 -Path $programPath
    $usingLine = "using HelpDesk.$pascal.Extensions;"
    if ($prog -notmatch [regex]::Escape($usingLine)) {
        $prog = $prog -replace '(using HelpDesk\.Books\.Extensions;)', "`$1`r`n$usingLine"
    }
    $regBlock = @"

var ${camel}Cs = builder.Configuration.GetConnectionString("$pascal")
    ?? throw new InvalidOperationException("Connection string '$pascal' is not configured.");
builder.Services.AddHelpDesk$pascal(${camel}Cs);
"@
    if ($prog -notmatch [regex]::Escape("AddHelpDesk$pascal(")) {
        $prog = $prog -replace '(builder\.Services\.AddHelpDeskBooks\(booksCs\);)', "`$1$regBlock"
    }
    if ($prog -notmatch "MapHelpDesk$pascal\(") {
        $prog = $prog -replace '(app\.MapHelpDeskBooks\(\);)', "`$1`r`napp.MapHelpDesk$pascal();"
    }
    $asmResult = Add-ProgramAdditionalAssemblies -Content $prog -ModulePascal $pascal
    $prog = $asmResult.Content
    Write-Utf8 -Path $programPath -Text $prog

    $routesPath = Join-Path $repoRoot 'HelpDesk.Web\Components\Routes.razor'
    $routes = Read-Utf8 -Path $routesPath
    $r = Add-AdditionalAssemblyToSource -Content $routes -ModulePascal $pascal
    Write-Utf8 -Path $routesPath -Text $r.Content
}

Write-Host "Created HelpDesk.$pascal. Building solution..." -ForegroundColor Green
& dotnet build (Join-Path $repoRoot 'HelpDesk.sln') -c Release
exit $LASTEXITCODE
