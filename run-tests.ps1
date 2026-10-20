<#
.SYNOPSIS
    Runs unit tests for the WiFiSurvey application.

.DESCRIPTION
    This script restores NuGet packages, builds the solution, and runs all unit tests
    with detailed output and code coverage reporting.

.REQUIREMENTS
    - PowerShell 7.0+
    - .NET 8.0 SDK
    - WiFiSurvey solution

.EXAMPLE
    .\run-tests.ps1
    Runs all tests with standard output

.EXAMPLE
    .\run-tests.ps1 -Verbose
    Runs all tests with detailed verbose output

.EXAMPLE
    .\run-tests.ps1 -Filter "HeatmapGenerator"
    Runs only HeatmapGenerator tests

.EXAMPLE
    .\run-tests.ps1 -Coverage
    Runs tests with code coverage report
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [string]$Filter = "",

    [Parameter(Mandatory=$false)]
    [switch]$Coverage,

    [Parameter(Mandatory=$false)]
    [switch]$Verbose
)

$ErrorActionPreference = 'Stop'

# Get script directory
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionPath = Join-Path $scriptPath "WiFiSurvey.sln"
$testProjectPath = Join-Path $scriptPath "WiFiSurvey.Tests"

Write-Host "WiFiSurvey Test Runner" -ForegroundColor Cyan
Write-Host "======================" -ForegroundColor Cyan
Write-Host ""

# Verify .NET SDK is installed
try {
    $dotnetVersion = dotnet --version
    Write-Host "Using .NET SDK version: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Error ".NET SDK not found. Please install .NET 8.0 SDK from https://dotnet.microsoft.com/download"
    exit 1
}

# Verify solution exists
if (-not (Test-Path $solutionPath)) {
    Write-Error "Solution file not found: $solutionPath"
    exit 1
}

Write-Host ""
Write-Host "Step 1: Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore $solutionPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to restore packages"
    exit 1
}

Write-Host ""
Write-Host "Step 2: Building solution..." -ForegroundColor Yellow
dotnet build $solutionPath --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed"
    exit 1
}

Write-Host ""
Write-Host "Step 3: Running tests..." -ForegroundColor Yellow

# Build test command
$testArgs = @(
    "test"
    $testProjectPath
    "--configuration", "Release"
    "--no-build"
    "--no-restore"
)

# Add filter if specified
if ($Filter) {
    $testArgs += "--filter"
    $testArgs += "FullyQualifiedName~$Filter"
    Write-Host "Filter: $Filter" -ForegroundColor Cyan
}

# Add verbosity
if ($Verbose) {
    $testArgs += "--logger"
    $testArgs += "console;verbosity=detailed"
} else {
    $testArgs += "--logger"
    $testArgs += "console;verbosity=normal"
}

# Add coverage if requested
if ($Coverage) {
    $testArgs += "/p:CollectCoverage=true"
    $testArgs += "/p:CoverageReportFormat=opencover"
    $testArgs += "/p:CoverageOutputPath=../coverage/coverage.opencover.xml"
    Write-Host "Code coverage enabled" -ForegroundColor Cyan
}

# Run tests
& dotnet @testArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Error "Tests failed!"
    exit 1
}

Write-Host ""
Write-Host "All tests passed successfully!" -ForegroundColor Green

# Show coverage summary if enabled
if ($Coverage) {
    $coveragePath = Join-Path $scriptPath "coverage\coverage.opencover.xml"
    if (Test-Path $coveragePath) {
        Write-Host ""
        Write-Host "Coverage report generated at: $coveragePath" -ForegroundColor Cyan
        Write-Host "To view HTML report, install ReportGenerator:" -ForegroundColor Yellow
        Write-Host "  dotnet tool install -g dotnet-reportgenerator-globaltool" -ForegroundColor Gray
        Write-Host "  reportgenerator -reports:coverage\coverage.opencover.xml -targetdir:coverage\html" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "------------" -ForegroundColor Cyan
Write-Host "Total test files: 4" -ForegroundColor White
Write-Host "  - HeatmapGeneratorTests.cs" -ForegroundColor Gray
Write-Host "  - MeasurementPointTests.cs" -ForegroundColor Gray
Write-Host "  - SurveyProjectTests.cs" -ForegroundColor Gray
Write-Host "  - FloorPlanTests.cs" -ForegroundColor Gray
Write-Host ""
Write-Host "For more options, run: dotnet test --help" -ForegroundColor Gray
