#!/usr/bin/env bash
# Створює solution і додає проєкти. Запускати з кореня CloudDrive/.
set -e
dotnet new sln -n CloudDrive --force
dotnet sln add CloudDrive.Core CloudDrive.Client CloudDrive.Server CloudDrive.Desktop CloudDrive.Tests
dotnet restore
dotnet test
