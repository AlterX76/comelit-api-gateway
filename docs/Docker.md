# Docker

## Build the Docker Image

### 1. Navigate to the project root directory

```bash
cd <your-path>/ComelitApiGateway
```

### 2. Build docker image

The image supports both `linux/amd64` (x86_64) and `linux/arm64` architectures via a single multi-arch image.

#### Single architecture (current machine)

```bash
docker build -f ./Dockerfile -t giogdev/comelit-api-gateway:v<x.x.x> .
```

#### Multi-architecture (amd64 + arm64)

Requires [Docker Buildx](https://docs.docker.com/buildx/working-with-buildx/), included in Docker Desktop and Docker Engine >= 19.03.

```bash
# One-time setup: create a multi-arch builder
docker buildx create --name multiarch --use

# Build and push both architectures as a single image tag
docker buildx build \
  --platform linux/amd64,linux/arm64,linux/arm/v7 \
  -t giogdev/comelit-api-gateway:v<x.x.x> \
  --push \
  .
```

> **Note:** `--push` is required for multi-arch builds — Docker cannot load a multi-arch image into the local daemon. When pulling the image, Docker automatically selects the correct architecture for the host machine.

---

## Run the Container

### Parameters

| Variable | Required | Default | Description |
|---|---|---|---|
| `VEDO_URL` | Yes | — | IP address of the Vedo alarm panel (e.g. `http://192.168.1.100`) |
| `VEDO_KEY` | Yes | — | PIN of the alarm |
| `VEDO_EXCLUDED_AREAS_ID` | No | — | Comma-separated area IDs to exclude (e.g. `4,5,6,7`) |
| `VEDO_API_VERSION_OVERRIDE` | No | _(auto-detect)_ | Forces the arm/disarm API dialect. Empty = auto-detect firmware; `v1` (old GET) or `v2` (new POST) to force it |
| `ENABLE_SWAGGER` | No | `false` | Enable Swagger UI |
| `KEEPALIVE_ENABLED` | No | `false` | Enable the background keep-alive ping (required from firmware 2.15.X onwards) |
| `KEEPALIVE_INTERVAL_SECONDS` | No | `60` | Interval in seconds between keep-alive pings (only if `KEEPALIVE_ENABLED` is true) |
| `PORT` | No | `8080` | Port exposed on the host |
| `CONTAINER_PORT` | No | `5000` | Port used inside the container |

### docker run

```bash
docker run -d \
  --name comelit-api-gateway \
  -p 8080:5000 \
  -e VEDO_URL=http://192.168.1.XXX \
  -e VEDO_KEY=XXXXXXX \
  -e ASPNETCORE_URLS=http://+:5000 \
  --restart unless-stopped \
  giogdev/comelit-api-gateway:v<x.x.x>
```

---

## Docker Compose

### 1. Copy and configure the `.env` file

```bash
cp .env.template .env
```

Edit `.env`:

```env
PORT=8080                        # Port exposed on the host
VEDO_KEY=XXXXXXX                 # PIN of your alarm
VEDO_URL=http://192.168.1.XXX    # IP of your Vedo alarm panel
VEDO_EXCLUDED_AREAS_ID=4,5,6,7   # Area IDs to exclude (optional)
ENABLE_SWAGGER=true              # Enable Swagger UI (optional, default: false)
KEEPALIVE_ENABLED=true           # Enable keep-alive ping, required from firmware 2.15.X (optional, default: false)
KEEPALIVE_INTERVAL_SECONDS=60    # Keep-alive interval in seconds (optional, default: 60)

VEDO_API_VERSION_OVERRIDE=       # [Optional] Force arm/disarm dialect: empty=auto, v1=old GET, v2=new POST
```

### 2. Start the container

```bash
docker compose up -d
```

### 3. Stop the container

```bash
docker compose down
```
