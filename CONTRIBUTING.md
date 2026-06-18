# Contributing

Thanks for your interest in contributing to `osdu-csharp-schemas`! This project
generates typed C# domain models from a pinned snapshot of the OSDU schema
registry. Contributions are welcome via issues and pull requests.

## Reporting issues

- Search [existing issues](https://github.com/equinor/osdu-csharp-schemas/issues)
  before opening a new one.
- For security vulnerabilities, **do not** open a public issue — follow
  [`SECURITY.md`](SECURITY.md) instead.

## Development setup

Requires the .NET SDK (see `Directory.Build.props`/`global.json` for the target).

```bash
dotnet run --project tools/SchemaGen   # regenerate the C# from the pinned snapshot
dotnet build Osdu.Schemas.slnx          # build everything
dotnet test                             # round-trip + integration tests
```

Generated code under `src/Osdu.Schemas/Generated/` is gitignored and fully
regenerable from the pinned snapshot in `schemas/`. Do not hand-edit generated
models; change the generator (`tools/SchemaGen`) or the snapshot instead.

## Pull request process

1. Fork the repository (or create a branch if you have write access) and base
   your work on `main`.
2. Make your change, then run `dotnet build` and `dotnet test` locally — both
   must pass.
3. Open a pull request against `main`.
4. **PR titles must follow [Conventional Commits](https://www.conventionalcommits.org/)**
   (e.g. `feat: add dataset models`, `fix: correct ref flattening`). This is
   enforced by CI and drives automated releases via release-please.
5. At least one approving review (including a CODEOWNERS review) is required
   before merging. Direct pushes to `main` are not permitted.
6. Pull requests are merged using **squash merge**.

## Releases

Releases are automated with
[release-please](https://github.com/googleapis/release-please). Merging
conventional commits to `main` opens/updates a release PR; merging that PR tags
the version and publishes the NuGet package to GitHub Packages.

## License

By contributing, you agree that your contributions will be licensed under the
[Apache License 2.0](LICENSE).
