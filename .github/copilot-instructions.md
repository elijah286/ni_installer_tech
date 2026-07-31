# Project setup checklist

- [x] Verify the `copilot-instructions.md` file in the `.github` directory is created.
- [x] Clarify project requirements — Cross-platform native installation-experience prototype using C#, .NET 8, and Avalonia UI. UI only; installer execution is mocked.
- [x] Scaffold the project — Created an Avalonia MVVM desktop app named `NIInstallerTech`.
- [x] Customize the project — Implemented a progressive-disclosure installation journey backed by mock plan and component data.
- [x] Install required extensions — No extensions are required by the project setup guidance.
- [x] Compile the project — `dotnet build` completed successfully using the local .NET SDK.
- [x] Create and run task — No task is needed for this small prototype; standard `dotnet run` is sufficient.
- [ ] Launch the project — Awaiting user confirmation after a successful build.
- [x] Ensure documentation is complete — Added project README with scope, architecture direction, and run guidance.

## Project principles

- Keep the UI cross-platform: Windows, Linux, and macOS.
- Keep installer operations mocked and behind interfaces.
- Use progressive disclosure: show only necessary decisions and make consequences clear.
- Model trusted repositories, catalogs, components, compatibility, plans, and installation state explicitly.
