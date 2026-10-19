//go:build mage

// Builds the plugin backend for every platform Grafana runs on: `go run github.com/magefile/mage -v` writes
// dist/gpx_flare_<os>_<arch>[.exe]. Use `build:linux`, `build:darwin` or `build:windows` for one OS, or
// `build:linuxARM64` and friends for one target.
package main

import (
	// mage:import
	build "github.com/grafana/grafana-plugin-sdk-go/build"
)

// Default builds every platform.
var Default = build.BuildAll
