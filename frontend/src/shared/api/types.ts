import type { components } from './schema';

/** Friendly aliases for API schemas. Import from here, not from the generated file. */
export type Schemas = components['schemas'];
export type SystemStatus = Schemas['SystemStatusResponse'];
export type ServiceStatus = Schemas['ServiceStatus'];
export type Problem = Schemas['ProblemDetails'];
export type SessionInfo = Schemas['SessionInfo'];
export type UserInfo = Schemas['UserInfo'];
