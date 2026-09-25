<script lang="ts">
	// Bucket width / query step picker shared by VolumeChart (Logs) and MetricChart/
	// FormulaChart (Metrics) - "Auto" plus the fixed ladder the auto-pick itself snaps to
	// (BUCKET_WIDTH_OPTIONS_SECONDS). Rungs that would produce more than MAX_BUCKET_COUNT
	// buckets over the last-queried range are disabled; a saved choice that's since become
	// too fine is raised by resolveBucketWidthSeconds instead, which is why the trigger
	// shows the width actually used (`effectiveSeconds`), not just the stored choice.
	import * as DropdownMenu from '$lib/components/ui/dropdown-menu';
	import {
		BUCKET_WIDTH_OPTIONS_SECONDS,
		formatBucketWidthSeconds,
		isBucketWidthAllowed
	} from '$lib/logs/bucket-width';
	import * as m from '$lib/paraglide/messages';

	let {
		value,
		effectiveSeconds,
		rangeSeconds,
		onChange
	}: {
		/** The stored choice - `null` = auto. */
		value: number | null;
		/** The width the displayed data was actually fetched with. */
		effectiveSeconds: number;
		/** Duration of the last-queried range, for disabling too-fine rungs. `null` = unknown yet (nothing disabled). */
		rangeSeconds: number | null;
		onChange: (seconds: number | null) => void;
	} = $props();

	const AUTO = 'auto';

	const label = $derived.by(() => {
		const interval = formatBucketWidthSeconds(effectiveSeconds);
		if (value == null) return m.metricChart_intervalLabel({ interval: m.bucketInterval_auto({ interval }) });
		return value === effectiveSeconds ? m.metricChart_intervalLabel({ interval }) : m.bucketInterval_raised({ interval });
	});
</script>

<DropdownMenu.Root>
	<DropdownMenu.Trigger
		class="hover:text-foreground decoration-muted-foreground/50 underline decoration-dotted underline-offset-2"
		title={m.bucketInterval_heading()}
	>
		{label}
	</DropdownMenu.Trigger>
	<DropdownMenu.Content class="w-44" align="start">
		<DropdownMenu.Label>{m.bucketInterval_heading()}</DropdownMenu.Label>
		<DropdownMenu.RadioGroup
			value={value == null ? AUTO : String(value)}
			onValueChange={(v) => v && onChange(v === AUTO ? null : Number(v))}
		>
			<DropdownMenu.RadioItem value={AUTO}>{m.bucketInterval_autoOption()}</DropdownMenu.RadioItem>
			{#each BUCKET_WIDTH_OPTIONS_SECONDS as option (option)}
				<DropdownMenu.RadioItem
					value={String(option)}
					disabled={rangeSeconds != null && !isBucketWidthAllowed(rangeSeconds, option)}
				>
					{formatBucketWidthSeconds(option)}
				</DropdownMenu.RadioItem>
			{/each}
		</DropdownMenu.RadioGroup>
	</DropdownMenu.Content>
</DropdownMenu.Root>
