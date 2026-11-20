import { defineConfig } from 'cypress';

const baseUrl = process.env['CYPRESS_BASE_URL'] ?? 'http://localhost:4200';

export default defineConfig({
  e2e: {
    baseUrl,
    specPattern: 'cypress/e2e/**/*.cy.ts',
    supportFile: 'cypress/support/e2e.ts',
    video: false,
    screenshotOnRunFailure: true,
    defaultCommandTimeout: 10_000,
    env: {
      mailpitUrl: process.env['CYPRESS_MAILPIT_URL'] ?? 'http://localhost:8025',
    },
    setupNodeEvents(on) {
      on('task', {
        log(message: string) {
          console.log(message);
          return null;
        },
      });
      on('before:browser:launch', (browser, launchOptions) => {
        // When the portal is reached as http://host.docker.internal (Cypress in Docker), let Chrome
        // treat it as a secure origin so the Secure, HttpOnly refresh cookie works as on HTTPS.
        if (browser.family === 'chromium' && !baseUrl.includes('localhost')) {
          launchOptions.args.push(`--unsafely-treat-insecure-origin-as-secure=${baseUrl}`);
        }
        return launchOptions;
      });
    },
  },
});
