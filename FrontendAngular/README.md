# Angular frontend

This directory contains the active frontend for the Electronics Store application.

The application uses Angular 22 and TypeScript. In the Docker deployment, Angular is built as a static production bundle and served by the ASP.NET Core API from its `wwwroot` directory. A separate frontend container is not required.

## Requirements

- Node.js 26.x
- npm 11.x

See the [Angular version compatibility guide](https://angular.dev/reference/versions) for supported Node.js versions.

## Local development

Start the ASP.NET Core API separately using the HTTP launch profile. The API listens at `http://localhost:5248`.

Install dependencies and start Angular:

```powershell
npm.cmd ci
npm.cmd start
```

Open [http://localhost:4200](http://localhost:4200).

The development server uses `proxy.conf.json` to forward `/api` and `/uploads` requests to the local API. The frontend therefore uses relative API paths in both development and Docker deployments.

To make the development server accessible from other devices on the same local network:

```powershell
npm.cmd start -- --host 0.0.0.0
```

Open `http://<COMPUTER-IP>:4200` from the other device. The API requests continue to pass through the Angular development proxy.

## Build

Create an optimized production build:

```powershell
npm.cmd run build
```

The browser bundle is written to:

```text
dist/electronics-store-frontend/browser/
```

The root Dockerfile copies this directory into the ASP.NET Core image as `wwwroot`.

## Tests

Run the Angular unit tests once without entering watch mode:

```powershell
npm.cmd test -- --watch=false
```

## Formatting

Format Angular source files:

```powershell
npm.cmd run format
```

Check formatting without modifying files:

```powershell
npm.cmd run format:check
```
