<script lang="ts">
	// Planning.md's "Multi-node scaling" follow-up: a dashboard view of cluster mode now
	// that docs/clustering.md has no open limitations left. Renders nothing at all on a
	// default single-node deployment (clusterModeEnabled false) rather than an empty-state
	// card - this is opt-in infrastructure (docker-compose.cluster.yml only), most
	// deployments should never see an inert "Cluster" section on their Indexing page.
	//
	// Node health here is per-node errors_count from system.clusters (see
	// ClusterQueryService's remarks on what that does and doesn't mean) - not Keeper quorum
	// health or replication lag, both named as deliberately out of scope for this first cut.
	import * as Card from '$lib/components/ui/card';
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import { indexingContext } from '$lib/indexing/context';
	import CircleCheckIcon from '@lucide/svelte/icons/circle-check';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';

	const indexing = indexingContext.get();

	const status = $derived(indexing.clusterStatus);

	// Grouped by shard for display - "shard 1: node A, node B" reads more like the actual
	// topology than a flat table would, matching how docs/clustering.md's own ASCII diagram
	// presents it.
	const shards = $derived.by(() => {
		const nodes = status?.nodes ?? [];
		const byShardNum = new Map<number, typeof nodes>();
		for (const node of nodes) {
			const group = byShardNum.get(node.shardNum);
			if (group) {
				group.push(node);
			} else {
				byShardNum.set(node.shardNum, [node]);
			}
		}
		return [...byShardNum.entries()].sort(([a], [b]) => a - b);
	});
</script>

{#if status?.clusterModeEnabled}
	<div class="px-4 pb-4">
		<div class="mb-2 flex items-center justify-between">
			<h2 class="text-sm font-medium">Cluster</h2>
			<Badge variant={status.sharedPatternStoreEnabled ? 'secondary' : 'outline'}>
				Shared pattern store {status.sharedPatternStoreEnabled ? 'on' : 'off'}
			</Badge>
		</div>
		<Card.Root>
			<Card.Content class="px-0 py-0">
				{#if status.nodes.length === 0}
					<p class="text-muted-foreground px-4 py-3 text-sm">
						Cluster mode is on, but <code class="font-mono">system.clusters</code> wasn't queryable just now - try refreshing.
					</p>
				{:else}
					<Table.Root>
						<Table.Header>
							<Table.Row>
								<Table.Head>Shard</Table.Head>
								<Table.Head>Host</Table.Head>
								<Table.Head>Replica</Table.Head>
								<Table.Head>Status</Table.Head>
							</Table.Row>
						</Table.Header>
						<Table.Body>
							{#each shards as [shardNum, nodes] (shardNum)}
								{#each nodes as node, i (node.hostName + node.port)}
									<Table.Row>
										{#if i === 0}
											<Table.Cell rowspan={nodes.length} class="text-muted-foreground align-top font-medium">
												Shard {shardNum}
											</Table.Cell>
										{/if}
										<Table.Cell class="font-mono text-xs">
											{node.hostName}:{node.port}
											{#if node.isLocal}
												<span class="text-muted-foreground">(local)</span>
											{/if}
										</Table.Cell>
										<Table.Cell class="text-muted-foreground text-xs">replica {node.replicaNum}</Table.Cell>
										<Table.Cell>
											{#if node.errorsCount === 0}
												<Badge variant="secondary"><CircleCheckIcon data-icon="inline-start" />Healthy</Badge>
											{:else}
												<Badge variant="warning" title="{node.errorsCount} connection error(s) recorded by the node that answered this request">
													<TriangleAlertIcon data-icon="inline-start" />{node.errorsCount} error{node.errorsCount === 1 ? '' : 's'}
												</Badge>
											{/if}
										</Table.Cell>
									</Table.Row>
								{/each}
							{/each}
						</Table.Body>
					</Table.Root>
				{/if}
			</Card.Content>
		</Card.Root>
	</div>
{/if}
