<script lang="ts">
	import { onMount } from 'svelte';
	import { UsersState } from '$lib/users/state.svelte';
	import { usersContext } from '$lib/users/context';
	import UserTable from '$lib/components/users/UserTable.svelte';

	const users = usersContext.set(new UsersState());

	onMount(() => {
		void users.load();
	});
</script>

<svelte:head>
	<title>Flare — Users</title>
</svelte:head>

<div class="flex h-full flex-col">
	<UserTable />
</div>
