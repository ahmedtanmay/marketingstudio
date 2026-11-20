# Runs the Cypress journeys in the cypress/included Docker image against a running stack:
#   docker compose up -d mongodb mailpit api              (from the repo root)
#   $env:API_URL='http://localhost:8080'; npx ng serve --host 0.0.0.0 --allowed-hosts host.docker.internal   (in portal/)
# then:  ./e2e/run-in-docker.ps1
$e2e = $PSScriptRoot
docker run --rm `
    -v "${e2e}:/src:ro" `
    -v ms-e2e-npm:/root/.npm `
    -e CYPRESS_BASE_URL=http://host.docker.internal:4200 `
    -e CYPRESS_MAILPIT_URL=http://host.docker.internal:8025 `
    -e CYPRESS_INSTALL_BINARY=0 `
    --entrypoint sh cypress/included:15.21.2 `
    -c 'mkdir -p /work && cp -r /src/. /work && cd /work && npm ci --no-audit --no-fund && cypress run --browser chrome'
exit $LASTEXITCODE
