FROM ubuntu:22.04 AS shim
RUN apt-get update && apt-get install -y --no-install-recommends gcc libc6-dev && rm -rf /var/lib/apt/lists/*
COPY nodirect.c /src/nodirect.c
RUN gcc -O2 -shared -fPIC -o /src/libnodirect.so /src/nodirect.c -ldl

FROM mcr.microsoft.com/mssql/server:2022-latest
COPY --from=shim /src/libnodirect.so /usr/local/lib/libnodirect.so
ENV LD_PRELOAD=/usr/local/lib/libnodirect.so
