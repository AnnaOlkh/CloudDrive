# CloudDrive

A lightweight personal cloud drive with native Windows and browser clients sharing an ASP.NET Core REST API.

**Live application:** http://myclouddrive.runasp.net/

## Assignment scope

Remote file-folder client (Google Drive–like). Assignment variant **7.0**: operation **0** (sort by name ascending/descending; filter all files, `.txt`, `.jpg`) and preview types for variant **7** (`.c` as text and `.jpg` as image).

## Features

- Account registration and JWT authentication; isolated personal file space.
- Folder hierarchy and folder creation/deletion.
- Upload, download, replacement and deletion of files; filename search.
- File metadata, configurable columns, name sorting and file-type filtering.
- Selected-file preview for `.c` and `.jpg`.
- File drag-and-drop uploads in both clients.
- Manual two-way folder synchronization with SHA-256 snapshots, conflict detection and confirmation of deletions.
- Automated unit and API/integration tests.

## Architecture

| Project | Responsibility |
| --- | --- |
| `CloudDrive.Core` | DTOs, file query, validators, SHA-256 and sync planner |
| `CloudDrive.Client` | Typed REST client and desktop sync executor |
| `CloudDrive.Server` | ASP.NET Core Minimal API, JWT, EF Core + SQLite, disk storage |
| `CloudDrive.Desktop` | WPF/XAML client (Windows) |
| `CloudDrive.Web` | Blazor WebAssembly client with JavaScript interop |
| `CloudDrive.Tests` | xUnit unit and integration tests |

Both clients → HTTP/REST → ASP.NET Core server → SQLite metadata + disk-backed file contents. The server also serves the deployed Blazor application.

## Notes

Browser folder synchronization requires a compatible browser and File System Access API permissions. Conflicting simultaneous changes are not automatically overwritten. The published URL is provided by the project author.

