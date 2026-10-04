import { redirect } from '@sveltejs/kit';
import { withBase } from '$lib/paths';

export function load() {
	redirect(307, withBase('/settings/appearance'));
}
