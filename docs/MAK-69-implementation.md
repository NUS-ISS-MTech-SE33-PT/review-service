# MAK-69 Implementation Note

## What Was Done
- Added production-only `Strict-Transport-Security` response header middleware.
- Kept API traffic HTTPS at the edge (AWS API Gateway default domain).

## Implemented
- HSTS header on API responses (`max-age=31536000; includeSubDomains`).

## Pending / Notes
- API Gateway default domain TLS policy is AWS-managed and not pinned in this repo.
- No HTTP->HTTPS redirect is added in app code because the public API endpoint is the API Gateway HTTPS endpoint.
