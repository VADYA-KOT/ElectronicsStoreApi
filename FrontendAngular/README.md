# Angular frontend

This directory contains the active frontend for the Electronics Store application. It is built with Angular 22, TypeScript, and RxJS.

In Docker deployments, Angular is compiled into static production assets and served by the ASP.NET Core API from its `wwwroot` directory. The frontend does not run in a separate container.

## Requirements

- Node.js 26.x.
- npm 11.x (the repository's package manager version is recorded in `package.json`).

See the [Angular version compatibility guide](https://angular.dev/reference/versions) for supported Node.js versions.

On Windows PowerShell, use `npm.cmd` in the commands below. On macOS and Linux, use `npm`.

## Local development

Start the ASP.NET Core API separately using its HTTP launch profile. By default, the API listens at:

```text
http://localhost:5248
```

From this directory, install the locked dependencies and start the Angular development server:

```powershell
npm.cmd ci
npm.cmd start
```

Open [http://localhost:4200](http://localhost:4200).

The development server uses `proxy.conf.json` to forward both `/api` and `/uploads` requests to `http://localhost:5248`. The frontend uses relative API and image paths, so the same application code works with the local proxy and with the Docker deployment.

To make the development server accessible from another device on the same local network:

```powershell
npm.cmd start -- --host 0.0.0.0
```

Open `http://<COMPUTER-IP>:4200` from the other device. API and uploaded-image requests continue to pass through the Angular development proxy. Allow inbound TCP port `4200` in Windows Firewall on a trusted private network if required.

## Production build

Create an optimized production build:

```powershell
npm.cmd run build
```

The browser bundle is written to:

```text
dist/electronics-store-frontend/browser/
```

The root `Dockerfile` builds this bundle in its `frontend-build` stage and copies it into the ASP.NET Core image as `wwwroot`. The API then serves the Angular application and API from the same origin.

The production build has bundle-size budgets configured in `angular.json`: an initial-bundle warning at 700 kB and an error threshold at 800 kB.

## Unit tests

Run Angular unit tests once without entering watch mode:

```powershell
npm.cmd test -- --watch=false
```

The Angular CLI uses the unit-test configuration in `angular.json`. The current GitHub Actions workflow builds the Angular production bundle through Docker but does not yet run Angular unit tests; run the command above locally when changing frontend code.

## Formatting

Format Angular source files under `src/`:

```powershell
npm.cmd run format
```

Check formatting without modifying files:

```powershell
npm.cmd run format:check
```

Formatting is configured in `.prettierrc.json`. The scripts cover TypeScript, HTML, CSS, and JSON files under `src/`.
