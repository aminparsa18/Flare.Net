// Shared state behind the "new version available" banner (UpdateNotice.svelte) and the
// version line in the user menu (NavUserMenu.svelte) - one /api/version fetch per page load
// for both. See ADR-0068.
//
// Dismissal is remembered per release version in localStorage, not as a flag: dismissing
// 0.6.0 hides the banner until 0.7.0 is out, rather than forever. Per-browser, same as
// $lib/time/display-zone - no server-side user settings store exists.
import { browser } from '$app/environment';
import { getVersionInfo, type VersionInfo } from '$lib/version-api';

const DISMISSED_KEY = 'flare.updateNotice.dismissedVersion';

function loadDismissed(): string | null {
	if (!browser) return null;
	try {
		return localStorage.getItem(DISMISSED_KEY);
	} catch {
		return null; // storage disabled
	}
}

class UpdateNoticeState {
	info = $state<VersionInfo | null>(null);
	dismissedVersion = $state<string | null>(loadDismissed());
	/** The release-notes dialog - opened from the banner or the user menu. */
	notesOpen = $state(false);

	#loading = false;

	/** The newer release, when there is one - whether or not the banner was dismissed. */
	get availableRelease() {
		return this.info?.updateAvailable ? this.info.latest : null;
	}

	get showBanner(): boolean {
		const release = this.availableRelease;
		return release !== null && release.version !== this.dismissedVersion;
	}

	/** Fetches once; later calls are no-ops. A failure just means no notice - it's best-effort. */
	async load(): Promise<void> {
		if (this.#loading || this.info) return;
		this.#loading = true;
		try {
			this.info = await getVersionInfo();
		} catch {
			// Older Flare.Api without the endpoint, or unreachable - nothing to show.
		} finally {
			this.#loading = false;
		}
	}

	dismiss(): void {
		const release = this.availableRelease;
		if (!release) return;
		this.dismissedVersion = release.version;
		try {
			localStorage.setItem(DISMISSED_KEY, release.version);
		} catch {
			// Storage full/disabled - dismissed for this session only.
		}
	}
}

export const updateNotice = new UpdateNoticeState();
