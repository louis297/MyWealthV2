# Caddy with the caddy-ratelimit module (docs/deployment.md §11). Built on the VPS by deploy.sh.
# The stock Caddy image has no rate limiting.
FROM caddy:2.10.2-builder AS builder

RUN xcaddy build \
    --with github.com/mholt/caddy-ratelimit

FROM caddy:2.10.2

COPY --from=builder /usr/bin/caddy /usr/bin/caddy
