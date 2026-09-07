<script lang="ts">
	// Planning.md's "Multi-node scaling" follow-up: a dashboard view of cluster mode now
	// that docs/clustering.md has no open limitations left. Renders nothing at all on a
	// default single-node deployment (clusterModeEnabled false) rather than an empty-state
	// card - this is opt-in infrastructure (docker-compose.cluster.yml only), most
	// deployments should never see an inert "Cluster" section on their Indexing page.
	//
	// "Status" is per-node errors_count from system.clusters - reachability, not replication
	// currency (see ClusterQueryService's remarks). "Replication" is the currency signal:
	// queue size + lag seconds from system.replicas, shown as "—" rather than a false "in
	// sync" when replicationInfoAvailable is false. Keeper quorum health is still
	// deliberately out of scope for this cut.
	import * as Card from '$lib/components/ui/card';
	import * as Table from '$lib/components/ui/table';
	import { Badge } from '$lib/components/ui/badge';
	import { indexingContext } from '$lib/indexing/context';
	import CircleCheckIcon from '@lucide/svelte/icons/circle-check';
	import TriangleAlertIcon from '@lucide/svelte/icons/triangle-alert';
	import * as m from '$lib/paraglide/messages';

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
			<h2 class="text-sm font-medium">{m.indexingClusterStatus_heading()}</h2>
			<Badge variant={status.sharedPatternStoreEnabled ? 'secondary' : 'outline'}>
				{status.sharedPatternStoreEnabled
					? m.indexingClusterStatus_sharedPatternStoreOn()
					: m.indexingClusterStatus_sharedPatternStoreOff()}
			</Badge>
		</div>
		<Card.Root>
			<Card.Content class="px-0 py-0">
				{#if status.nodes.length === 0}
					<p class="text-muted-foreground px-4 py-3 text-sm">
						{@html m.indexingClusterStatus_clustersNotQueryable({ table: '<code class="font-mono">system.clusters</code>' })}
					</p>
				{:else}
					<Table.Root>
						<Table.Header>
							<Table.Row>
								<Table.Head>{m.indexingClusterStatus_shardColumn()}</Table.Head>
								<Table.Head>{m.indexingClusterStatus_hostColumn()}</Table.Head>
								<Table.Head>{m.indexingClusterStatus_replicaColumn()}</Table.Head>
								<Table.Head>{m.indexingClusterStatus_statusColumn()}</Table.Head>
								<Table.Head>{m.indexingClusterStatus_replicationColumn()}</Table.Head>
							</Table.Row>
						</Table.Header>
						<Table.Body>
							{#each shards as [shardNum, nodes] (shardNum)}
								{#each nodes as node, i (node.hostName + node.port)}
									<Table.Row>
										{#if i === 0}
											<Table.Cell rowspan={nodes.length} class="text-muted-foreground align-top font-medium">
												{m.indexingClusterStatus_shardLabel({ number: shardNum })}
											</Table.Cell>
										{/if}
										<Table.Cell class="font-mono text-xs">
											{node.hostName}:{node.port}
											{#if node.isLocal}
												<span class="text-muted-foreground">{m.indexingClusterStatus_localSuffix()}</span>
											{/if}
										</Table.Cell>
										<Table.Cell class="text-muted-foreground text-xs">{m.indexingClusterStatus_replicaLabel({ number: node.replicaNum })}</Table.Cell>
										<Table.Cell>
											{#if node.errorsCount === 0}
												<Badge variant="secondary"><CircleCheckIcon data-icon="inline-start" />{m.indexingClusterStatus_healthy()}</Badge>
											{:else}
												<Badge variant="warning" title={m.indexingClusterStatus_errorsTooltip({ count: node.errorsCount })}>
													<TriangleAlertIcon data-icon="inline-start" />
													{node.errorsCount === 1
														? m.indexingClusterStatus_errorCountSingular({ count: node.errorsCount })
														: m.indexingClusterStatus_errorCountPlural({ count: node.errorsCount })}
												</Badge>
											{/if}
										</Table.Cell>
										<Table.Cell>
											{#if !status.replicationInfoAvailable}
												<span class="text-muted-foreground text-xs" title={m.indexingClusterStatus_replicasNotQueryableTooltip()}>
													—
												</span>
											{:else if node.replicationQueueSize === 0 && node.replicationLagSeconds === 0}
												<Badge variant="secondary"><CircleCheckIcon data-icon="inline-start" />{m.indexingClusterStatus_inSync()}</Badge>
											{:else}
												<Badge
													variant="warning"
													title={node.replicationQueueSize === 1
														? m.indexingClusterStatus_replicationLagTooltipSingular({ queueSize: node.replicationQueueSize, lagSeconds: node.replicationLagSeconds })
														: m.indexingClusterStatus_replicationLagTooltipPlural({ queueSize: node.replicationQueueSize, lagSeconds: node.replicationLagSeconds })}
												>
													<TriangleAlertIcon data-icon="inline-start" />
													{m.indexingClusterStatus_queueValue({ queueSize: node.replicationQueueSize, lagSeconds: node.replicationLagSeconds })}
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
