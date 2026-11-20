# Runs the test suites inside the .NET 10 SDK Linux container (same as CI).
# Use this on Windows machines where Smart App Control blocks unsigned NuGet DLLs (e.g. MongoDB.Bson).
# Requires Docker Desktop. Testcontainers starts MongoDB as a sibling container.
param(
    [string]$Project = "tests/MarketingStudio.IntegrationTests"
)

$repo = Split-Path -Parent $PSScriptRoot
$script = 'mkdir -p /work && cd /src && tar --exclude=bin --exclude=obj --exclude=.local -cf - . | tar -xf - -C /work ' +
          "&& cd /work && dotnet test $Project -nologo"

docker run --rm `
    -v "${repo}:/src:ro" `
    -v //var/run/docker.sock:/var/run/docker.sock `
    -v ms-nuget-cache:/root/.nuget/packages `
    -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal `
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 `
    mcr.microsoft.com/dotnet/sdk:10.0 sh -c $script

exit $LASTEXITCODE
