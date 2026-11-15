# Electronics Store

A full-stack electronics store application built with Angular, ASP.NET Core Web API, and PostgreSQL.

The repository contains an Angular single-page application, a .NET 10 backend, automated tests, and a Docker Compose setup for running the complete application.

## Technology stack

| Area | Technologies |
|---|---|
| Frontend | Angular 22, TypeScript, RxJS, HTML, CSS |
| Backend | C#, .NET 10, ASP.NET Core Web API |
| Data access | Entity Framework Core, Npgsql |
| Database | PostgreSQL 18 |
| Authentication | JWT Bearer, role-based authorization, ASP.NET Core password hashing |
| Testing | xUnit, ASP.NET Core integration tests |
| Containers | Docker, Docker Compose |
| CI | GitHub Actions |

## Application capabilities

### Storefront
- Browse the product catalog and product details.
- Search, filter, sort, and paginate products.
- Browse categories and category-specific product specifications.
- Add products to a persistent browser cart.
- Register, sign in, view the current account, and change the password.
- Create orders and view order history.

### Administration
- Create, update, and delete products and categories.
- Upload and replace product and category images.
- Manage category specification templates.
- View all orders and update order statuses.

### Backend
- Validate incoming requests and separate API contracts from database entities with DTOs.
- Enforce role-based access to administrative operations.
- Calculate order totals using current database prices.
- Update order and stock data within database transactions.
- Apply database schema changes through EF Core migrations.

New registrations receive the `User` role. Public registration does not allow a user to assign themselves the `Admin` role. An administrator account must be provisioned through a controlled database administration process.

## Architecture

The Angular application is built as static production assets and copied into the ASP.NET Core `wwwroot` directory during the Docker build.

The API and frontend are served from the same origin:

- Angular pages and static assets are served by ASP.NET Core.
- API requests use relative paths such as `/api/products`.
- Product images are exposed under `/uploads/products`.
- Category images are exposed under `/uploads/categories`.

This avoids a separate frontend container and removes the need to configure CORS for the deployed application. During local development, the Angular development server forwards `/api` and `/uploads` requests to the local API using its proxy configuration.

## Repository layout

```text
ElectronicsStoreApi/
  Controllers/
  Data/
  DTOs/
  Migrations/
  Models/
  Services/
  Program.cs

ElectronicsStoreApi.Tests/
  Infrastructure/
  Unit/
  Intagration/

FrontendAngular/
  public/
  src/
    app/
      core/
      features/
  angular.json
  package.json
  package-lock.json
  proxy.conf.json

Dockerfile
compose.yaml
.dockerignore
.editorconfig
.env.example
ElectronicsStoreApi.slnx
```

The legacy static frontend in the `Frontend/` directory is not used by the current Docker deployment. The active frontend is `FrontendAngular/`.

## Run with Docker Compose

### Requirements

- Docker Desktop with Docker Compose.
- Windows, macOS, or Linux.

### 1. Configure environment variables

From the repository root, create a local environment file:

```powershell
Copy-Item .env.example .env
```

Edit `.env` and set a strong database password. Generate a Base64-encoded JWT signing key with PowerShell:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Copy the generated value into `JWT_KEY` in `.env`.

The `.env` file is ignored by Git and must not be committed.

### 2. Build and start the application

```powershell
docker compose up -d --build
```

Docker Compose performs the following steps:

1. Starts PostgreSQL and waits for its health check.
2. Builds the Angular production application.
3. Builds the ASP.NET Core API.
4. Runs the EF Core migration service.
5. Starts the API after the migrations complete successfully.

Check the service state:

```powershell
docker compose ps -a
```

The migration service is expected to exit with code `0` after applying pending migrations.

### 3. Open the application

Open:

- Storefront: [http://localhost:8081](http://localhost:8081)
- Example API endpoint: [http://localhost:8081/api/products](http://localhost:8081/api/products)

The Docker Compose API container uses the Production environment. Swagger is therefore disabled in this configuration.

### 4. Open the application from another device on the same network

The computer running Docker and the other device must be connected to the same local network.

1. Find the computer's local IPv4 address with `ipconfig`.
2. Locate the IPv4 address of the active Wi-Fi or Ethernet adapter.
3. On the other device, open `http://<COMPUTER-IP>:8081`, replacing the placeholder with that IPv4 address.

For example:

```text
http://192.168.1.100:8081
```

If Windows Firewall blocks the connection, add an inbound rule for TCP port `8081` on the Private network profile. Run PowerShell as Administrator:

```powershell
New-NetFirewallRule -DisplayName "Electronics Store - HTTP 8081" -Direction Inbound -Protocol TCP -LocalPort 8081 -Action Allow -Profile Private
```

This setup is intended for development or demonstration on a trusted local network. HTTP traffic is not encrypted. Do not expose this configuration directly to the public internet; public deployment requires HTTPS, a domain or public IP, and an appropriately configured reverse proxy.

### Docker ports and persistent data

| Host address | Purpose |
|---|---|
| `localhost:8081` | Angular frontend and ASP.NET Core API |
| `localhost:5435` | PostgreSQL, bound to the host loopback interface only |

The following named volumes persist data when containers are recreated:

- `electronics-store-db-v18` — PostgreSQL 18 database files.
- `electronics-store-product-images` — uploaded product images.
- `electronics-store-category-images` — uploaded category images.

Stop the containers without deleting persistent data:

```powershell
docker compose down
```

To remove containers and all named volumes, including the database and uploaded images:

```powershell
docker compose down -v
```

**Warning:** `docker compose down -v` removes the named volumes declared by the current Compose configuration, including the active database and uploaded-image volumes. This permanently deletes the data stored in those volumes. Do not use it as a routine cleanup command.

### PostgreSQL 17 to 18 data migration

The current configuration uses PostgreSQL 18 and a new volume named `electronics-store-db-v18`, mounted at `/var/lib/postgresql`. Earlier versions of this project used PostgreSQL 17 and a different volume mounted at `/var/lib/postgresql/data`.

Docker Compose does **not** copy database files between these volumes. Starting PostgreSQL 18 without restoring a backup creates a new, empty database. The old PostgreSQL 17 volume is not the active database volume in the new configuration.

For an existing PostgreSQL 17 installation, create a logical backup **before** switching to the PostgreSQL 18 Compose configuration. The following commands assume the old PostgreSQL 17 `db` service is still running and the root `.env` file points to that installation.

1. Create a custom-format backup inside the PostgreSQL 17 container and copy it to your Windows user profile:

```powershell
$backupPath = Join-Path $env:USERPROFILE "electronics-store-pg17.dump"

docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -f /tmp/electronics-store-pg17.dump'
if ($LASTEXITCODE -ne 0) { throw "PostgreSQL 17 backup failed." }

docker compose cp db:/tmp/electronics-store-pg17.dump $backupPath
if ($LASTEXITCODE -ne 0) { throw "Could not copy the PostgreSQL backup to the host." }
```

2. Switch to the PostgreSQL 18 configuration. Stop the old stack without deleting its volumes, then start only the new database:

```powershell
docker compose down
docker compose up -d --wait db
```

3. Copy the backup into the PostgreSQL 18 container and restore it into the database created from `.env`:

```powershell
docker compose cp $backupPath db:/tmp/electronics-store-pg17.dump

docker compose exec -T db sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists --no-owner /tmp/electronics-store-pg17.dump'
if ($LASTEXITCODE -ne 0) { throw "PostgreSQL 18 restore failed." }
```

4. Start the complete application. The migration service will apply any migrations that are not yet present in the restored database:

```powershell
docker compose up -d --build
docker compose ps -a
```

Keep the backup file and the old PostgreSQL 17 Docker volume until you have checked that the application data is present and the application works correctly. Product and category image volumes are separate from the database volume and are not part of this database dump.

**Already started PostgreSQL 18 without making a PostgreSQL 17 backup?** Do not delete the old volume and do not mount a PostgreSQL 17 data directory directly into a PostgreSQL 18 container. The old volume must be started with a matching PostgreSQL 17 image so that a logical backup can be exported and restored into PostgreSQL 18. Do not run `docker compose down -v` or `docker volume prune` while recovering old data.

### Useful Docker commands

```powershell
docker compose ps -a
docker compose logs -f api
docker compose logs migrate
docker compose logs db
docker compose restart api
docker compose down
```

## Local development

### Requirements

- .NET 10 SDK.
- Node.js 26.x and npm.
- Docker Desktop with Docker Compose.
- An editor such as Visual Studio or Visual Studio Code.

Angular 22 supports Node.js 26. The official compatibility table is available in the [Angular version compatibility guide](https://angular.dev/reference/versions).

### 1. Start PostgreSQL

Create and configure the root `.env` file as described in the Docker section, then start only the database:

```powershell
docker compose up -d db
```

PostgreSQL is available to local development tools at `localhost:5435`.

### 2. Configure backend secrets

Initialize .NET User Secrets once:

```powershell
dotnet user-secrets init --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj
```

Set the database connection string using the values from the running PostgreSQL container. This keeps the local API connected to the database selected in your `.env` file, even when its name is not `electronics_store`:

```powershell
$pgDatabase = (docker compose exec -T db printenv POSTGRES_DB).Trim()
$pgUser = (docker compose exec -T db printenv POSTGRES_USER).Trim()
$pgPassword = (docker compose exec -T db printenv POSTGRES_PASSWORD).Trim()
$connectionString = "Host=localhost;Port=5435;Database=$pgDatabase;Username=$pgUser;Password=$pgPassword"

dotnet user-secrets set "ConnectionStrings:DefaultConnection" $connectionString --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj
```

Set the JWT issuer, audience, and key. Generate a key using the PowerShell command from the Docker section:

```powershell
dotnet user-secrets set "Jwt:Issuer" "ElectronicsStoreApi" --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj
dotnet user-secrets set "Jwt:Audience" "ElectronicsStoreClient" --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj
dotnet user-secrets set "Jwt:Key" "YOUR_BASE64_SIGNING_KEY" --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj
```

Do not commit real secrets or database passwords to source control.

### 3. Apply migrations and run the API

Install the EF Core command-line tool once. The version matches the EF Core tooling used by this project:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.14
```

If that version is already installed, this step can be skipped. To update an existing global installation to this version, use `dotnet tool update --global dotnet-ef --version 10.0.14`.

Then restore the solution, apply migrations, and start the API:

```powershell
dotnet restore .\ElectronicsStoreApi.slnx

dotnet ef database update --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj --startup-project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj

dotnet run --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj --launch-profile http
```

The API listens at `http://localhost:5248`. In the Development environment, Swagger UI is available at `http://localhost:5248/swagger`.

### 4. Run the Angular application

In a separate terminal:

```powershell
Set-Location .\FrontendAngular
npm.cmd ci
npm.cmd start
```

Open [http://localhost:4200](http://localhost:4200). The Angular development proxy forwards API and uploaded-image requests to `http://localhost:5248`.

To make the Angular development server reachable from other devices on the same local network:

```powershell
npm.cmd start -- --host 0.0.0.0
```

Then open `http://<COMPUTER-IP>:4200` on the other device and allow inbound TCP port `4200` in Windows Firewall if required. The local API remains behind the Angular proxy.

## Build and tests

Run backend commands from the repository root.

Build the backend:

```powershell
dotnet build .\\ElectronicsStoreApi.slnx --configuration Release
```

### Backend tests

The complete backend test suite includes four tests that run against a real PostgreSQL database. They require the `ELECTRONICS_STORE_TEST_CONNECTION` environment variable. Use a dedicated test database; never point this variable at the database containing your application data.

Start PostgreSQL and wait for its health check:

```powershell
docker compose up -d --wait db
```

Create the test database if it does not already exist, then set the connection string for the current PowerShell session:

```powershell
$pgUser = (docker compose exec -T db printenv POSTGRES_USER).Trim()
$pgPassword = (docker compose exec -T db printenv POSTGRES_PASSWORD).Trim()

$testDatabaseExists = (docker compose exec -T db psql -U $pgUser -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'electronics_store_tests'").Trim()
if ($testDatabaseExists -ne "1") {
    docker compose exec -T db createdb -U $pgUser electronics_store_tests
}

$env:ELECTRONICS_STORE_TEST_CONNECTION = "Host=localhost;Port=5435;Database=electronics_store_tests;Username=$pgUser;Password=$pgPassword"
```

The environment variable is session-specific. Repeat the assignment in each new PowerShell window before running the full test suite:

```powershell
dotnet test .\\ElectronicsStoreApi.slnx --configuration Release
```

The PostgreSQL-backed tests create their schema in `electronics_store_tests`. Their data is isolated from the application's configured database.

### Angular frontend build and tests

Run the following commands from the `FrontendAngular` directory:

```powershell
Set-Location .\\FrontendAngular
npm.cmd ci
npm.cmd run build
```

Run Angular unit tests once, without entering watch mode:

```powershell
npm.cmd test -- --watch=false
```

The Angular unit-test command is available locally. The current GitHub Actions workflow runs the .NET tests and builds the Docker images (which also builds the Angular production bundle); it does not currently execute Angular unit tests.

### Formatting

Format C# files from the repository root:

```powershell
dotnet format whitespace .\\ElectronicsStoreApi.slnx
```

Check C# formatting without changing files:

```powershell
dotnet format whitespace .\\ElectronicsStoreApi.slnx --verify-no-changes
```

From `FrontendAngular`, format Angular source files and check formatting:

```powershell
npm.cmd run format
npm.cmd run format:check
```

GitHub Actions validates the .NET solution and its tests, checks the Docker Compose configuration, and builds both Docker targets. The Docker build includes the optimized Angular production bundle.

## API endpoints

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/auth/register` | Register a user |
| POST | `/api/auth/login` | Authenticate and receive a JWT |
| GET | `/api/auth/me` | Get the current user's profile |
| POST | `/api/auth/change-password` | Change the current user's password |
| GET | `/api/products` | List, search, filter, sort, and paginate products |
| GET | `/api/products/{id}` | Get a product |
| GET | `/api/products/by-category/{categoryId}` | Get products in a category |
| POST | `/api/products` | Create a product |
| PUT | `/api/products/{id}` | Update a product |
| DELETE | `/api/products/{id}` | Delete a product |
| POST | `/api/products/{productId}/image` | Upload or replace a product image |
| DELETE | `/api/products/{productId}/image` | Delete a product image |
| GET | `/api/categories` | List categories |
| GET | `/api/categories/{id}` | Get a category |
| POST | `/api/categories` | Create a category |
| PUT | `/api/categories/{id}` | Update a category |
| DELETE | `/api/categories/{id}` | Delete a category |
| POST | `/api/categories/{categoryId}/image` | Upload or replace a category image |
| DELETE | `/api/categories/{categoryId}/image` | Delete a category image |
| GET | `/api/categories/{categoryId}/specifications` | List category specifications |
| POST | `/api/categories/{categoryId}/specifications` | Create a category specification |
| PUT | `/api/categories/{categoryId}/specifications/{specificationId}` | Update a category specification |
| DELETE | `/api/categories/{categoryId}/specifications/{specificationId}` | Delete a category specification |
| GET | `/api/orders` | Get the current user's orders |
| GET | `/api/orders/admin` | Get all orders for administration |
| POST | `/api/orders` | Create an order |
| PATCH | `/api/orders/{id}/status` | Update an order status |

Product and category write operations, image management, category specification changes, and order administration require the `Admin` role. Order retrieval and creation require authentication. Public catalog and category retrieval endpoints are available without authentication.

## Code style

- `.editorconfig` defines indentation, line endings, and core C# formatting conventions.
- `FrontendAngular/.prettierrc.json` defines frontend formatting.
- Angular formatting commands are available through the `format` and `format:check` npm scripts.

Format C# files:

```powershell
dotnet format whitespace .\ElectronicsStoreApi.slnx
```

Check C# formatting without changing files:

```powershell
dotnet format whitespace .\ElectronicsStoreApi.slnx --verify-no-changes
```
