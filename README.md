# Grafana Proxy

This project provides a secure proxy solution for accessing Grafana dashboards, along with a management API for configuring tenants, API keys, and dashboard permissions.

## Components

1.  **`GrafanaProxy.Web`**: An ASP.NET Core application acting as a YARP (Yet Another Reverse Proxy) reverse proxy. It authenticates incoming requests using API keys and forwards authorized requests to the appropriate Grafana public dashboard endpoint based on tenant permissions.
2.  **`GrafanaProxy.Management.Api`**: A separate ASP.NET Core Web API intended for internal use. It provides endpoints for managing tenants, generating/regenerating API keys, and assigning dashboard permissions to tenants.

## Features

*   Tenant-based access control for Grafana dashboards.
*   API key authentication for proxy access.
*   Management API for configuration.
*   SQLite database for storing configuration (tenants, keys, permissions).
*   Dockerized for easy deployment.
*   GitHub Actions workflow for building and pushing Docker images to Docker Hub on version tags (`v*.*.*`).

## Getting Started

### Prerequisites

*   .NET 9 SDK
*   Docker & Docker Compose

### Running Locally (Docker Compose)

The easiest way to run both applications locally is using Docker Compose:

1.  **Clone the repository:**
    ```bash
    git clone https://github.com/bjth/grafana-proxy.git
    cd grafana-proxy
    ```
2.  **(Optional) Configure HTTPS Development Certificate:**
    Ensure you have ASP.NET Core development certificates trusted:
    ```bash
    dotnet dev-certs https --trust
    ```
    If your certificate requires a password, set the `KESTREL_CERT_PASSWORD` environment variable (e.g., in a `.env` file in the project root).

3.  **Run Docker Compose:**
    From the project root directory:
    ```bash
    docker-compose -f build/docker/docker-compose.yml up --build
    ```

This will:
*   Build the Docker images for both applications.
*   Create a Docker volume (`grafana_proxy_data`) to store the shared `grafana_proxy_config.db` SQLite database.
*   Start containers for both applications.

**Access Points:**
*   **Web Proxy:** `http://localhost:8080` / `https://localhost:8081`
*   **Management API:** `http://localhost:8090` / `https://localhost:8091`
*   **Management API Swagger UI:** `http://localhost:8090/swagger` or `https://localhost:8091/swagger`

### Running Locally (dotnet run)

You can also run the applications directly using the .NET CLI:

*   **Run Management API:**
    ```bash
    dotnet run --project src/GrafanaProxy.Management.Api/GrafanaProxy.Management.Api.csproj
    ```
*   **Run Web Proxy (in a separate terminal):**
    ```bash
    dotnet run --project src/GrafanaProxy.Web/GrafanaProxy.Web.csproj
    ```
    *(Note: The database file `src/grafana_proxy_config.db` will be created relative to the `src` directory when running this way).* Fix the YARP HealthCheck issue if running this way.

### Integration Testing (Postman)

A Postman collection for testing the Management API endpoints is located at `tests/integration/GrafanaProxy.Management.postman_collection.json`.

1.  Ensure the Management API is running (either via `docker-compose up` or `dotnet run`).
2.  Import the collection file into Postman.
3.  The collection uses a variable `{{baseUrl}}` which defaults to `http://localhost:8090`. Adjust this variable in Postman if your API is running on a different address (e.g., `https://localhost:8091`).
4.  Run the requests individually or use the Postman Collection Runner to execute them in sequence.
    *   The `Create Tenant` request uses random data and saves the created tenant's ID and ShortCode to collection variables.
    *   Subsequent requests use these variables to target the created tenant.

## Configuration

*   **Database:** Configuration (tenants, keys, permissions) is stored in a SQLite database (`grafana_proxy_config.db`). When running via Docker Compose, this is stored in the `grafana_proxy_data` volume.
*   **YARP & Grafana URL:** The reverse proxy configuration (routes, clusters) is defined in `src/GrafanaProxy.Web/appsettings.json`. 
    *   When running via Docker Compose, the Grafana destination address can be overridden by setting the `GRAFANA_URL` environment variable (e.g., in a `.env` file in the project root). It defaults to `https://play.grafana.org/` if not set.
    *   If running directly (`dotnet run`), you might need to adjust the Grafana destination address (`clusters.grafana-cluster.destinations.destination1.address`) in `appsettings.json` and potentially the health check policy (`clusters.grafana-cluster.HealthCheck.Active.Policy`) if you encounter startup errors like `No matching IActiveHealthCheckPolicy found`.
*   **API Keys:** Use the Management API (`POST /api/tenants`) to create tenants and initial API keys.
*   **Dashboard Permissions:** Use the Management API (`POST /api/tenants/{tenantId}/dashboards`) to grant tenants access to specific Grafana dashboard UIDs.

## CI/CD

A GitHub Actions workflow (`.github/workflows/docker-publish.yml`) is configured to:
*   Build Docker images for both applications.
*   Push images to Docker Hub (`<your-username>/grafana-proxy` and `<your-username>/grafana-proxy-api`) when a tag matching `v*.*.*` is pushed to the `main` branch.
*   Requires `DOCKERHUB_USERNAME` and `DOCKERHUB_TOKEN` secrets to be configured in the GitHub repository settings.

## Contributing

(Add contribution guidelines if applicable)

## License

(Add license information if applicable) 