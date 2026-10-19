package main

import (
	"os"

	"github.com/grafana/grafana-plugin-sdk-go/backend/datasource"
	"github.com/grafana/grafana-plugin-sdk-go/backend/log"

	"github.com/aminparsa18/Flare.Net/src/grafana-datasource/pkg/plugin"
)

func main() {
	// Start listening to requests sent from Grafana. This call is blocking, so it won't finish until Grafana
	// shuts down the process or the plugin choose to exit by itself.
	if err := datasource.Manage("apardev-flare-datasource", plugin.NewDatasource, datasource.ManageOpts{}); err != nil {
		log.DefaultLogger.Error(err.Error())
		os.Exit(1)
	}
}
