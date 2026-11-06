# Electronics Store

A web application simulating an online electronics store. The system provides product catalog management, category management, user authentication, order creation, inventory management, and product image uploads.

The application consists of an ASP.NET Core Web API backend, a PostgreSQL database, and a web-based frontend.

## Technologies

### Backend
- C#
- .NET 10
- ASP.NET Core Web API
- Entity Framework Core
- PostgreSQL
- Npgsql
- JWT Bearer Authentication
- Role-based Authorization

### Frontend
- HTML5
- CSS3
- JavaScript

### Infrastructure and Testing
- Docker
- Docker Compose
- xUnit
- ASP.NET Core Integration Testing
- EF Core Migrations
- GitHub Actions

## Features

### Product Management
- Create, retrieve, update, and delete products.
- Retrieve products by ID or category.
- Search products by name.
- Filter products by category, price range, and stock availability.
- Sort products by supported fields: ID, name, price, and quantity.
- Paginate product listings.
- Manage product prices, previous prices, quantities, descriptions, and images.
- Validate product data and category references.

### Category Management
- Create, retrieve, update, and delete categories.
- Retrieve category information, including product counts.
- Manage category-specific product specifications through the API.
- Create, update, retrieve, and delete category specifications.

### Authentication and Authorization
- Register new users and authenticate with a username and password.
- Hash passwords using ASP.NET Core Identity password-hashing services.
- Issue JWT access tokens.
- Validate token signatures, issuer, audience, and expiration.
- Implement role-based authorization.
- Restrict product, category, and image management operations to administrators.

Newly registered users receive the `User` role. Administrative role assignment is not exposed through public registration.

### Order Processing
- Create orders containing multiple products.
- Associate orders with authenticated users.
- Validate product availability and requested quantities.
- Calculate order totals using current product prices.
- Decrease inventory quantities after successful order creation.
- Save orders and inventory changes within a database transaction.
- Use Serializable transaction isolation to handle concurrent stock changes.
- Return information about products with insufficient stock.

The current API supports order creation. Order history management, status transitions, and payment processing are not included.

### Product Image Management
- Upload and replace product images.
- Delete product images.
- Support JPEG, PNG, and WebP formats.
- Limit image size to 5 MB.
- Validate file signatures.
- Generate unique filenames.
- Store image references in PostgreSQL and image files in persistent file storage.

Image management operations require the administrator role.

### Frontend
- Product catalog.
- Product details page.
- Shopping cart with browser-based persistence using localStorage.
- User authentication.
- Order submission through the backend API.
- Administrative dashboard.
- Product and category management.
- Product image previews and uploads.
- Catalog statistics and product search.

### Database and Data Integrity
- PostgreSQL persistence through Entity Framework Core.
- Database schema management using EF Core migrations.
- Database constraints for product prices and quantities.
- Relationships between products, categories, users, orders, and order items.
- Request and response DTOs separating API contracts from database entities.

## Project Structure

```text
ElectronicsStoreApiPractice/
│
├── ElectronicsStoreApi/
│   ├── Controllers/
│   ├── Data/
│   ├── DTOs/
│   │   ├── Requests/
│   │   └── Response/
│   ├── Migrations/
│   ├── Models/
│   ├── Services/
│   ├── ProductImages/
│   ├── Program.cs
│   └── appsettings.json
│
├── ElectronicsStoreApi.Tests/
│   ├── Services/
│   │   └── ProductImageServiceTests.cs
│   ├── CategoriesAuthorizationIntegrationTests.cs
│   ├── CreateProductRequestTests.cs
│   ├── OrderServiceAvailabilityTests.cs
│   ├── OrderServiceTests.cs
│   ├── OrderServiceTransactionTests.cs
│   ├── ProductImagesControllerIntegrationTests.cs
│   ├── ProductServiceTests.cs
│   ├── ProductsApiIntegrationTests.cs
│   ├── ProductsFilteringIntegrationTests.cs
│   └── TestApplicationFactory.cs
│
├── Frontend/
│   ├── images/
│   ├── admin.html
│   ├── admin.js
│   ├── cart.js
│   ├── index.html
│   ├── product.html
│   ├── script.js
│   └── style.css
│
├── .github/
│   └── workflows/
│       └── dotnet-tests.yml
│
├── Dockerfile
├── compose.yaml
├── .gitignore
├── .env.example
└── ElectronicsStoreApi.slnx
```

## Getting Started

### Requirements

- Docker Desktop
- Docker Compose

For local development:
- .NET 10 SDK
- PostgreSQL
- EF Core CLI
- Visual Studio or another .NET-compatible development environment

### Running with Docker Compose

Create a `.env` file in the repository root:

```dotenv
POSTGRES_DB=electronics_store
POSTGRES_USER=store_user
POSTGRES_PASSWORD=replace_with_a_local_password
JWT_ISSUER=ElectronicsStoreApiPractice
JWT_AUDIENCE=ElectronicsStoreApiPracticeClient
JWT_KEY=replace_with_a_base64_encoded_key
```

Generate a secure JWT signing key using PowerShell:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Use your own database password and generated signing key. Never commit `.env` or real credentials to source control.

Build and start the application:

```powershell
docker compose up -d --build
```

Check the running services:

```powershell
docker compose ps -a
```

The application is available at:

- Frontend: http://localhost:8081
- Backend API: http://localhost:8081
- PostgreSQL: localhost:5435

The API connects to PostgreSQL through the Docker Compose network using the database service hostname `db` and port `5432`.

### Database Migrations

Docker Compose includes a dedicated migration service.

The service:

1. Waits for PostgreSQL to become healthy.
2. Applies pending EF Core migrations.
3. Exits after successful completion.
4. Allows the API service to start after migrations complete successfully.

There is no need to apply migrations manually when starting the application through Docker Compose.

Inspect migration logs:

```powershell
docker compose logs migrate
```

### Useful Docker Commands

```powershell
docker compose ps -a
docker compose logs api
docker compose logs db
docker compose logs migrate
docker compose restart api
docker compose down
```

PostgreSQL data and uploaded product images are stored in persistent Docker volumes.

Running `docker compose down` does not remove named volumes. Avoid `docker compose down -v` unless you intentionally want to delete persistent database and image data.

## Running Locally

Configure the following settings through .NET User Secrets or environment variables:

- `ConnectionStrings:DefaultConnection` — PostgreSQL connection string.
- `Jwt:Issuer` — expected token issuer.
- `Jwt:Audience` — expected token audience.
- `Jwt:Key` — Base64-encoded JWT signing key.

Restore dependencies:

```powershell
dotnet restore ElectronicsStoreApi.slnx
```

Apply database migrations:

```powershell
dotnet ef database update --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj --startup-project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj
```

Run the API:

```powershell
dotnet run --project .\ElectronicsStoreApi\ElectronicsStoreApi.csproj
```

The local HTTP and HTTPS addresses depend on the configured launch profile.

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/auth/register` | Register a user |
| POST | `/api/auth/login` | Authenticate and obtain a JWT |
| GET | `/api/products` | List, search, filter, sort, and paginate products |
| GET | `/api/products/{id}` | Retrieve a product |
| GET | `/api/products/by-category/{categoryId}` | Retrieve products by category |
| POST | `/api/products` | Create a product |
| PUT | `/api/products/{id}` | Update a product |
| DELETE | `/api/products/{id}` | Delete a product |
| GET | `/api/categories` | List categories |
| GET | `/api/categories/{id}` | Retrieve a category |
| POST | `/api/categories` | Create a category |
| PUT | `/api/categories/{id}` | Update a category |
| DELETE | `/api/categories/{id}` | Delete a category |
| GET | `/api/categories/{categoryId}/specifications` | List category specifications |
| POST | `/api/categories/{categoryId}/specifications` | Create a category specification |
| PUT | `/api/categories/{categoryId}/specifications/{specificationId}` | Update a category specification |
| DELETE | `/api/categories/{categoryId}/specifications/{specificationId}` | Delete a category specification |
| POST | `/api/orders` | Create an order |
| POST | `/api/products/{productId}/image` | Upload or replace a product image |
| DELETE | `/api/products/{productId}/image` | Delete a product image |

Product, category, image, and category specification write operations require the administrator role. Order creation requires authentication. Public catalog and category retrieval endpoints do not require authentication.

## Testing

The solution includes automated unit and integration tests implemented with xUnit.

Test coverage includes:
- Product service behaviour and request validation.
- Product API endpoints.
- Product filtering and pagination.
- Category authorization.
- Order availability validation.
- Order transaction handling.
- Product image validation and upload endpoints.
- Product image service behaviour.

Run the test suite:

```powershell
dotnet test ElectronicsStoreApi.slnx --configuration Release
```

The tests use isolated EF Core InMemory databases where configured. This does not replace testing against a real PostgreSQL instance.

The current test suite contains 69 tests, all passing.

GitHub Actions runs automated tests when changes are pushed to the `main` branch or submitted through a pull request.

## Architecture

The application follows a layered structure:

- **Controllers** handle HTTP requests and responses.
- **Services** contain application and business logic.
- **DTOs** define request and response contracts.
- **Models** represent application entities.
- **Data** contains the Entity Framework Core database context.
- **Migrations** manage database schema changes.

The backend exposes REST API endpoints consumed by the frontend. PostgreSQL stores application data, while uploaded product images are stored in persistent file storage.