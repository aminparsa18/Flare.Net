// Thrown by the terminal commands' flag parsers; run() catches it and prints the message
// as an error line. Shared so duration.ts/search.ts/traces.ts don't each declare their own.
export class UsageError extends Error {}
