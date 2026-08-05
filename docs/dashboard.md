# Dashboard

The optional metrics dashboard is a Next.js application in `apps/dashboard/` that provides a browser UI over the control-api REST endpoints.

## Features

- Conversation list with token metrics (baseline, sent-equivalent, savings)
- Turn-level detail views with compression phase breakdowns
- Working memory version tracking
- Budget event history
- Prompt growth charts

## Running locally

```bash
cd apps/dashboard
npm install
npm run dev                  # http://localhost:3000
```

## Configuration

The dashboard talks to control-api at `http://localhost:8130` by default. Override the API base URL:

```bash
NEXT_PUBLIC_API_BASE_URL=http://localhost:8130 npm run dev
```

Development CORS already allows `http://localhost:3000` in `apps/control-api/appsettings.Development.json` (and the Local example). For other hosts, set `Cors:AllowedOrigins` on control-api.

## Production

The dashboard can be built and deployed as a static site or served from any static host. Run `npm run build` to produce the optimized output in `apps/dashboard/out/`.
