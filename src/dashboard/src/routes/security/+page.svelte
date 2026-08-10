<script lang="ts">
	import { onMount } from 'svelte';
	import { EntraSettingsState } from '$lib/entra-settings/state.svelte';
	import { entraSettingsContext } from '$lib/entra-settings/context';
	import SecuritySettingsForm from '$lib/components/security/SecuritySettingsForm.svelte';

	const entra = entraSettingsContext.set(new EntraSettingsState());

	onMount(() => {
		void entra.load();
	});
</script>

<svelte:head>
	<title>Flare — Security</title>
</svelte:head>

<div class="flex h-full flex-col">
	<SecuritySettingsForm />
</div>
