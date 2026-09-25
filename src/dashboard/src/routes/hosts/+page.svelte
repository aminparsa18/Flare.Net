<script lang="ts">
	// Host inventory from ingested OTel `hostmetrics`-receiver metrics - one row per
	// `host.name`. Its own route rather than a section of /resources: that page shows
	// infrastructure Flare discovers by polling Docker/K8s/its own machine, while this
	// shows hosts that report themselves over OTLP (a host running only an OTel Collector
	// appears here and nowhere else).
	import { onMount, onDestroy } from 'svelte';
	import { HostsState } from '$lib/hosts/state.svelte';
	import { hostsContext } from '$lib/hosts/context';
	import HostsToolbar from '$lib/components/hosts/HostsToolbar.svelte';
	import HostsTable from '$lib/components/hosts/HostsTable.svelte';
	import HostDetailSheet from '$lib/components/hosts/HostDetailSheet.svelte';
	import * as m from '$lib/paraglide/messages';

	const hosts = hostsContext.set(new HostsState());

	onMount(() => {
		void hosts.load();
		hosts.startPolling();
	});

	onDestroy(() => {
		hosts.dispose();
	});
</script>

<svelte:head>
	<title>{m.hostsPage_title()}</title>
</svelte:head>

<div class="flex h-full flex-col overflow-y-auto">
	<HostsToolbar />
	<HostsTable />
	<HostDetailSheet />
</div>
