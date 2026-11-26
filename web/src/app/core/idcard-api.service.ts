import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, throwError } from 'rxjs';
import { API_BASE_URL } from './api-base-url';
import { ApiProblem, IdCard, PagedResult, SaveIdCardRequest, ScanResult } from './models';

/** Typed client for the IdCardReader API. */
@Injectable({ providedIn: 'root' })
export class IdCardApiService {
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly http = inject(HttpClient);

  /** Sends both card sides for OCR. Images are not stored server side. */
  scan(frontImage: File, backImage: File): Observable<ScanResult> {
    const form = new FormData();
    form.append('frontImage', frontImage, frontImage.name);
    form.append('backImage', backImage, backImage.name);

    return this.http
      .post<ScanResult>(`${this.baseUrl}/api/idcards/scan`, form)
      .pipe(catchError((error) => throwError(() => new ApiError(error))));
  }

  create(request: SaveIdCardRequest): Observable<IdCard> {
    return this.http
      .post<IdCard>(`${this.baseUrl}/api/idcards`, request)
      .pipe(catchError((error) => throwError(() => new ApiError(error))));
  }

  list(search: string, page: number, pageSize: number): Observable<PagedResult<IdCard>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);

    const term = search.trim();
    if (term.length > 0) {
      params = params.set('search', term);
    }

    return this.http
      .get<PagedResult<IdCard>>(`${this.baseUrl}/api/idcards`, { params })
      .pipe(catchError((error) => throwError(() => new ApiError(error))));
  }

  getById(id: number): Observable<IdCard> {
    return this.http
      .get<IdCard>(`${this.baseUrl}/api/idcards/${id}`)
      .pipe(catchError((error) => throwError(() => new ApiError(error))));
  }
}

/**
 * Normalised API failure. `code` is the translatable code returned by the API
 * (`invalid_national_id`, `duplicate_national_id`, ...) or a synthetic one for transport errors.
 */
export class ApiError extends Error {
  readonly code: string;
  readonly status: number;
  readonly fieldErrors: Record<string, string[]>;

  constructor(error: unknown) {
    const httpError = error instanceof HttpErrorResponse ? error : null;
    const problem = (httpError?.error ?? {}) as ApiProblem;

    const code = problem.code ?? (httpError?.status === 0 ? 'network_error' : 'unknown_error');

    super(code);
    this.name = 'ApiError';
    this.code = code;
    this.status = httpError?.status ?? 0;
    this.fieldErrors = problem.errors ?? {};
  }
}

/** Extracts the translatable code from an unknown error. */
export function errorCodeOf(error: unknown): string {
  return error instanceof ApiError ? error.code : 'unknown_error';
}
