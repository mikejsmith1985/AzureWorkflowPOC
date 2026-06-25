# sample-app — Docker executor integration fixture (feature 013)

A trivial repo used by `DockerAppExecutorTests` to exercise a real throwaway-container build and run.

The integration test supplies POSIX-shell build/run commands that work on the default `alpine` base
image (no language toolchain required), so the test verifies the **executor mechanics** — bind-mount,
artifact volume, log capture, timeout, and container cleanup — rather than any specific ecosystem.

- **Build command** (example): `echo built > artifact.txt`
- **Run command** (example): `cat artifact.txt`

The test is **env-gated**: it runs only when `DBAI_DOCKER_TESTS=1` and a Docker engine is reachable;
otherwise it is skipped so the unit suite stays hermetic.
