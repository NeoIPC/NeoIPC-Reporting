# NeoIPC.Reporting.Tests

The NUnit suite for the reporting service, in four categories. Whatever a test needs from outside
the process is either provided by the test run itself or reported as an ignored test with the
reason, so running the whole project from Visual Studio, Rider or `dotnet test` is safe on any
machine.

## What each category needs

| Category | Tests | Needs |
|---|---|---|
| `Unit` | the service's own logic, in process | nothing |
| `Generator` | the source generator's drift tests, driving Roslyn in process | nothing |
| `Container` | `NegativePathTests`, `ParametersEndpointTests`, `ImageFontTests`: the built image, started in isolation through Testcontainers with no DHIS2 behind it; `NegativePathTests` drives it with a placeholder session, `ParametersEndpointTests` anonymously, and `ImageFontTests` reads the fonts it installs through fontconfig inside the container and checks the fonts of a figure R's Cairo device draws there | Docker. The run builds the image itself unless `NEOIPC_REPORTING_IMAGE_TAG` names one (below). |
| `Integration` | `RenderingIntegrationTests`: the real authentication-and-render path against a running, seeded NeoIPC stack, including the embedding of every font in a rendered Partner Report PDF | the stack. The fixture skips when the reporting service is unreachable or DHIS2 refuses the login, and the render tests skip when the test department is not there. |

## Running everything from an IDE

Run the project as it is. With nothing set up beforehand:

- `Unit` and `Generator` run.
- `Container` builds `neoipc-reporting:smoke-test` from `src/NeoIPC.Reporting/Dockerfile` and runs
  it. The build happens on every run, so the tests exercise this repository's current sources
  rather than whatever an earlier build left behind; the Surveillance-Toolkit clone and the neoipcr
  install are cached layers that stay at whatever `main` was when they were first built, until
  `docker builder prune`. BuildKit's layer cache makes a rebuild of unchanged sources a matter of
  seconds. **The first build on a machine takes tens of minutes**: it clones
  the Surveillance-Toolkit `main` branch from GitHub, installs neoipcr from its `main` branch, and
  installs R, TeX Live and Quarto. The build output reaches the runner as progress messages,
  which Visual Studio shows in the test output pane and `dotnet test` shows from
  `--logger "console;verbosity=normal"` upwards. With no Docker on the machine, or no daemon
  running, the three fixtures report ignored. The container has no DHIS2 behind it, and every
  request `NegativePathTests` sends carries a session cookie the service tries to validate there;
  on a machine whose resolver holds an unknown host name open instead of refusing it — a VPN's
  resolver can — each of those requests first waits out the service's five-second DHIS2 timeout,
  so that fixture takes a couple of minutes there. The results are the same.
- `Integration` reports ignored unless the local stack is up (see the environment table below).

Filter on the `Category` trait to run a subset.

## Running from the command line

```bash
dotnet test --filter "Category!=Integration&Category!=Container"   # what CI runs on every pull request
dotnet test --filter "Category=Container"                          # builds the image, then runs it
dotnet test --filter "Category=Integration"                        # against the running stack
```

## Pointing the container tests at an existing image

Set `NEOIPC_REPORTING_IMAGE_TAG` to a tag and the run uses that image without building. That is
what the CI smoke job does after building with its own inputs, and it is the way to run the tests
against an image built by other means. A tag that names no image is a failure, not a skip: the image
is what the category verifies.

In Visual Studio, environment variables for a test run come from a `.runsettings` file selected
under **Test › Configure Run Settings**:

```xml
<RunSettings>
  <RunConfiguration>
    <EnvironmentVariables>
      <NEOIPC_REPORTING_IMAGE_TAG>neoipc-reporting:mine</NEOIPC_REPORTING_IMAGE_TAG>
    </EnvironmentVariables>
  </RunConfiguration>
</RunSettings>
```

## Environment of the integration tests

All read by `ExternalDhis2Fixture`; the defaults match a local stack published on port 8080.

| Variable | Default | Meaning |
|---|---|---|
| `NEOIPC_DHIS2_BASE_URL` | `http://localhost:8080` | DHIS2, for the login |
| `NEOIPC_REPORTING_BASE_URL` | `http://localhost:8080/neoipc/api` | the reporting API's mount |
| `NEOIPC_DHIS2_ADMIN_USER` / `NEOIPC_DHIS2_ADMIN_PASS` | `admin` / `district` | the DHIS2 credentials |
| `NEOIPC_TEST_DEPARTMENT_CODE` | `AT_TEST_TEST` | the seeded test department the render tests report on; the default is the play package's regular test department |
