// Client for Flare.Api's project API (ADR-0123): `GET /api/projects/mine` for any signed-in
// user (the switcher and the form pickers), and the Admin-only `/api/projects` CRUD plus
// `/members` management behind Settings > Projects. Plain JSON, like `slos-api.ts` - small
// flat records, no MemoryPack adapter to maintain.

import { API_BASE_URL, apiFetch } from './api';
import type { UserRole } from './auth-api';

/** Sent as `projectId` on an update to move an object back to instance-wide (omitted keeps its project). */
export const NO_PROJECT = '00000000-0000-0000-0000-000000000000';

export interface Project {
	id: string;
	name: string;
	description: string;
	servicePatterns: string[];
}

/** A project the caller belongs to, with their own role in it. */
export interface MyProject {
	id: string;
	name: string;
	description: string;
	role: UserRole;
}

export interface ProjectMember {
	userId: string;
	username: string;
	role: UserRole;
}

export interface ProjectRequest {
	name: string;
	description: string;
	servicePatterns: string[];
}

async function expectOk(res: Response, what: string): Promise<void> {
	if (res.ok) return;
	let detail = '';
	try {
		detail = ((await res.json()) as { detail?: string }).detail ?? '';
	} catch {
		// not a problem+json body
	}
	throw new Error(detail || `${what} failed: ${res.status} ${res.statusText}`);
}

const jsonHeaders = { 'Content-Type': 'application/json' };

export async function listMyProjects(signal?: AbortSignal): Promise<MyProject[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/projects/mine`, { signal });
	await expectOk(res, 'GET /api/projects/mine');
	return ((await res.json()) as { projects: MyProject[] }).projects;
}

export async function listProjects(signal?: AbortSignal): Promise<Project[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/projects`, { signal });
	await expectOk(res, 'GET /api/projects');
	return ((await res.json()) as { projects: Project[] }).projects;
}

export async function createProject(request: ProjectRequest): Promise<Project> {
	const res = await apiFetch(`${API_BASE_URL}/api/projects`, { method: 'POST', headers: jsonHeaders, body: JSON.stringify(request) });
	await expectOk(res, 'POST /api/projects');
	return (await res.json()) as Project;
}

export async function updateProject(id: string, request: ProjectRequest): Promise<Project> {
	const res = await apiFetch(`${API_BASE_URL}/api/projects/${id}`, { method: 'PUT', headers: jsonHeaders, body: JSON.stringify(request) });
	await expectOk(res, 'PUT /api/projects/{id}');
	return (await res.json()) as Project;
}

export async function deleteProject(id: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/projects/${id}`, { method: 'DELETE' });
	await expectOk(res, 'DELETE /api/projects/{id}');
}

export async function listProjectMembers(id: string, signal?: AbortSignal): Promise<ProjectMember[]> {
	const res = await apiFetch(`${API_BASE_URL}/api/projects/${id}/members`, { signal });
	await expectOk(res, 'GET /api/projects/{id}/members');
	return ((await res.json()) as { members: ProjectMember[] }).members;
}

export async function setProjectMember(id: string, userId: string, role: UserRole): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/projects/${id}/members/${userId}`, {
		method: 'PUT',
		headers: jsonHeaders,
		body: JSON.stringify({ role })
	});
	await expectOk(res, 'PUT /api/projects/{id}/members/{userId}');
}

export async function removeProjectMember(id: string, userId: string): Promise<void> {
	const res = await apiFetch(`${API_BASE_URL}/api/projects/${id}/members/${userId}`, { method: 'DELETE' });
	await expectOk(res, 'DELETE /api/projects/{id}/members/{userId}');
}
