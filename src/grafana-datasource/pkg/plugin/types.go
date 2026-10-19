package plugin

// Wire shapes of the Flare.Api responses the plugin reads (camelCase JSON, enums as strings).

type appSession struct {
	SessionID       string   `json:"sessionId"`
	ServiceName     string   `json:"serviceName"`
	Version         string   `json:"version"`
	Os              string   `json:"os"`
	Device          string   `json:"device"`
	FirstSeenUnixMs int64    `json:"firstSeenUnixMs"`
	LastSeenUnixMs  int64    `json:"lastSeenUnixMs"`
	SpanCount       uint64   `json:"spanCount"`
	TraceCount      uint64   `json:"traceCount"`
	ErrorCount      uint64   `json:"errorCount"`
	Screens         []string `json:"screens"`
}

type appSessionsResponse struct {
	WindowMinutes int          `json:"windowMinutes"`
	Sessions      []appSession `json:"sessions"`
	Truncated     bool         `json:"truncated"`
	Services      []string     `json:"services"`
	Versions      []string     `json:"versions"`
}

type timelineEvent struct {
	TraceID            string  `json:"traceId"`
	SpanID             string  `json:"spanId"`
	Name               string  `json:"name"`
	ServiceName        string  `json:"serviceName"`
	StartUnixMs        int64   `json:"startUnixMs"`
	DurationMs         float64 `json:"durationMs"`
	Screen             string  `json:"screen"`
	IsError            bool    `json:"isError"`
	StatusMessage      string  `json:"statusMessage"`
	ExceptionType      string  `json:"exceptionType"`
	ExceptionMessage   string  `json:"exceptionMessage"`
	BreadcrumbCategory string  `json:"breadcrumbCategory"`
	BreadcrumbMessage  string  `json:"breadcrumbMessage"`
	HasScreenshot      bool    `json:"hasScreenshot"`
}

type timelineResponse struct {
	SessionID   string          `json:"sessionId"`
	ServiceName string          `json:"serviceName"`
	Version     string          `json:"version"`
	Os          string          `json:"os"`
	Device      string          `json:"device"`
	Events      []timelineEvent `json:"events"`
	Truncated   bool            `json:"truncated"`
}

type appStartStat struct {
	Version string  `json:"version"`
	Type    string  `json:"type"`
	Count   uint64  `json:"count"`
	P50Ms   float64 `json:"p50Ms"`
	P95Ms   float64 `json:"p95Ms"`
}

type screenPerformance struct {
	Screen       string  `json:"screen"`
	Loads        uint64  `json:"loads"`
	LoadP50Ms    float64 `json:"loadP50Ms"`
	LoadP95Ms    float64 `json:"loadP95Ms"`
	Visits       uint64  `json:"visits"`
	Frames       uint64  `json:"frames"`
	SlowFrames   uint64  `json:"slowFrames"`
	FrozenFrames uint64  `json:"frozenFrames"`
}

type performanceResponse struct {
	WindowMinutes int                 `json:"windowMinutes"`
	Starts        []appStartStat      `json:"starts"`
	Screens       []screenPerformance `json:"screens"`
}

type releaseHealthVersion struct {
	Version         string `json:"version"`
	Sessions        uint64 `json:"sessions"`
	CrashedSessions uint64 `json:"crashedSessions"`
	ErroredSessions uint64 `json:"erroredSessions"`
	Users           uint64 `json:"users"`
	CrashedUsers    uint64 `json:"crashedUsers"`
}

type releaseHealthResponse struct {
	WindowMinutes int                    `json:"windowMinutes"`
	Versions      []releaseHealthVersion `json:"versions"`
}

type releaseHealthTrendPoint struct {
	BucketUnixMs    int64  `json:"bucketUnixMs"`
	Version         string `json:"version"`
	Sessions        uint64 `json:"sessions"`
	CrashedSessions uint64 `json:"crashedSessions"`
}

type releaseHealthTrendResponse struct {
	WindowMinutes int                       `json:"windowMinutes"`
	BucketMinutes int                       `json:"bucketMinutes"`
	Points        []releaseHealthTrendPoint `json:"points"`
}

type exceptionGroup struct {
	ExceptionType    string   `json:"exceptionType"`
	ExceptionMessage string   `json:"exceptionMessage"`
	OccurrenceCount  uint64   `json:"occurrenceCount"`
	FirstSeen        string   `json:"firstSeen"`
	LastSeen         string   `json:"lastSeen"`
	AffectedServices []string `json:"affectedServices"`
	SamplePaths      []string `json:"samplePaths"`
	Owner            string   `json:"owner"`
}

type exceptionGroupsResponse struct {
	Groups []exceptionGroup `json:"groups"`
}

type exceptionFacetValuesResponse struct {
	Values []struct {
		Value string `json:"value"`
		Count uint64 `json:"count"`
	} `json:"values"`
}

type errorIssue struct {
	ID                     string `json:"id"`
	ExceptionType          string `json:"exceptionType"`
	ExceptionMessage       string `json:"exceptionMessage"`
	Status                 string `json:"status"`
	Assignee               string `json:"assignee"`
	StatusChangedAt        string `json:"statusChangedAt"`
	IssueURL               string `json:"issueUrl"`
	OccurrencesSinceChange uint64 `json:"occurrencesSinceChange"`
	RegressedVersion       string `json:"regressedVersion"`
	UpdatedAt              string `json:"updatedAt"`
}

type errorIssueListResponse struct {
	Issues []errorIssue `json:"issues"`
}

type release struct {
	ID              string `json:"id"`
	Service         string `json:"service"`
	Version         string `json:"version"`
	Commit          string `json:"commit"`
	URL             string `json:"url"`
	Notes           string `json:"notes"`
	DeployedAt      string `json:"deployedAt"`
	CreatedBy       string `json:"createdBy"`
	NewErrorCount   *int64 `json:"newErrorCount"`
	RegressionCount *int64 `json:"regressionCount"`
}

type releaseListResponse struct {
	Releases []release `json:"releases"`
}
