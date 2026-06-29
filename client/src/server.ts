import {
  AngularNodeAppEngine,
  createNodeRequestHandler,
  isMainModule,
  writeResponseToNodeResponse,
} from '@angular/ssr/node';
import express from 'express';
import { createProxyMiddleware } from 'http-proxy-middleware';
import { join } from 'node:path';

const browserDistFolder = join(import.meta.dirname, '../browser');

const app = express();
const angularApp = new AngularNodeAppEngine();

/**
 * Proxy /api/* to the C# backend.
 * Mount at '/' with pathFilter so the /api prefix is preserved in the forwarded URL.
 */
const apiBaseUrl = process.env['API_BASE_URL'] || 'http://localhost:8080';
app.use(
  createProxyMiddleware({
    target: apiBaseUrl,
    changeOrigin: true,
    pathFilter: '/api',
    on: {
      error: (err: Error, _req: any, res: any) => {
        console.error('[proxy] error:', err.message);
        if (typeof res.headersSent !== 'undefined' && !res.headersSent) {
          res.writeHead(502, { 'Content-Type': 'application/json' });
          res.end(JSON.stringify({ error: 'Proxy error', message: err.message }));
        }
      },
    },
  }),
);

/**
 * Diagnostics: test direct connectivity from this Node server to the C# API.
 * Authenticated — requires X-Admin-Key header matching ADMIN_KEY env var.
 * Fails closed: if ADMIN_KEY is unset, every request is rejected.
 */
app.get('/proxy-diagnostics', async (req: any, res: any) => {
  const adminKey = process.env['ADMIN_KEY'];
  if (!adminKey || req.get('X-Admin-Key') !== adminKey) {
    res.status(401).json({ ok: false, error: 'Unauthorized' });
    return;
  }
  const target = `${apiBaseUrl}/health`;
  try {
    const response = await fetch(target);
    const body = await response.text();
    res.json({ ok: response.ok, status: response.status, target, body: body.slice(0, 500) });
  } catch (err: any) {
    res.status(502).json({ ok: false, target, error: err.message });
  }
});

/**
 * Serve static files from /browser
 */
app.use(
  express.static(browserDistFolder, {
    maxAge: '1y',
    index: false,
    redirect: false,
  }),
);

/**
 * Handle all other requests by rendering the Angular application.
 */
app.use((req, res, next) => {
  angularApp
    .handle(req)
    .then((response) =>
      response ? writeResponseToNodeResponse(response, res) : next(),
    )
    .catch(next);
});

/**
 * Start the server if this module is the main entry point, or it is ran via PM2.
 * The server listens on the port defined by the `PORT` environment variable, or defaults to 4000.
 */
if (isMainModule(import.meta.url) || process.env['pm_id']) {
  const port = process.env['PORT'] || 4000;
  app.listen(port, (error) => {
    if (error) {
      throw error;
    }

    console.log(`Node Express server listening on http://localhost:${port}`);
  });
}

/**
 * Request handler used by the Angular CLI (for dev-server and during build) or Firebase Cloud Functions.
 */
export const reqHandler = createNodeRequestHandler(app);
