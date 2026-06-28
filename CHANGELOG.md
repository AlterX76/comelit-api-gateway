# Changelog

All notable changes to this project are documented in this file.

## 1.1.0
- Optional and configurable background keep-alive (`KEEPALIVE_ENABLED`, `KEEPALIVE_INTERVAL_SECONDS`) to avoid session timeout on firmware 2.15.X+
- Firmware-aware arm/disarm: auto-detect Vedo API version with manual override (`VEDO_API_VERSION_OVERRIDE`)
- Vedo service refactoring: better performance, managed concurrency, automatic re-login on expired session
- HttpClient factory with session cookie handling and added cancellation token support

## 1.0.1
- Upgraded to .NET 10
- Improved documentation

## 1.0.0
- Initial release: REST gateway to integrate Comelit Vedo alarm with Home Assistant (or everything you want)
- Arm/disarm whole system or single area
- Read global and per-area alarm status
- Include/exclude elements (e.g. windows) from an area
- Read radar/motion sensor status
- Docker image and Swagger UI
