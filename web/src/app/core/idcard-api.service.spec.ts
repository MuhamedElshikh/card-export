import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { describe, expect, it, beforeEach, afterEach } from 'vitest';
import { API_BASE_URL } from './api-base-url';
import { ApiError, IdCardApiService, errorCodeOf } from './idcard-api.service';

describe('IdCardApiService', () => {
  let service: IdCardApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://localhost:5099' },
      ],
    });

    service = TestBed.inject(IdCardApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
  });

  it('posts the image as multipart form data', () => {
    const file = new File([new Uint8Array([0xff, 0xd8, 0xff])], 'card.jpg', { type: 'image/jpeg' });

    service.scan(file).subscribe();

    const request = http.expectOne('http://localhost:5099/api/idcards/scan');
    expect(request.request.method).toBe('POST');
    expect(request.request.body instanceof FormData).toBe(true);
    request.flush({ nationalId: null, lowConfidence: true });
  });

  it('sends paging and search parameters', () => {
    service.list(' ahmed ', 2, 10).subscribe();

    const request = http.expectOne(
      'http://localhost:5099/api/idcards?page=2&pageSize=10&search=ahmed',
    );
    expect(request.request.method).toBe('GET');
    request.flush({ items: [], page: 2, pageSize: 10, totalCount: 0, totalPages: 0 });
  });

  it('maps a problem response to a translatable code', () => {
    let code = '';

    service.create({ nationalId: '29001011234567', fullName: 'X', address: '', lowConfidence: false }).subscribe({
      error: (error: unknown) => {
        code = errorCodeOf(error);
        expect(error).toBeInstanceOf(ApiError);
      },
    });

    const request = http.expectOne('http://localhost:5099/api/idcards');
    request.flush(
      { title: 'duplicate_national_id', code: 'duplicate_national_id', status: 409 },
      { status: 409, statusText: 'Conflict' },
    );

    expect(code).toBe('duplicate_national_id');
  });

  it('reports transport failures as network_error', () => {
    let code = '';

    service.getById(1).subscribe({
      error: (error: unknown) => {
        code = errorCodeOf(error);
      },
    });

    const request = http.expectOne('http://localhost:5099/api/idcards/1');
    request.error(new ProgressEvent('error'));

    expect(code).toBe('network_error');
  });
});
