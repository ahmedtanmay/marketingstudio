// Dev-server proxy: the portal and API share one origin, as in production (Nginx), so the
// HttpOnly refresh cookie is first-party. API_URL defaults to `dotnet run`; use
// API_URL=http://localhost:8080 when the API runs in Docker (e.g. with Smart App Control on).
export default {
  '/api': {
    target: process.env.API_URL ?? 'http://localhost:5283',
    secure: false,
    changeOrigin: false,
  },
};
