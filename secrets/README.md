# Test Secrets

The files in this directory are self-signed development certificates and secret
placeholders that allow the docker-compose stack to exercise TLS, mTLS and
key-per-file configuration reloads. They are *not* production-grade secrets.

* `certs/` holds the root CA plus issued certificates for the gateway,
  products API, the gateway's mTLS client identity, and the Postgres server.
* `config/` contains key-per-file entries that ASP.NET Core consumes via the
  `AddKeyPerFile` provider (for example `ConnectionStrings__ProductsDb`).

Pentesters can swap any of these files with their own certificates to simulate
rotation events without changing application code.
