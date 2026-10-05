// Shared project state for the whole app (ADR-0123): the projects the caller belongs to, and
// the "active project" the switcher in AppNav selects. The active project is a view filter on
// config lists (dashboards, alerts, SLOs, saved views) and the default for new objects - it
// does not change what telemetry a query returns, which the server already scopes to the
// caller's union of projects.

import { listMyProjects, NO_PROJECT, type MyProject } from '$lib/projects-api';
import type { UserRole } from '$lib/auth-api';

const STORAGE_KEY = 'flare.activeProject';

function readStored(): string {
	try {
		return localStorage.getItem(STORAGE_KEY) ?? '';
	} catch {
		return '';
	}
}

class ProjectStore {
	mine = $state.raw<MyProject[]>([]);
	loaded = $state(false);
	/** Selected project id, '' = all projects. */
	activeId = $state('');

	async load(): Promise<void> {
		try {
			this.mine = await listMyProjects();
		} catch {
			this.mine = [];
		} finally {
			this.loaded = true;
		}
		if (this.activeId === '') this.activeId = readStored();
		// A stale id (project deleted, membership removed) must not silently hide everything.
		if (this.activeId && !this.mine.some((p) => p.id === this.activeId)) this.setActive('');
	}

	setActive(id: string): void {
		this.activeId = id;
		try {
			if (id) localStorage.setItem(STORAGE_KEY, id);
			else localStorage.removeItem(STORAGE_KEY);
		} catch {
			// private mode: the choice just doesn't persist
		}
	}

	get active(): MyProject | undefined {
		return this.mine.find((p) => p.id === this.activeId);
	}

	nameOf(id: string | null): string | undefined {
		return id ? this.mine.find((p) => p.id === id)?.name : undefined;
	}

	/** Whether a config object belongs in the current view: instance-wide ones always do. */
	visible(projectId: string | null | undefined): boolean {
		return !this.activeId || !projectId || projectId === this.activeId;
	}

	/** Projects the caller may create/edit objects in (project role Admin or Member). */
	get writable(): MyProject[] {
		return this.mine.filter((p) => (p.role as UserRole) !== 'Viewer');
	}

	/** The project a new object should default to. */
	get defaultForNew(): string | null {
		return this.writable.some((p) => p.id === this.activeId) ? this.activeId : null;
	}
}

export const projects = new ProjectStore();

/**
 * The `projectId` to send on a save. A cleared picker on an object that had a project must send
 * `NO_PROJECT` - omitting it would keep the old project (ADR-0123 update semantics).
 */
export function projectIdForRequest(selected: string | null, original: string | null | undefined): string | null {
	if (selected) return selected;
	return original ? NO_PROJECT : null;
}
