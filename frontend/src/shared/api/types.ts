import type { components } from './schema';

/** Friendly aliases for API schemas. Import from here, not from the generated file. */
export type Schemas = components['schemas'];
export type SystemStatus = Schemas['SystemStatusResponse'];
export type ServiceStatus = Schemas['ServiceStatus'];
