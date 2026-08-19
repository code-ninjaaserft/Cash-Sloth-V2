# CashSloth.App.Tests

Application-layer tests cover local preset persistence, event-register persistence, completed-sale history, and central-server client trust/connection behavior.

The removed local-account and anonymous preset-provider suites are intentionally absent because those production paths no longer exist. Server account, pairing, token, preset, backup, reference-data, and API authorization behavior is covered in `tests/CashSloth.Server.Tests`.

```powershell
dotnet test tests/CashSloth.App.Tests/CashSloth.App.Tests.csproj -p:SkipNativeCoreBuild=true
```
