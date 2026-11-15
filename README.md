# Electronics Store

A full-stack electronics store application built with Angular, ASP.NET Core Web API, and PostgreSQL.

The repository contains an Angular single-page application, a .NET 10 backend, automated tests, and a Docker Compose setup for running the complete application.

## Technology stack

| Area | Technologies |
|---|---|
| Frontend | Angular 22, TypeScript, RxJS, HTML, CSS |
| Backend | C#, .NET 10, ASP.NET Core Web API |
| Data access | Entity Framework Core, Npgsql |
| Database | PostgreSQL 17 |
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

- `electronics-store-db` — PostgreSQL database files.
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

**Warning:** `docker compose down -v` permanently deletes the application's persisted database and uploaded images.

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

Set the database connection string. Use the same database name, username, and password as in `.env`:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5435;Database=electronics_store;Username=store_user;Password=YOUR_DATABASE_PASSWORD" --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj
```

Set the JWT issuer, audience, and key. Generate a key using the PowerShell command from the Docker section:

```powershell
dotnet user-secrets set "Jwt:Issuer" "ElectronicsStoreApi" --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj
dotnet user-secrets set "Jwt:Audience" "ElectronicsStoreClient" --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj
dotnet user-secrets set "Jwt:Key" "YOUR_BASE64_SIGNING_KEY" --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj
```

Do not commit real secrets or database passwords to source control.

### 3. Apply migrations and run the API

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

Build the backend:

```powershell
dotnet build .\ElectronicsStoreApi.slnx --configuration Release
```

Run the backend test suite:

```powershell
dotnet test .\ElectronicsStoreApi.slnx --configuration Release
```

Build the Angular frontend:

```powershell
Set-Location .\FrontendAngular
npm.cmd ci
npm.cmd run build
```

Format Angular source files with the repository's Prettier configuration:

```powershell
npm.cmd run format
```

Check formatting without changing files:

```powershell
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
