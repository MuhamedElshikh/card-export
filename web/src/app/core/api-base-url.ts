import { InjectionToken } from '@angular/core';

/** Base URL of the IdCardReader API. Override in app.config.ts for other environments. */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL');
