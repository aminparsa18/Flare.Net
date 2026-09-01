// Hand-written adapters between MemoryPack's generated numeric `const enum`s and the
// string-literal union types this dashboard's components already compare against (e.g.
// `auth.currentUser.role === 'Admin'` in `+layout.svelte`/`nav-links.ts`).
//
// MemoryPack's TypeScript generator encodes a C# enum as its raw numeric ordinal (`const
// enum UserRole { Admin = 0, Member = 1, Viewer = 2 }` - see
// `$lib/generated/memorypack/UserRole.ts`), never its member name - unlike the JSON path,
// which used `UseStringEnumConverter` to put the name itself on the wire. Every one of
// this dashboard's ~20 components that reads a role/status/kind field does so as a string
// literal, not a number, and none of those components are in this migration's scope (only
// the 16 `lib/*-api.ts` + `api.ts` client files are - see
// docs-internal/investigations/memorypack-serialization-migration-scope.md's Phase 2
// section). Rather than widen the diff to every consumer, every hand-written `-api.ts`
// function that touches an enum-bearing field converts at the boundary: decode the
// generated class, map its numeric field through this file back to the exact same string
// literal the JSON path always produced, and return the same public interface shape this
// module already exported - zero change needed outside `lib/`.

import { UserRole } from '$lib/generated/memorypack/UserRole.js';

/** Matches `AuthModels.cs`'s `Flare.Identity.Users.UserRole` member order exactly - see that file's remarks on why renaming a member is a schema/claims-breaking change, not just a rename (so this order is exactly as stable as the enum itself). */
const USER_ROLE_NAMES = ['Admin', 'Member', 'Viewer'] as const;

export type UserRoleName = (typeof USER_ROLE_NAMES)[number];

export function userRoleToString(value: UserRole): UserRoleName {
	return USER_ROLE_NAMES[value];
}

export function userRoleFromString(value: UserRoleName): UserRole {
	return USER_ROLE_NAMES.indexOf(value) as UserRole;
}
