<script lang="ts">
	// Create/edit a synthetic monitor. Same Dialog + reset-on-open $effect shape as
	// OnCallRotationFormDialog.svelte. Validation mirrors SyntheticMonitorRequest.Validate server-side.
	import * as Dialog from '$lib/components/ui/dialog';
	import * as Select from '$lib/components/ui/select';
	import { Button } from '$lib/components/ui/button';
	import { Input } from '$lib/components/ui/input';
	import { Textarea } from '$lib/components/ui/textarea';
	import { Spinner } from '$lib/components/ui/spinner';
	import { syntheticMonitorsContext } from '$lib/synthetic-monitors/context';
	import type { SyntheticMonitorKind, SyntheticMonitorRequest } from '$lib/synthetic-monitors-api';
	import * as m from '$lib/paraglide/messages';

	const monitors = syntheticMonitorsContext.get();

	const open = $derived(monitors.formTarget !== null);
	const isEdit = $derived(monitors.formTarget !== null && monitors.formTarget !== 'new');

	const KINDS: SyntheticMonitorKind[] = ['Http', 'Tcp', 'Tls', 'Dns', 'Udp', 'Icmp'];
	const METHODS = ['GET', 'HEAD', 'POST', 'OPTIONS'];

	let name = $state('');
	let description = $state('');
	let kind = $state<SyntheticMonitorKind>('Http');
	let target = $state('');
	let method = $state('GET');
	let expectedStatusText = $state('0');
	let requestHeaders = $state('');
	let requestBody = $state('');
	let bodyContains = $state('');
	let bodyNotContains = $state('');
	let bodyMatchesRegex = $state('');
	let jsonPath = $state('');
	let jsonPathEquals = $state('');
	let expectedAnswer = $state('');
	let dnsRecord = $state('A');
	let intervalText = $state('60');
	let timeoutText = $state('10');
	let locationsText = $state('');
	let enabled = $state(true);

	$effect(() => {
		const t = monitors.formTarget;
		if (t === 'new') {
			name = '';
			description = '';
			kind = 'Http';
			target = '';
			method = 'GET';
			expectedStatusText = '0';
			requestHeaders = '';
			requestBody = '';
			bodyContains = '';
			bodyNotContains = '';
			bodyMatchesRegex = '';
			jsonPath = '';
			jsonPathEquals = '';
			expectedAnswer = '';
			dnsRecord = 'A';
			intervalText = '60';
			timeoutText = '10';
			locationsText = '';
			enabled = true;
		} else if (t) {
			name = t.name;
			description = t.description;
			kind = t.kind;
			target = t.target;
			method = t.method;
			expectedStatusText = String(t.expectedStatus);
			requestHeaders = t.requestHeaders ?? '';
			requestBody = t.requestBody ?? '';
			bodyContains = t.bodyContains ?? '';
			bodyNotContains = t.bodyNotContains ?? '';
			bodyMatchesRegex = t.bodyMatchesRegex ?? '';
			jsonPath = t.jsonPath ?? '';
			jsonPathEquals = t.jsonPathEquals ?? '';
			expectedAnswer = t.expectedAnswer ?? '';
			dnsRecord = t.kind === 'Dns' && t.method ? t.method : 'A';
			intervalText = String(t.intervalSeconds);
			timeoutText = String(t.timeoutSeconds);
			locationsText = (t.locations ?? []).join(', ');
			enabled = t.enabled;
		}
	});

	const interval = $derived(Number(intervalText));
	const timeout = $derived(Number(timeoutText));
	const expectedStatus = $derived(Number(expectedStatusText));
	const intervalValid = $derived(Number.isInteger(interval) && interval >= 10 && interval <= 86400);
	const timeoutValid = $derived(Number.isInteger(timeout) && timeout >= 1 && timeout <= 120 && timeout <= interval);
	const statusValid = $derived(Number.isInteger(expectedStatus) && (expectedStatus === 0 || (expectedStatus >= 100 && expectedStatus <= 599)));
	const targetValid = $derived.by(() => {
		const text = target.trim();
		if (!text) return false;
		if (kind === 'Http') return /^https?:\/\/[^\s/]+/i.test(text);
		if (kind === 'Tls') return !/[\s/]/.test(text);
		if (kind === 'Dns' || kind === 'Icmp') return !/[\s/@?#]/.test(text) && (!text.includes(':') || /^[0-9a-f:.]+$/i.test(text));
		return /^(\[[^\]]+\]|[^\s/:]+):\d{1,5}$/.test(text);
	});
	const locations = $derived(locationsText.split(/[,\s]+/).filter((l) => l.length > 0));
	const locationsValid = $derived(locations.length <= 20 && locations.every((l) => /^[A-Za-z0-9._-]{1,64}$/.test(l)));
	const canSave = $derived(name.trim().length > 0 && targetValid && intervalValid && timeoutValid && locationsValid && (kind !== 'Http' || statusValid));

	const targetPlaceholder = $derived(
		kind === 'Http' ? 'https://example.com/health' : kind === 'Tcp' ? 'db.internal:5432' : kind === 'Udp' ? 'ntp.internal:123' : kind === 'Tls' ? 'example.com:443' : 'example.com'
	);

	function buildRequest(): SyntheticMonitorRequest {
		return {
			name: name.trim(),
			description: description.trim(),
			enabled,
			kind,
			target: target.trim(),
			method: kind === 'Http' ? method : kind === 'Dns' ? dnsRecord : 'GET',
			expectedStatus: kind === 'Http' ? expectedStatus : 0,
			requestHeaders: kind === 'Http' ? requestHeaders.trim() : '',
			requestBody: (kind === 'Http' && method === 'POST') || kind === 'Udp' ? requestBody : '',
			expectedAnswer: kind === 'Dns' || kind === 'Udp' ? expectedAnswer.trim() : '',
			bodyContains: kind === 'Http' ? bodyContains : '',
			bodyNotContains: kind === 'Http' ? bodyNotContains : '',
			bodyMatchesRegex: kind === 'Http' ? bodyMatchesRegex : '',
			jsonPath: kind === 'Http' ? jsonPath.trim() : '',
			jsonPathEquals: kind === 'Http' && jsonPath.trim() ? jsonPathEquals : '',
			intervalSeconds: interval,
			timeoutSeconds: timeout,
			locations
		};
	}
</script>

<Dialog.Root {open} onOpenChange={(next) => !next && monitors.closeForm()}>
	<Dialog.Content class="max-h-[85vh] w-full overflow-y-auto sm:max-w-lg">
		<Dialog.Header>
			<Dialog.Title>{isEdit ? m.synthetic_titleEdit() : m.synthetic_titleNew()}</Dialog.Title>
		</Dialog.Header>

		<div class="flex flex-col gap-3">
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_nameLabel()}</span>
				<Input bind:value={name} placeholder={m.synthetic_namePlaceholder()} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.alertRuleForm_descriptionLabel()}</span>
				<Textarea bind:value={description} placeholder={m.alertRuleForm_optionalPlaceholder()} rows={2} />
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.synthetic_typeLabel()}</span>
				<Select.Root type="single" value={kind} onValueChange={(v) => (kind = v as SyntheticMonitorKind)}>
					<Select.Trigger class="w-40">{kind}</Select.Trigger>
					<Select.Content>
						{#each KINDS as k (k)}
							<Select.Item value={k} label={k} />
						{/each}
					</Select.Content>
				</Select.Root>
			</div>

			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.synthetic_targetLabel()}</span>
				<Input bind:value={target} placeholder={targetPlaceholder} class="font-mono" aria-invalid={target !== '' && !targetValid} />
				<span class="text-muted-foreground text-xs">
					{kind === 'Http'
						? m.synthetic_targetHintHttp()
						: kind === 'Tcp'
							? m.synthetic_targetHintTcp()
							: kind === 'Dns'
								? m.synthetic_targetHintDns()
								: kind === 'Udp'
									? m.synthetic_targetHintUdp()
									: kind === 'Icmp'
										? m.synthetic_targetHintIcmp()
										: m.synthetic_targetHintTls()}
				</span>
			</div>

			{#if kind === 'Dns'}
				<div class="flex gap-3">
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.synthetic_recordTypeLabel()}</span>
						<Select.Root type="single" value={dnsRecord} onValueChange={(v) => (dnsRecord = v)}>
							<Select.Trigger class="w-28">{dnsRecord}</Select.Trigger>
							<Select.Content>
								{#each ['A', 'AAAA'] as option (option)}
									<Select.Item value={option} label={option} />
								{/each}
							</Select.Content>
						</Select.Root>
					</div>
					<div class="flex flex-1 flex-col gap-1">
						<span class="text-xs font-medium">{m.synthetic_expectedAddressLabel()}</span>
						<Input bind:value={expectedAnswer} maxlength={100} class="font-mono" />
					</div>
				</div>
			{:else if kind === 'Udp'}
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.synthetic_udpPayloadLabel()}</span>
					<Textarea bind:value={requestBody} rows={2} maxlength={1400} class="font-mono text-xs" />
				</div>
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.synthetic_udpReplyContainsLabel()}</span>
					<Input bind:value={expectedAnswer} maxlength={1000} />
				</div>
				<span class="text-muted-foreground -mt-2 text-xs">{m.synthetic_udpHint()}</span>
			{/if}

			{#if kind === 'Http'}
				<div class="flex gap-3">
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.synthetic_methodLabel()}</span>
						<Select.Root type="single" value={method} onValueChange={(v) => (method = v)}>
							<Select.Trigger class="w-32">{method}</Select.Trigger>
							<Select.Content>
								{#each METHODS as option (option)}
									<Select.Item value={option} label={option} />
								{/each}
							</Select.Content>
						</Select.Root>
					</div>
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.synthetic_expectedStatusLabel()}</span>
						<Input type="number" min="0" max="599" step="1" bind:value={expectedStatusText} class="w-28" aria-invalid={!statusValid} />
					</div>
				</div>
				<span class="text-muted-foreground -mt-2 text-xs">{m.synthetic_expectedStatusHint()}</span>

				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.synthetic_headersLabel()}</span>
					<Textarea bind:value={requestHeaders} rows={3} class="font-mono text-xs" placeholder="Authorization: Bearer ..." />
					<span class="text-muted-foreground text-xs">{m.synthetic_headersHint()}</span>
				</div>

				{#if method === 'POST'}
					<div class="flex flex-col gap-1">
						<span class="text-xs font-medium">{m.synthetic_bodyLabel()}</span>
						<Textarea bind:value={requestBody} rows={3} class="font-mono text-xs" />
					</div>
				{/if}

				<div class="flex gap-3">
					<div class="flex flex-1 flex-col gap-1">
						<span class="text-xs font-medium">{m.synthetic_bodyContainsLabel()}</span>
						<Input bind:value={bodyContains} maxlength={1000} />
					</div>
					<div class="flex flex-1 flex-col gap-1">
						<span class="text-xs font-medium">{m.synthetic_bodyNotContainsLabel()}</span>
						<Input bind:value={bodyNotContains} maxlength={1000} />
					</div>
				</div>
				<span class="text-muted-foreground -mt-2 text-xs">{m.synthetic_assertionHint()}</span>

				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.synthetic_bodyRegexLabel()}</span>
					<Input bind:value={bodyMatchesRegex} maxlength={1000} class="font-mono text-xs" />
				</div>
				<div class="flex gap-3">
					<div class="flex flex-1 flex-col gap-1">
						<span class="text-xs font-medium">{m.synthetic_jsonPathLabel()}</span>
						<Input bind:value={jsonPath} maxlength={1000} placeholder="$.data.status" class="font-mono text-xs" />
					</div>
					<div class="flex flex-1 flex-col gap-1">
						<span class="text-xs font-medium">{m.synthetic_jsonPathEqualsLabel()}</span>
						<Input bind:value={jsonPathEquals} maxlength={1000} disabled={!jsonPath.trim()} />
					</div>
				</div>
				<span class="text-muted-foreground -mt-2 text-xs">{m.synthetic_jsonAssertionHint()}</span>
			{/if}

			<div class="flex gap-3">
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.synthetic_intervalLabel()}</span>
					<Input type="number" min="10" max="86400" step="1" bind:value={intervalText} class="w-28" aria-invalid={!intervalValid} />
				</div>
				<div class="flex flex-col gap-1">
					<span class="text-xs font-medium">{m.synthetic_timeoutLabel()}</span>
					<Input type="number" min="1" max="120" step="1" bind:value={timeoutText} class="w-28" aria-invalid={!timeoutValid} />
				</div>
			</div>
			<div class="flex flex-col gap-1">
				<span class="text-xs font-medium">{m.synthetic_locationsLabel()}</span>
				<Input bind:value={locationsText} placeholder="eu-west, us-east" class="font-mono" aria-invalid={!locationsValid} />
				<span class="text-xs {locationsValid ? 'text-muted-foreground' : 'text-destructive'}">
					{locationsValid ? m.synthetic_locationsHint() : m.synthetic_locationsError()}
				</span>
			</div>

			{#if !intervalValid}
				<span class="text-destructive text-xs">{m.synthetic_errorInterval()}</span>
			{:else if !timeoutValid}
				<span class="text-destructive text-xs">{m.synthetic_errorTimeout()}</span>
			{/if}

			{#if monitors.saveError}
				<p class="text-destructive text-xs">{monitors.saveError}</p>
			{/if}
		</div>

		<Dialog.Footer>
			<Button variant="outline" size="sm" onclick={() => monitors.closeForm()}>{m.alertRuleForm_cancel()}</Button>
			<Button size="sm" onclick={() => monitors.save(buildRequest())} disabled={!canSave || monitors.saving}>
				{#if monitors.saving}
					<Spinner class="size-3.5" />
				{/if}
				{isEdit ? m.alertRuleForm_saveChanges() : m.synthetic_create()}
			</Button>
		</Dialog.Footer>
	</Dialog.Content>
</Dialog.Root>
