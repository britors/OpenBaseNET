# OpenBaseNET template

Create an API with four production projects (Domain, Application, Infrastructure and Api)
and the selected database adapter. Requires the stable .NET 10 SDK.

```bash
dotnet new openbasenet --name MinhaApi --database postgres
dotnet new openbasenet --name MinhaApi --database sqlserver
dotnet new openbasenet --name MinhaApi --database oracle
```

The database choice is required. Creation does not connect to a database or restore packages.
The generated README explains configuration, builds, tests and migrations.

[Project and compatibility documentation](https://github.com/britors/OpenBaseNET).
